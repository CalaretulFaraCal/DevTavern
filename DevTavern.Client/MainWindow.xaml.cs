using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DevTavern.Client
{
    public partial class MainWindow : Window
    {
        private readonly int _currentUserId;
        private readonly HttpClient _apiClient;
        private HubConnection? _hubConnection;

        private readonly string _accessToken;
        private readonly List<RepoItem> _projects;
        private readonly string _username;
        private readonly string _avatarUrl;

        private string? _selectedProject;
        private int _selectedChannelId;
        private bool _membersPanelVisible = false;
        private ChannelItem? _editingChannel = null;

        // Channels per project: projectName -> list of channels
        private readonly Dictionary<string, ObservableCollection<ChannelItem>> _projectChannels = new();

        // Members per project
        private readonly Dictionary<string, ObservableCollection<MemberItem>> _projectMembers = new();

        public ObservableCollection<ChatMessage> Messages { get; set; } = new ObservableCollection<ChatMessage>();

        public MainWindow(string accessToken, List<RepoItem> projects, string username, string avatarUrl, int currentUserId)
        {
            InitializeComponent();

            _currentUserId = currentUserId;
            _apiClient = new HttpClient { BaseAddress = new Uri("https://devtavern.onrender.com/api/") };

            _accessToken = accessToken;
            _projects = projects;
            _username = username;
            _avatarUrl = avatarUrl;

            // Generate icon letters for each project
            foreach (var project in _projects)
            {
                project.IconLetters = GenerateIconLetters(project.name);
            }

            // Populate sidebar
            ProjectList.ItemsSource = _projects;
            MessagesList.ItemsSource = Messages;

            // Set user info
            UsernameText.Text = _username;
            UserInitials.Text = _username.Length >= 2
                ? _username.Substring(0, 2).ToUpper()
                : _username.ToUpper();

            // Load GitHub avatar
            if (!string.IsNullOrEmpty(_avatarUrl))
            {
                try
                {
                    var bitmap = new BitmapImage(new Uri(_avatarUrl));
                    UserAvatarImage.Source = bitmap;
                    UserAvatarImage.Visibility = Visibility.Visible;
                    UserInitials.Visibility = Visibility.Collapsed;
                }
                catch { /* fallback to initials */ }
            }

            MessageInput.Text = "";

            // Show home view on startup
            ShowHomeView();

            this.Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // ---- SignalR Init ----
            _hubConnection = new HubConnectionBuilder()
                .WithUrl("https://devtavern.onrender.com/chat")
                .Build();

            // Primim mesaje live doar pentru canalul in care suntem
            _hubConnection.On<string, string, string>("ReceiveMessage", (senderUsername, senderAvatarUrl, messageContent) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (senderUsername == _username) return;

                    Messages.Add(new ChatMessage
                    {
                        Username = senderUsername,
                        Initials = senderUsername.Length >= 2 ? senderUsername.Substring(0, 2).ToUpper() : senderUsername.ToUpper(),
                        AvatarColor = "#8B949E",
                        UsernameColor = "#E6EDF3",
                        AvatarUrl = string.IsNullOrEmpty(senderAvatarUrl) ? null : senderAvatarUrl,
                        Content = messageContent,
                        Timestamp = DateTime.Now.ToString("HH:mm"),
                        IsSystemMessage = false,
                        IsMentioningMe = messageContent.Contains("@" + _username, StringComparison.OrdinalIgnoreCase)
                    });
                    Application.Current.Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToEnd(),
                        System.Windows.Threading.DispatcherPriority.Background);
                });
            });

            // Primim notificare cand s-a creat un canal nou in tot proiectul
            _hubConnection.On<int, string>("ChannelCreated", (channelId, channelName) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_selectedProject != null && _projectChannels.TryGetValue(_selectedProject, out var channels))
                    {
                        if (!channels.Any(c => c.Id == channelId))
                        {
                            channels.Add(new ChannelItem { Id = channelId, Name = channelName });
                        }
                    }
                });
            });

            try 
            { 
                await _hubConnection.StartAsync(); 
                ChatSubtitle.Text = "Connected to Taverna Link";
            } 
            catch
            {
                ChatSubtitle.Text = "Offline Mode (Real-time sync disabled)";
            }

            // ---- Sync Projects & Channels with DB ----
            foreach (var project in _projects)
            {
                try
                {
                    // POST /api/projects — creaza proiectul sau il returneaza daca exista deja
                    var pData = new { GitHubRepoId = project.id, Name = project.name };
                    var pContent = new StringContent(JsonConvert.SerializeObject(pData), System.Text.Encoding.UTF8, "application/json");
                    var pResp = await _apiClient.PostAsync("projects", pContent);
                    if (pResp.IsSuccessStatusCode)
                    {
                        var pJson = JObject.Parse(await pResp.Content.ReadAsStringAsync());
                        project.DbId = pJson["id"]?.ToObject<int>() ?? 0;

                        // POST /api/channels/generate-defaults/{projectId}
                        // Serverul genereaza 2 canale default (general-tech, off-topic-lounge) sau le returneaza pe cele existente
                        var cResp = await _apiClient.PostAsync($"channels/generate-defaults/{project.DbId}", null);
                        if (cResp.IsSuccessStatusCode)
                        {
                            var cArr = JArray.Parse(await cResp.Content.ReadAsStringAsync());
                            var channelsList = new ObservableCollection<ChannelItem>();
                            foreach (var c in cArr)
                            {
                                channelsList.Add(new ChannelItem
                                {
                                    Id = c["id"]?.ToObject<int>() ?? 0,
                                    Name = c["name"]?.ToString() ?? ""
                                });
                            }
                            _projectChannels[project.name] = channelsList;
                        }
                    }

                    // Members — fetch collaborators from GitHub API
                    _projectMembers[project.name] = new ObservableCollection<MemberItem>();
                    try
                    {
                        using var ghClient = new HttpClient();
                        ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                        ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                        // GitHub API: GET /repos/{owner}/{repo}/collaborators
                        var collabResp = await ghClient.GetAsync($"https://api.github.com/repos/{project.fullName}/collaborators");
                        
                        if (!collabResp.IsSuccessStatusCode)
                        {
                            // Daca nu merge collaborators (403 pt non-admin), incercam contributors
                            collabResp = await ghClient.GetAsync($"https://api.github.com/repos/{project.fullName}/contributors");
                        }

                        if (collabResp.IsSuccessStatusCode)
                        {
                            var collabJson = JArray.Parse(await collabResp.Content.ReadAsStringAsync());
                            foreach (var collab in collabJson)
                            {
                                string memberUsername = collab["login"]?.ToString() ?? "";
                                string memberAvatar = collab["avatar_url"]?.ToString() ?? "";
                                if (string.IsNullOrEmpty(memberUsername)) continue;

                                string role = memberUsername == _username ? "You" : "Collaborator";

                                // Verificam daca user-ul are permisiuni admin/owner
                                var permissions = collab["permissions"];
                                if (permissions != null && permissions["admin"]?.ToObject<bool>() == true)
                                {
                                    role = memberUsername == _username ? "Owner (You)" : "Owner";
                                }

                                _projectMembers[project.name].Add(new MemberItem
                                {
                                    Username = memberUsername,
                                    Initials = memberUsername.Length >= 2 ? memberUsername.Substring(0, 2).ToUpper() : memberUsername.ToUpper(),
                                    Role = role,
                                    IsOnline = memberUsername == _username,
                                    AvatarUrl = memberAvatar
                                });
                            }
                        }
                    }
                    catch { }

                    // Daca lista e goala (eroare API), adaugam cel putin utilizatorul curent
                    if (_projectMembers[project.name].Count == 0)
                    {
                        _projectMembers[project.name].Add(new MemberItem
                        {
                            Username = _username,
                            Initials = UserInitials.Text,
                            Role = "Owner",
                            IsOnline = true,
                            AvatarUrl = _avatarUrl
                        });
                    }
                }
                catch { }
            }
        }

        private string GenerateIconLetters(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";

            var parts = name.Split(new[] { '-', '_', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
            {
                return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}";
            }

            return name.Length >= 2
                ? name.Substring(0, 2).ToUpper()
                : name.ToUpper();
        }

        private async void ProjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProjectList.SelectedItem is RepoItem selected)
            {
                // ---- SignalR Join/Leave Project Group ----
                string? oldProjectDbId = _projects.FirstOrDefault(p => p.name == _selectedProject)?.DbId.ToString();
                string newProjectDbId = selected.DbId.ToString();

                _selectedProject = selected.name;
                SelectedProjectName.Text = selected.name;

                try
                {
                    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                    {
                        if (oldProjectDbId != null)
                        {
                            await _hubConnection.InvokeAsync("LeaveProject", oldProjectDbId);
                        }
                        await _hubConnection.InvokeAsync("JoinProject", newProjectDbId);
                    }
                }
                catch { }

                ChannelsSectionHeader.Visibility = Visibility.Visible;
                ChannelList.Visibility = Visibility.Visible;

                if (_projectChannels.TryGetValue(selected.name, out var channels))
                {
                    ChannelList.ItemsSource = channels;
                    if (channels.Count > 0)
                    {
                        ChannelList.SelectedIndex = 0;
                    }
                }

                if (_projectMembers.TryGetValue(selected.name, out var members))
                {
                    RefreshMembersList();
                }

                HomeView.Visibility = Visibility.Collapsed;
                ChatView.Visibility = Visibility.Visible;
            }
        }

        private void RefreshMembersList()
        {
            if (string.IsNullOrEmpty(_selectedProject) || !_projectMembers.TryGetValue(_selectedProject, out var baseMembers))
            {
                MembersList.ItemsSource = null;
                return;
            }

            // Define highest priority hierarchy (top to bottom)
            var hierarchy = new List<string> 
            { 
                "Project Manager", 
                "DevOps", 
                "Fullstack Developer", 
                "Backend Developer", 
                "Frontend Developer", 
                "UI/UX Designer", 
                "QA / Tester" 
            };

            var groupedList = new ObservableCollection<MemberItem>();
            var memberRoles = new Dictionary<MemberItem, string>();

            // 1. Assign each member their primary role
            foreach (var m in baseMembers)
            {
                if (m.IsHeader) continue; // safety check

                string primaryRole = "ONLINE";
                if (m.DevRoles != null && m.DevRoles.Count > 0)
                {
                    foreach (var h in hierarchy)
                    {
                        if (m.DevRoles.Contains(h)) { primaryRole = h; break; }
                    }
                }
                
                // Hide 'Collaborator' / 'You' by overwriting the visual title with their DevRole or 'Member'
                m.Role = primaryRole == "ONLINE" ? "Member" : primaryRole;
                memberRoles[m] = primaryRole;
            }

            // 2. Build the visual list inserting Header objects
            var allCategories = hierarchy.ToList();
            allCategories.Add("ONLINE");

            foreach (var cat in allCategories)
            {
                var membersInCat = baseMembers.Where(m => !m.IsHeader && memberRoles.ContainsKey(m) && memberRoles[m] == cat)
                                              .OrderBy(m => m.Username).ToList();

                if (membersInCat.Count > 0)
                {
                    // Add Category Header
                    groupedList.Add(new MemberItem
                    {
                        Username = $"{cat.ToUpper()} — {membersInCat.Count}",
                        IsHeader = true
                    });

                    // Add Members
                    foreach (var m in membersInCat)
                    {
                        groupedList.Add(m);
                    }
                }
            }

            MembersList.ItemsSource = null;
            MembersList.ItemsSource = groupedList;
        }

        private void HomeButton_Click(object sender, MouseButtonEventArgs e)
        {
            ShowHomeView();
        }

        private void ShowHomeView()
        {
            ProjectList.SelectedIndex = -1;
            _selectedProject = null;
            _selectedChannelId = 0;

            SelectedProjectName.Text = "Home";
            ChannelsSectionHeader.Visibility = Visibility.Collapsed;
            ChannelList.Visibility = Visibility.Collapsed;
            ChannelList.ItemsSource = null;

            ChatView.Visibility = Visibility.Collapsed;
            HomeView.Visibility = Visibility.Visible;

            HomeWelcomeText.Text = $"Welcome back, {_username}!";
            HomeProjectCountText.Text = $"{_projects.Count} project{(_projects.Count != 1 ? "s" : "")} imported";
            HomeProjectList.ItemsSource = _projects;

            _membersPanelVisible = false;
            MembersPanelColumn.Width = new GridLength(0);
            MembersPanelBorder.Visibility = Visibility.Collapsed;
        }

        private async void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChannelList.SelectedItem is ChannelItem selectedChannel && _selectedProject != null)
            {
                // ---- Leave old group and Join new group in SignalR ----
                int oldChannelId = _selectedChannelId;
                _selectedChannelId = selectedChannel.Id;

                try
                {
                    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                    {
                        if (oldChannelId > 0)
                        {
                            await _hubConnection.InvokeAsync("LeaveChannel", oldChannelId.ToString());
                        }
                        await _hubConnection.InvokeAsync("JoinChannel", _selectedChannelId.ToString());
                    }
                }
                catch { }

                ChatTitle.Text = selectedChannel.Name;
                ChatSubtitle.Text = $"{_selectedProject} · #{selectedChannel.Name}";

                Messages.Clear();

                // GET /api/messages/channel/{channelId}
                // Nota: serverul foloseste Repository generic fara Include, deci User vine null.
                // Folosim userId numeric si cautam username-ul local cand e posibil.
                try
                {
                    var mResp = await _apiClient.GetStringAsync($"messages/channel/{selectedChannel.Id}");
                    var mArr = JArray.Parse(mResp);
                    foreach (var m in mArr)
                    {
                        string content = m["content"]?.ToString() ?? "";
                        string time = "";
                        try
                        {
                            time = m["sentAt"]?.ToObject<DateTime>().ToLocalTime().ToString("HH:mm") ?? "";
                        }
                        catch { time = DateTime.Now.ToString("HH:mm"); }

                        // Serverul nu include navigation properties (User) => user va fi null
                        // Verificam daca userId corespunde utilizatorului curent
                        int msgUserId = m["userId"]?.ToObject<int>() ?? 0;
                        string msgUsername;
                        string msgAvatarUrl;

                        if (msgUserId == _currentUserId)
                        {
                            msgUsername = _username;
                            msgAvatarUrl = _avatarUrl;
                        }
                        else
                        {
                            // Incercam sa citim user-ul din raspuns (poate serverul il include)
                            msgUsername = m["user"]?["username"]?.ToString() ?? $"User#{msgUserId}";
                            msgAvatarUrl = m["user"]?["avatarUrl"]?.ToString() ?? "";
                        }

                        Messages.Add(new ChatMessage
                        {
                            Username = msgUsername,
                            Initials = msgUsername.Length >= 2 ? msgUsername.Substring(0, 2).ToUpper() : msgUsername.ToUpper(),
                            AvatarColor = msgUserId == _currentUserId ? "#238636" : "#8B949E",
                            UsernameColor = msgUserId == _currentUserId ? "#238636" : "#E6EDF3",
                            AvatarUrl = string.IsNullOrEmpty(msgAvatarUrl) ? null : msgAvatarUrl,
                            Content = content,
                            Timestamp = time,
                            IsSystemMessage = false,
                            IsMentioningMe = content.Contains("@" + _username, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
                catch { }

                if (Messages.Count == 0)
                {
                    Messages.Add(new ChatMessage
                    {
                        IsSystemMessage = true,
                        Content = $"Welcome to #{selectedChannel.Name}! This is the beginning of the conversation.",
                        Timestamp = DateTime.Now.ToString("HH:mm")
                    });
                }

                await Application.Current.Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToEnd(),
                    System.Windows.Threading.DispatcherPriority.Background);
                MessageInput.Focus();
            }
        }

        private void ToggleMembersButton_Click(object sender, RoutedEventArgs e)
        {
            _membersPanelVisible = !_membersPanelVisible;

            if (_membersPanelVisible)
            {
                MembersPanelColumn.Width = new GridLength(240);
                MembersPanelBorder.Visibility = Visibility.Visible;
            }
            else
            {
                MembersPanelColumn.Width = new GridLength(0);
                MembersPanelBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void AddChannelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProject == null)
            {
                MessageBox.Show("Select a project first.", "DevTavern", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            NewChannelNameInput.Text = "";
            AddChannelOverlay.Visibility = Visibility.Visible;
            NewChannelNameInput.Focus();
        }

        private void CancelAddChannel_Click(object sender, RoutedEventArgs e)
        {
            AddChannelOverlay.Visibility = Visibility.Collapsed;
        }

        private void AddChannelOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            AddChannelOverlay.Visibility = Visibility.Collapsed;
        }

        private void ConfirmAddChannel_Click(object sender, RoutedEventArgs e)
        {
            CreateChannel();
        }

        private void NewChannelNameInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CreateChannel();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                AddChannelOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

        // POST /api/channels/project/{projectId} — creaza un canal custom in DB
        private async void CreateChannel()
        {
            var channelName = NewChannelNameInput.Text?.Trim().ToLower().Replace(" ", "-");
            if (string.IsNullOrEmpty(channelName) || _selectedProject == null) return;

            var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
            if (proj == null || proj.DbId == 0) return;

            if (_projectChannels.TryGetValue(_selectedProject, out var channels))
            {
                if (channels.Any(c => c.Name == channelName))
                {
                    MessageBox.Show("A channel with this name already exists.", "DevTavern",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var postData = new { Name = channelName, Type = 0 };
                    var content = new StringContent(JsonConvert.SerializeObject(postData), System.Text.Encoding.UTF8, "application/json");
                    var resp = await _apiClient.PostAsync($"channels/project/{proj.DbId}", content);

                    if (resp.IsSuccessStatusCode)
                    {
                        var cJson = JObject.Parse(await resp.Content.ReadAsStringAsync());
                        var newChannel = new ChannelItem
                        {
                            Id = cJson["id"]?.ToObject<int>() ?? 0,
                            Name = channelName
                        };
                        channels.Add(newChannel);
                        ChannelList.SelectedItem = newChannel;

                        // Notificam restul utilizatorilor care sunt in aceleasi proiect
                        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                        {
                            await _hubConnection.InvokeAsync("NotifyChannelCreated", proj.DbId.ToString(), newChannel.Id, newChannel.Name);
                        }
                    }
                }
                catch { }
            }

            AddChannelOverlay.Visibility = Visibility.Collapsed;
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        // ========== @Mention Autocomplete ==========

        private bool _isMentioning = false;
        private int _mentionStartIndex = -1;

        private void MessageInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var text = MessageInput.Text ?? "";
            var caretIndex = MessageInput.CaretIndex;

            // Find the @ character before the caret
            int atIndex = -1;
            for (int i = caretIndex - 1; i >= 0; i--)
            {
                if (text[i] == '@')
                {
                    atIndex = i;
                    break;
                }
                if (text[i] == ' ') break; // stop at space
            }

            if (atIndex >= 0)
            {
                string query = text.Substring(atIndex + 1, caretIndex - atIndex - 1).ToLower();
                _isMentioning = true;
                _mentionStartIndex = atIndex;

                // Filter members matching the query
                if (_selectedProject != null && _projectMembers.TryGetValue(_selectedProject, out var members))
                {
                    var filtered = members.Where(m => m.Username.ToLower().Contains(query)).ToList();
                    if (filtered.Count > 0)
                    {
                        MentionPopup.ItemsSource = filtered;
                        MentionPopup.SelectedIndex = 0;
                        MentionPopup.Visibility = Visibility.Visible;
                        return;
                    }
                }
            }

            _isMentioning = false;
            _mentionStartIndex = -1;
            MentionPopup.Visibility = Visibility.Collapsed;
        }

        private void MentionPopup_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Triggered by click
            if (MentionPopup.SelectedItem is MemberItem member && _isMentioning && System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                InsertMention(member.Username);
            }
        }

        private void MentionPopup_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (MentionPopup.SelectedItem is MemberItem member)
                {
                    InsertMention(member.Username);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                MentionPopup.Visibility = Visibility.Collapsed;
                _isMentioning = false;
                MessageInput.Focus();
                e.Handled = true;
            }
        }

        private void InsertMention(string username)
        {
            var text = MessageInput.Text ?? "";
            if (_mentionStartIndex < 0 || _mentionStartIndex >= text.Length) return;

            var caretIndex = MessageInput.CaretIndex;
            string before = text.Substring(0, _mentionStartIndex);
            string after = caretIndex < text.Length ? text.Substring(caretIndex) : "";
            string newText = before + "@" + username + " " + after;

            MessageInput.Text = newText;
            MessageInput.CaretIndex = before.Length + 1 + username.Length + 1;

            _isMentioning = false;
            _mentionStartIndex = -1;
            MentionPopup.Visibility = Visibility.Collapsed;
            MessageInput.Focus();
        }

        private void MessageInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (_isMentioning && MentionPopup.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Down)
                {
                    if (MentionPopup.SelectedIndex < MentionPopup.Items.Count - 1) MentionPopup.SelectedIndex++;
                    e.Handled = true; return;
                }
                else if (e.Key == Key.Up)
                {
                    if (MentionPopup.SelectedIndex > 0) MentionPopup.SelectedIndex--;
                    e.Handled = true; return;
                }
                else if (e.Key == Key.Tab || e.Key == Key.Enter)
                {
                    if (MentionPopup.SelectedItem is MemberItem member)
                    {
                        InsertMention(member.Username);
                        e.Handled = true; return;
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    MentionPopup.Visibility = Visibility.Collapsed;
                    _isMentioning = false;
                    e.Handled = true; return;
                }
            }

            if (e.Key == Key.Enter)
            {
                SendMessage();
                e.Handled = true;
            }
        }

        // POST /api/messages — salveaza mesajul in DB
        // Hub SendLiveMessage — trimite mesajul live la toti
        private async void SendMessage()
        {
            var text = MessageInput.Text?.Trim();
            if (string.IsNullOrEmpty(text) || _selectedProject == null || _selectedChannelId == 0) return;

            // Afisam mesajul local imediat (optimistic UI)
            Messages.Add(new ChatMessage
            {
                Username = _username,
                Initials = _username.Length >= 2 ? _username.Substring(0, 2).ToUpper() : _username.ToUpper(),
                AvatarColor = "#238636",
                UsernameColor = "#238636",
                AvatarUrl = string.IsNullOrEmpty(_avatarUrl) ? null : _avatarUrl,
                Content = text,
                Timestamp = DateTime.Now.ToString("HH:mm"),
                IsSystemMessage = false,
                IsMentioningMe = text.Contains("@" + _username, StringComparison.OrdinalIgnoreCase)
            });

            await Application.Current.Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToEnd(),
                System.Windows.Threading.DispatcherPriority.Background);

            MessageInput.Text = "";
            MessageInput.Focus();

            try
            {
                // Salvare in DB
                var postData = new { Content = text, UserId = _currentUserId, ChannelId = _selectedChannelId };
                var content = new StringContent(JsonConvert.SerializeObject(postData), System.Text.Encoding.UTF8, "application/json");
                await _apiClient.PostAsync("messages", content);

                // Trimitere live catre grupul canalului curent (SignalR)
                if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                {
                    await _hubConnection.InvokeAsync("SendLiveMessage", _selectedChannelId.ToString(), _username, _avatarUrl, text);
                }
            }
            catch { }
        }

        // ========== Channel Settings Handlers ==========

        private void ChannelSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            // The gear button is inside the ListBoxItem ControlTemplate,
            // so we walk up the visual tree to find the ListBoxItem and get its DataContext.
            if (sender is Button btn)
            {
                var listBoxItem = FindParent<ListBoxItem>(btn);
                if (listBoxItem?.DataContext is ChannelItem channel)
                {
                    _editingChannel = channel;
                    SettingsChannelTitle.Text = channel.Name;
                    EditChannelNameInput.Text = channel.Name;

                    // Reset to Overview tab
                    OverviewTabContent.Visibility = Visibility.Visible;
                    PermissionsTabContent.Visibility = Visibility.Collapsed;

                    ChannelSettingsOverlay.Visibility = Visibility.Visible;
                    EditChannelNameInput.Focus();
                    EditChannelNameInput.SelectAll();
                }
            }

            e.Handled = true; // Prevent channel selection from changing
        }

        private static T? FindParent<T>(System.Windows.DependencyObject child) where T : System.Windows.DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && parent is not T)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as T;
        }

        private void CloseChannelSettings_Click(object sender, RoutedEventArgs e)
        {
            ChannelSettingsOverlay.Visibility = Visibility.Collapsed;
            _editingChannel = null;
        }

        private void ChannelSettingsOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ChannelSettingsOverlay.Visibility = Visibility.Collapsed;
            _editingChannel = null;
        }

        private void TabOverview_Click(object sender, RoutedEventArgs e)
        {
            OverviewTabContent.Visibility = Visibility.Visible;
            PermissionsTabContent.Visibility = Visibility.Collapsed;
        }

        private void TabPermissions_Click(object sender, RoutedEventArgs e)
        {
            OverviewTabContent.Visibility = Visibility.Collapsed;
            PermissionsTabContent.Visibility = Visibility.Visible;
        }

        // Redenumeste canalul (doar local)
        private void SaveChannelName_Click(object sender, RoutedEventArgs e)
        {
            if (_editingChannel == null) return;

            var newName = EditChannelNameInput.Text?.Trim().ToLower().Replace(" ", "-");
            if (string.IsNullOrEmpty(newName)) return;

            if (newName == _editingChannel.Name)
            {
                ChannelSettingsOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            // Check if name already exists in project
            if (_selectedProject != null && _projectChannels.TryGetValue(_selectedProject, out var channels))
            {
                if (channels.Any(c => c.Name == newName && c.Id != _editingChannel.Id))
                {
                    MessageBox.Show("A channel with this name already exists.", "DevTavern",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Update local state
            _editingChannel.Name = newName;

            // Refresh the channel list UI
            if (_selectedProject != null && _projectChannels.TryGetValue(_selectedProject, out var ch))
            {
                ChannelList.ItemsSource = null;
                ChannelList.ItemsSource = ch;
            }

            // Update chat header if this is the currently selected channel
            if (_selectedChannelId == _editingChannel.Id)
            {
                ChatTitle.Text = newName;
                ChatSubtitle.Text = $"{_selectedProject} · #{newName}";
            }

            SettingsChannelTitle.Text = newName;
        }

        // Sterge canalul (doar local) — arata dialog custom de confirmare
        private void DeleteChannel_Click(object sender, RoutedEventArgs e)
        {
            if (_editingChannel == null || _selectedProject == null) return;

            DeleteConfirmMessage.Text = $"Are you sure you want to delete #{_editingChannel.Name}?\nThis action cannot be undone and all messages will be lost.";
            DeleteConfirmOverlay.Visibility = Visibility.Visible;
        }

        private void DeleteConfirmCancel_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmOverlay.Visibility = Visibility.Collapsed;
        }

        private void DeleteConfirmOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
        {
            DeleteConfirmOverlay.Visibility = Visibility.Collapsed;
        }

        private void DeleteConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmOverlay.Visibility = Visibility.Collapsed;

            if (_editingChannel == null || _selectedProject == null) return;

            // Remove from local collection
            if (_projectChannels.TryGetValue(_selectedProject, out var channels))
            {
                channels.Remove(_editingChannel);

                // If we deleted the currently selected channel, switch to first available
                if (_selectedChannelId == _editingChannel.Id)
                {
                    if (channels.Count > 0)
                    {
                        ChannelList.SelectedIndex = 0;
                    }
                    else
                    {
                        Messages.Clear();
                        ChatTitle.Text = "No channels";
                        ChatSubtitle.Text = "Create a channel to start chatting";
                    }
                }
            }

            ChannelSettingsOverlay.Visibility = Visibility.Collapsed;
            _editingChannel = null;
        }

        // ========== Logout ==========

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            // Disconnect SignalR
            if (_hubConnection != null)
            {
                try { await _hubConnection.StopAsync(); } catch { }
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }

            // Stergem token-ul salvat local pentru a forta re-autentificarea
            Services.GitHubAuthService.ClearTokenCache();

            // Open a fresh login window
            var loginWindow = new LoginWindow();
            loginWindow.Show();

            // Close this window
            this.Close();
        }
        
        // ========== Set Roles Overlay ==========

        private void SetRoleButton_Click(object sender, RoutedEventArgs e)
        {
            // Reset checkboxes
            foreach (UIElement child in RoleCheckboxesContainer.Children)
            {
                if (child is System.Windows.Controls.CheckBox cb)
                {
                    cb.IsChecked = false;
                }
            }
            
            // Check the ones the user already has
            if (_selectedProject != null && _projectMembers.TryGetValue(_selectedProject, out var members))
            {
                var me = members.FirstOrDefault(m => m.Username == _username);
                if (me != null && me.DevRoles != null)
                {
                    foreach (UIElement child in RoleCheckboxesContainer.Children)
                    {
                        if (child is System.Windows.Controls.CheckBox cb && cb.Content is string cbText)
                        {
                            if (me.DevRoles.Contains(cbText)) cb.IsChecked = true;
                        }
                    }
                }
            }

            RoleSelectionOverlay.Visibility = Visibility.Visible;
        }

        private void RoleSelectionOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
        {
            RoleSelectionOverlay.Visibility = Visibility.Collapsed;
        }

        private void SaveRoles_Click(object sender, RoutedEventArgs e)
        {
            RoleSelectionOverlay.Visibility = Visibility.Collapsed;

            if (_selectedProject != null && _projectMembers.TryGetValue(_selectedProject, out var members))
            {
                var me = members.FirstOrDefault(m => m.Username == _username);
                if (me != null)
                {
                    me.DevRoles.Clear();
                    foreach (UIElement child in RoleCheckboxesContainer.Children)
                    {
                        if (child is System.Windows.Controls.CheckBox cb && cb.IsChecked == true && cb.Content is string cbText)
                        {
                            me.DevRoles.Add(cbText);
                        }
                    }

                    // Refresh binding for members list
                    RefreshMembersList();
                }
            }
        }

        // ========== Import Projects Overlay ==========

        public ObservableCollection<RepoItem> ImportableRepos { get; set; } = new ObservableCollection<RepoItem>();

        private async void AddProjectButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ImportProjectsOverlay.Visibility = Visibility.Visible;
            ImportStatusText.Text = "Loading your GitHub repositories...";
            ImportableRepos.Clear();
            ImportProjectsList.ItemsSource = ImportableRepos;

            try
            {
                var response = await _apiClient.GetStringAsync($"projects/github/my-projects?githubPersonalAccessToken={_accessToken}");
                var jsonArray = Newtonsoft.Json.Linq.JArray.Parse(response);

                var currentRepoIds = new HashSet<string>();
                foreach (var p in _projects) currentRepoIds.Add(p.id);

                foreach (var repo in jsonArray)
                {
                    string id = repo["id"]?.ToString() ?? "";
                    if (!currentRepoIds.Contains(id))
                    {
                        ImportableRepos.Add(new RepoItem
                        {
                            id = id,
                            name = repo["name"]?.ToString() ?? "",
                            fullName = repo["fullName"]?.ToString() ?? "",
                            owner = repo["owner"]?.ToString() ?? "",
                            isPrivate = repo["isPrivate"]?.ToObject<bool>() ?? false,
                            isSelected = false
                        });
                    }
                }

                if (ImportableRepos.Count == 0)
                {
                    ImportStatusText.Text = "No new repositories to import.";
                }
                else
                {
                    ImportStatusText.Text = "Select projects to import to DevTavern.";
                }
            }
            catch (Exception ex)
            {
                ImportStatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void ImportProjectsOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
        {
            ImportProjectsOverlay.Visibility = Visibility.Collapsed;
        }

        private void ImportSelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool anyUnselected = ImportableRepos.Any(r => !r.isSelected);
            foreach (var r in ImportableRepos) r.isSelected = anyUnselected;
            
            ImportProjectsList.ItemsSource = null;
            ImportProjectsList.ItemsSource = ImportableRepos;
        }

        private async void ConfirmImport_Click(object sender, RoutedEventArgs e)
        {
            var newSelectedRepos = ImportableRepos.Where(r => r.isSelected).ToList();
            if (newSelectedRepos.Count == 0)
            {
                ImportProjectsOverlay.Visibility = Visibility.Collapsed;
                return;
            }
            
            ImportStatusText.Text = "Importing and syncing...";
            
            foreach (var r in newSelectedRepos)
            {
                r.IconLetters = GenerateIconLetters(r.name);
                _projects.Add(r);
            }

            // Sync with DB just for the newly appended projects
            foreach (var project in newSelectedRepos)
            {
                try
                {
                    var pData = new { GitHubRepoId = project.id, Name = project.name };
                    var pContent = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(pData), System.Text.Encoding.UTF8, "application/json");
                    var pResp = await _apiClient.PostAsync("projects", pContent);
                    if (pResp.IsSuccessStatusCode)
                    {
                        var pJson = Newtonsoft.Json.Linq.JObject.Parse(await pResp.Content.ReadAsStringAsync());
                        project.DbId = pJson["id"]?.ToObject<int>() ?? 0;

                        var cResp = await _apiClient.PostAsync($"channels/generate-defaults/{project.DbId}", null);
                        if (cResp.IsSuccessStatusCode)
                        {
                            var cArr = Newtonsoft.Json.Linq.JArray.Parse(await cResp.Content.ReadAsStringAsync());
                            var channelsList = new ObservableCollection<ChannelItem>();
                            foreach (var c in cArr)
                            {
                                channelsList.Add(new ChannelItem
                                {
                                    Id = c["id"]?.ToObject<int>() ?? 0,
                                    Name = c["name"]?.ToString() ?? ""
                                });
                            }
                            _projectChannels[project.name] = channelsList;
                        }
                    }

                    _projectMembers[project.name] = new ObservableCollection<MemberItem>();
                    try
                    {
                        using var ghClient = new HttpClient();
                        ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                        ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                        var collabResp = await ghClient.GetAsync($"https://api.github.com/repos/{project.fullName}/collaborators");
                        if (!collabResp.IsSuccessStatusCode)
                        {
                            collabResp = await ghClient.GetAsync($"https://api.github.com/repos/{project.fullName}/contributors");
                        }

                        if (collabResp.IsSuccessStatusCode)
                        {
                            var collabJson = Newtonsoft.Json.Linq.JArray.Parse(await collabResp.Content.ReadAsStringAsync());
                            foreach (var collab in collabJson)
                            {
                                string memberUsername = collab["login"]?.ToString() ?? "";
                                string memberAvatar = collab["avatar_url"]?.ToString() ?? "";
                                if (string.IsNullOrEmpty(memberUsername)) continue;

                                string role = memberUsername == _username ? "You" : "Collaborator";
                                var permissions = collab["permissions"];
                                if (permissions != null && permissions["admin"]?.ToObject<bool>() == true)
                                {
                                    role = memberUsername == _username ? "Owner (You)" : "Owner";
                                }

                                _projectMembers[project.name].Add(new MemberItem
                                {
                                    Username = memberUsername,
                                    Initials = memberUsername.Length >= 2 ? memberUsername.Substring(0, 2).ToUpper() : memberUsername.ToUpper(),
                                    Role = role,
                                    IsOnline = memberUsername == _username,
                                    AvatarUrl = memberAvatar
                                });
                            }
                        }
                    }
                    catch { }

                    if (_projectMembers[project.name].Count == 0)
                    {
                        _projectMembers[project.name].Add(new MemberItem
                        {
                            Username = _username,
                            Initials = UserInitials.Text,
                            Role = "Owner",
                            IsOnline = true,
                            AvatarUrl = _avatarUrl
                        });
                    }
                }
                catch { }
            }

            // Refresh ProjectList in the sidebar so new icons show up
            ProjectList.ItemsSource = null;
            ProjectList.ItemsSource = _projects;

            // Add newly imported projects to the cache for auto-login skips
            try
            {
                System.IO.File.WriteAllText("installed_projects.cache", JsonConvert.SerializeObject(_projects));
            }
            catch { }

            ImportProjectsOverlay.Visibility = Visibility.Collapsed;
        }
    }
    public class ChatMessage
    {
        public string Username { get; set; } = "";
        public string Initials { get; set; } = "";
        public string AvatarColor { get; set; } = "#8B949E";
        public string UsernameColor { get; set; } = "#E6EDF3";
        public string? AvatarUrl { get; set; } = null;
        public string Content { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public bool IsSystemMessage { get; set; } = false;
        public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);
        public bool IsMentioningMe { get; set; } = false;
    }

    public class ChannelItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class MemberItem
    {
        public string Username { get; set; } = "";
        public string Initials { get; set; } = "";
        public string Role { get; set; } = "Member";
        public bool IsOnline { get; set; } = false;
        public string? AvatarUrl { get; set; } = null;
        public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);
        public bool IsHeader { get; set; } = false;
        
        public List<string> DevRoles { get; set; } = new List<string>();
        public string RoleBadges => DevRoles != null && DevRoles.Count > 0 ? string.Join(" · ", DevRoles) : "";
        public bool HasDevRoles => DevRoles != null && DevRoles.Count > 0;
    }
}