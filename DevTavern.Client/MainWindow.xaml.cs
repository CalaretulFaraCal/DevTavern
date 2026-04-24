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
using ICSharpCode.AvalonEdit.Highlighting;

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

        // Notifications
        private readonly Dictionary<int, string> _channelToProject = new();
        private readonly Dictionary<int, int> _lastSeenMessageCount = new();
        private System.Windows.Threading.DispatcherTimer? _notificationTimer;
        private bool _isPolling = false;

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

            // Customize AvalonEdit for Dark Theme
            CodeViewerContent.TextArea.Caret.CaretBrush = Brushes.Transparent;
            CodeViewerContent.TextArea.TextView.LineTransformers.Add(new DarkModeColorizer());

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

                    Messages.Add(ParseMessageContent(new ChatMessage
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
                    }));
                    // Tine lastSeenMessageCount sincronizat pentru canalul activ
                    if (_lastSeenMessageCount.ContainsKey(_selectedChannelId))
                        _lastSeenMessageCount[_selectedChannelId] = Messages.Count;

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

            // Primim notificare cand un canal este sters
            _hubConnection.On<int>("ChannelDeleted", (channelId) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_selectedProject != null && _projectChannels.TryGetValue(_selectedProject, out var channels))
                    {
                        var chToRemove = channels.FirstOrDefault(c => c.Id == channelId);
                        if (chToRemove != null)
                        {
                            channels.Remove(chToRemove);
                            if (_selectedChannelId == channelId)
                            {
                                if (channels.Count > 0) ChannelList.SelectedIndex = 0;
                                else
                                {
                                    Messages.Clear();
                                    ChatTitle.Text = "No channels";
                                    ChatSubtitle.Text = "Create a channel to start chatting";
                                }
                            }
                        }
                    }
                });
            });

            // Primim notificare cand un utilizator intra pe proiect
            _hubConnection.On<string>("UserJoinedProject", (username) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_selectedProject != null && _projectMembers.TryGetValue(_selectedProject, out var members))
                    {
                        var m = members.FirstOrDefault(u => u.Username == username);
                        if (m != null)
                        {
                            m.IsOnline = true;
                            RefreshMembersList();
                        }
                    }
                });
            });

            // Primim notificare cand un utilizator a inchis aplicatia
            _hubConnection.On<string>("UserLeftProject", (username) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_selectedProject != null && _projectMembers.TryGetValue(_selectedProject, out var members))
                    {
                        var m = members.FirstOrDefault(u => u.Username == username);
                        if (m != null)
                        {
                            m.IsOnline = false;
                            RefreshMembersList();
                        }
                    }
                });
            });

            // Primim notificare live daca cineva ii schimba rolurile unui utilizator
            _hubConnection.On<string, string>("RolesChanged", (username, newRolesCsv) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_selectedProject != null && _projectMembers.TryGetValue(_selectedProject, out var members))
                    {
                        var m = members.FirstOrDefault(u => u.Username == username);
                        if (m != null)
                        {
                            m.DevRoles = newRolesCsv.Split(new[] { ", ", "," }, StringSplitOptions.RemoveEmptyEntries).ToList();
                            RefreshMembersList();
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

                    // Genereaza lista locala, chiar daca e goala (eroare API)
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

                    // Aducem rolurile custom din BD Server
                    try
                    {
                        var rolesResp = await _apiClient.GetAsync($"projects/{project.DbId}/roles");
                        if (rolesResp.IsSuccessStatusCode)
                        {
                            var rolesArr = JArray.Parse(await rolesResp.Content.ReadAsStringAsync());
                            foreach (var rJson in rolesArr)
                            {
                                string roleUsername = rJson["username"]?.ToString() ?? "";
                                string devRolesStr = rJson["devRoles"]?.ToString() ?? "";
                                var member = _projectMembers[project.name].FirstOrDefault(m => m.Username == roleUsername);
                                if (member != null && !string.IsNullOrEmpty(devRolesStr))
                                {
                                    member.DevRoles = devRolesStr.Split(new[] { ", ", "," }, StringSplitOptions.RemoveEmptyEntries).ToList();
                                }
                            }
                        }
                    }
                    catch { }
                }
                catch { }
            }

            // Construieste mappingul channel -> project si porneste polling-ul
            foreach (var proj in _projects)
            {
                if (_projectChannels.TryGetValue(proj.name, out var chans))
                    foreach (var ch in chans)
                        _channelToProject[ch.Id] = proj.name;
            }
            StartNotificationPolling();
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
                        
                        // Cand intram pe proiect, anuntam serverul cine suntem ca sa le zica celorlalti ca suntem online
                        var onlineUsers = await _hubConnection.InvokeAsync<List<string>>("JoinProject", newProjectDbId, _username);
                        
                        // Setam "IsOnline = true" pentru cei care ne-a returnat serverul ca sunt DEJA online acum
                        if (_projectMembers.TryGetValue(selected.name, out var projMembers))
                        {
                            foreach(var user in onlineUsers)
                            {
                                var m = projMembers.FirstOrDefault(x => x.Username == user);
                                if (m != null) m.IsOnline = true;
                            }
                        }
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
                
                if (_codeBrowserVisible)
                {
                    _ = LoadCodeBrowserAsync();
                }
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

                        Messages.Add(ParseMessageContent(new ChatMessage
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
                        }));
                    }
                }
                catch { }

                if (Messages.Count == 0)
                {
                    Messages.Add(ParseMessageContent(new ChatMessage
                    {
                        IsSystemMessage = true,
                        Content = $"Welcome to #{selectedChannel.Name}! This is the beginning of the conversation.",
                        Timestamp = DateTime.Now.ToString("HH:mm")
                    }));
                }

                await Application.Current.Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToEnd(),
                    System.Windows.Threading.DispatcherPriority.Background);

                // Marcheaza canalul ca citit si sterge badge-urile
                if (_selectedProject != null && _projectChannels.TryGetValue(_selectedProject, out var chList))
                {
                    var readCh = chList.FirstOrDefault(c => c.Id == _selectedChannelId);
                    if (readCh != null) { readCh.UnreadMentionCount = 0; readCh.HasUnreadMessages = false; }
                }
                _lastSeenMessageCount[_selectedChannelId] = Messages.Count;
                UpdateProjectBadge(_selectedProject ?? "");

                MessageInput.Focus();
            }
        }

        private bool _codeBrowserVisible = false;

        private void ToggleMembersButton_Click(object sender, RoutedEventArgs e)
        {
            // If code browser is open, close it first
            if (_codeBrowserVisible)
            {
                CodeBrowserPanel.Visibility = Visibility.Collapsed;
                CodeBrowserSplitter.Visibility = Visibility.Collapsed;
                SplitterColumn.Width = new GridLength(0);
                _codeBrowserVisible = false;
            }

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
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    int caretPos = MessageInput.SelectionStart;
                    int selLen = MessageInput.SelectionLength;
                    MessageInput.Text = MessageInput.Text.Substring(0, caretPos) + "\n" + MessageInput.Text.Substring(caretPos + selLen);
                    MessageInput.CaretIndex = caretPos + 1;
                    e.Handled = true;
                    return;
                }

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
            Messages.Add(ParseMessageContent(new ChatMessage
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
            }));

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

        private async void DeleteConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmOverlay.Visibility = Visibility.Collapsed;

            if (_editingChannel == null || _selectedProject == null) return;

            var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
            if (proj == null || proj.DbId == 0) return;

            try
            {
                var resp = await _apiClient.DeleteAsync($"channels/{_editingChannel.Id}");
                if (resp.IsSuccessStatusCode)
                {
                    int deletedId = _editingChannel.Id;

                    // Remove from local collection
                    if (_projectChannels.TryGetValue(_selectedProject, out var channels))
                    {
                        channels.Remove(_editingChannel);

                        if (_selectedChannelId == deletedId)
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

                    // Notificare SignalR pentru restul echipei
                    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                    {
                        await _hubConnection.InvokeAsync("NotifyChannelDeleted", proj.DbId.ToString(), deletedId);
                    }
                }
            }
            catch { }

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

        // ========== Code Browser ==========

        private string _codeBrowserCurrentBranch = "";
        private bool _codeBranchChanging = false;

        private void BrowseCodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_codeBrowserVisible)
            {
                CodeBrowserPanel.Visibility = Visibility.Collapsed;
                CodeBrowserSplitter.Visibility = Visibility.Collapsed;
                SplitterColumn.Width = new GridLength(0);
                MembersPanelColumn.Width = new GridLength(0);
                _codeBrowserVisible = false;
            }
            else
            {
                _ = LoadCodeBrowserAsync();
            }
        }

        private async Task LoadCodeBrowserAsync()
        {
            if (_selectedProject == null) return;

            var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
            if (proj == null || string.IsNullOrEmpty(proj.fullName)) return;

            CodeBrowserTitle.Text = proj.name;
            CodeBrowserSubtitle.Text = proj.fullName;

            // Reset state
            CodeTreeView.ItemsSource = null;
            CodeViewerContent.Text = "";
            CodeViewerFilePath.Text = "Select a file to view its contents";
            CodeViewerWelcome.Visibility = Visibility.Visible;
            CodeContentLoading.Visibility = Visibility.Collapsed;
            CodeTreeLoading.Visibility = Visibility.Visible;

            // Close members panel if open, show code browser panel
            if (_membersPanelVisible)
            {
                MembersPanelBorder.Visibility = Visibility.Collapsed;
                _membersPanelVisible = false;
            }

            // Open code browser — use half the chat area width
            SplitterColumn.Width = new GridLength(5);
            MembersPanelColumn.Width = new GridLength(1, GridUnitType.Star);
            CodeBrowserSplitter.Visibility = Visibility.Visible;
            CodeBrowserPanel.Visibility = Visibility.Visible;
            _codeBrowserVisible = true;

            // Fetch branches
            try
            {
                using var ghClient = new HttpClient();
                ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                var branchResp = await ghClient.GetStringAsync($"https://api.github.com/repos/{proj.fullName}/branches");
                var branchArr = JArray.Parse(branchResp);

                var branchNames = new List<string>();
                foreach (var b in branchArr)
                {
                    branchNames.Add(b["name"]?.ToString() ?? "");
                }

                _codeBranchChanging = true;
                CodeBranchSelector.ItemsSource = branchNames;

                // Select main or master by default
                int defaultIdx = branchNames.IndexOf("main");
                if (defaultIdx < 0) defaultIdx = branchNames.IndexOf("master");
                if (defaultIdx < 0 && branchNames.Count > 0) defaultIdx = 0;

                CodeBranchSelector.SelectedIndex = defaultIdx;
                _codeBranchChanging = false;

                if (defaultIdx >= 0)
                {
                    _codeBrowserCurrentBranch = branchNames[defaultIdx];
                    await LoadBranchTree(proj.fullName, _codeBrowserCurrentBranch);
                }
            }
            catch (Exception ex)
            {
                CodeTreeLoading.Text = $"Error: {ex.Message}";
            }
        }

        private async void CodeBranchSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_codeBranchChanging) return;
            if (CodeBranchSelector.SelectedItem is string branch && !string.IsNullOrEmpty(branch))
            {
                _codeBrowserCurrentBranch = branch;

                var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
                if (proj != null && !string.IsNullOrEmpty(proj.fullName))
                {
                    // Reset code viewer
                    CodeViewerContent.Text = "";
                    CodeViewerFilePath.Text = "Select a file to view its contents";
                    CodeViewerWelcome.Visibility = Visibility.Visible;
                    CodeContentLoading.Visibility = Visibility.Collapsed;

                    await LoadBranchTree(proj.fullName, branch);
                }
            }
        }

        private async Task LoadBranchTree(string fullName, string branch)
        {
            CodeTreeView.ItemsSource = null;
            CodeTreeLoading.Text = "Loading file tree...";
            CodeTreeLoading.Visibility = Visibility.Visible;

            try
            {
                using var ghClient = new HttpClient();
                ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                var treeResp = await ghClient.GetStringAsync($"https://api.github.com/repos/{fullName}/git/trees/{branch}?recursive=1");
                var treeJson = JObject.Parse(treeResp);
                var treeArr = treeJson["tree"] as JArray;

                if (treeArr == null)
                {
                    CodeTreeLoading.Text = "No files found.";
                    return;
                }

                // Build hierarchical tree
                var root = new List<GitTreeNode>();
                var nodeMap = new Dictionary<string, GitTreeNode>();

                // Sort so that trees come before blobs, and alphabetical
                var sortedItems = treeArr
                    .OrderBy(i => i["type"]?.ToString() == "blob" ? 1 : 0)
                    .ThenBy(i => i["path"]?.ToString())
                    .ToList();

                foreach (var item in sortedItems)
                {
                    string path = item["path"]?.ToString() ?? "";
                    string type = item["type"]?.ToString() ?? "";

                    if (string.IsNullOrEmpty(path)) continue;

                    var parts = path.Split('/');
                    string name = parts[parts.Length - 1];

                    var node = new GitTreeNode
                    {
                        Name = name,
                        FullPath = path,
                        IsDirectory = type == "tree",
                        Children = new ObservableCollection<GitTreeNode>()
                    };

                    nodeMap[path] = node;

                    if (parts.Length == 1)
                    {
                        root.Add(node);
                    }
                    else
                    {
                        string parentPath = string.Join("/", parts.Take(parts.Length - 1));
                        if (nodeMap.TryGetValue(parentPath, out var parentNode))
                        {
                            parentNode.Children.Add(node);
                        }
                        else
                        {
                            root.Add(node);
                        }
                    }
                }

                // Sort each folder: directories first, then files, alphabetical
                SortTreeNodes(root);

                CodeTreeView.ItemsSource = root;
                CodeTreeLoading.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                CodeTreeLoading.Text = $"Error: {ex.Message}";
            }
        }

        private void SortTreeNodes(List<GitTreeNode> nodes)
        {
            nodes.Sort((a, b) =>
            {
                if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var node in nodes)
            {
                if (node.Children.Count > 0)
                {
                    var childList = node.Children.ToList();
                    SortTreeNodes(childList);
                    node.Children = new ObservableCollection<GitTreeNode>(childList);
                }
            }
        }

        private async void CodeTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is GitTreeNode selectedNode && !selectedNode.IsDirectory)
            {
                var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
                if (proj == null || string.IsNullOrEmpty(proj.fullName)) return;

                CodeViewerWelcome.Visibility = Visibility.Collapsed;
                CodeViewerContent.Visibility = Visibility.Collapsed;
                CodeContentLoading.Visibility = Visibility.Visible;
                CodeViewerFilePath.Text = selectedNode.FullPath;

                try
                {
                    using var ghClient = new HttpClient();
                    ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                    ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                    var contentResp = await ghClient.GetStringAsync(
                        $"https://api.github.com/repos/{proj.fullName}/contents/{selectedNode.FullPath}?ref={_codeBrowserCurrentBranch}");
                    var contentJson = JObject.Parse(contentResp);

                    string encoding = contentJson["encoding"]?.ToString() ?? "";
                    string content = contentJson["content"]?.ToString() ?? "";
                    int size = contentJson["size"]?.ToObject<int>() ?? 0;

                    if (encoding == "base64" && !string.IsNullOrEmpty(content))
                    {
                        // Decode base64 content
                        byte[] bytes = Convert.FromBase64String(content);
                        string decoded = System.Text.Encoding.UTF8.GetString(bytes);

                        // AvalonEdit natively handles line numbers, just assign pure text.
                        CodeViewerContent.Text = decoded;
                        string ext = System.IO.Path.GetExtension(selectedNode.FullPath);
                        CodeViewerContent.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext) ?? HighlightingManager.Instance.GetDefinitionByExtension(".txt");
                        CodeViewerContent.Visibility = Visibility.Visible;
                    }
                    else if (size > 1_000_000)
                    {
                        CodeViewerContent.Text = "⚠ File too large to display.";
                        CodeViewerContent.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        CodeViewerContent.Text = "⚠ Binary file — cannot display content.";
                        CodeViewerContent.Visibility = Visibility.Visible;
                    }
                }
                catch (Exception ex)
                {
                    CodeViewerContent.Text = $"Error loading file: {ex.Message}";
                    CodeViewerContent.Visibility = Visibility.Visible;
                }

                CodeContentLoading.Visibility = Visibility.Collapsed;
            }
        }

        private void CloseCodeBrowser_Click(object sender, RoutedEventArgs e)
        {
            CodeBrowserPanel.Visibility = Visibility.Collapsed;
            CodeBrowserSplitter.Visibility = Visibility.Collapsed;
            SplitterColumn.Width = new GridLength(0);
            MembersPanelColumn.Width = new GridLength(0);
            _codeBrowserVisible = false;
        }

        private void QuoteSelectedCode_Click(object sender, RoutedEventArgs e)
        {
            if (CodeViewerContent.SelectedText.Length > 0 && _selectedChannelId > 0)
            {
                string selectedCode = CodeViewerContent.SelectedText;
                string filePath = CodeViewerFilePath.Text;
                string separator = filePath.Contains("/") ? "/" : "\\";
                string fileName = filePath.Contains(separator) ? filePath.Substring(filePath.LastIndexOf(separator) + 1) : filePath;

                string currentText = MessageInput.Text ?? "";
                if (currentText.Length > 0 && !currentText.EndsWith("\n"))
                {
                    currentText += "\n";
                }

                currentText += $"[CodeRef: {_codeBrowserCurrentBranch}|{filePath}]\nFrom `{fileName}`:\n```\n{selectedCode}\n```\n";

                MessageInput.Text = currentText;
                MessageInput.CaretIndex = MessageInput.Text.Length;
                MessageInput.Focus();
                
                // Also close code browser to let user chat? Not necessarily, user can do it.
            }
            else if (_selectedChannelId == 0)
            {
                MessageBox.Show("Vă rugăm să selectați un canal înainte de a menționa codul.", "Eroare", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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

        private async void SaveRoles_Click(object sender, RoutedEventArgs e)
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

                    // Salvare in Backend DB
                    var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
                    if (proj != null && proj.DbId > 0)
                    {
                        try
                        {
                            string rolesCsv = string.Join(", ", me.DevRoles);
                            var postData = new { Username = _username, DevRoles = rolesCsv };
                            var content = new StringContent(JsonConvert.SerializeObject(postData), System.Text.Encoding.UTF8, "application/json");
                            var resp = await _apiClient.PostAsync($"projects/{proj.DbId}/roles", content);

                            if (resp.IsSuccessStatusCode && _hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                            {
                                // Anuntam pe toti colegii live ca rolurile s-au modificat pentru a rescrie design-ul
                                await _hubConnection.InvokeAsync("NotifyRolesChanged", proj.DbId.ToString(), _username, rolesCsv);
                            }
                        }
                        catch { }
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
        private ChatMessage ParseMessageContent(ChatMessage msg)
        {
            msg.DisplayContent = msg.Content;
            if (msg.Content.StartsWith("[CodeRef:") || msg.Content.Contains("\n[CodeRef:"))
            {
                int startIndex = msg.Content.IndexOf("[CodeRef:");
                if (startIndex >= 0)
                {
                    int endIndex = msg.Content.IndexOf("]", startIndex);
                    if (endIndex > startIndex)
                    {
                        string refData = msg.Content.Substring(startIndex + 9, endIndex - startIndex - 9);
                        var parts = refData.Split(new[] { '|' }, 2);
                        if (parts.Length == 2)
                        {
                            msg.HasCodeReference = true;
                            msg.CodeBranch = parts[0].Trim();
                            msg.CodeFilePath = parts[1].Trim();
                            
                            string before = msg.Content.Substring(0, startIndex).Trim();
                            string after = msg.Content.Substring(endIndex + 1).Trim();
                            string rawDisplay = (before + "\n" + after).Trim();

                            int codeBlockStart = rawDisplay.IndexOf("```");
                            if (codeBlockStart >= 0)
                            {
                                int contentStart = codeBlockStart + 3;
                                if (rawDisplay.Length > contentStart && rawDisplay[contentStart] == '\n') contentStart++;
                                int codeBlockEnd = rawDisplay.IndexOf("```", contentStart);
                                if (codeBlockEnd >= 0)
                                {
                                    msg.CodeSelectedText = rawDisplay.Substring(contentStart, codeBlockEnd - contentStart).Trim('\r','\n');
                                    string beforeBlock = rawDisplay.Substring(0, codeBlockStart).Trim();
                                    string afterBlock = rawDisplay.Substring(codeBlockEnd + 3).Trim();
                                    rawDisplay = (beforeBlock + "\n" + afterBlock).Trim();
                                }
                            }
                            
                            msg.DisplayContent = rawDisplay;
                        }
                    }
                }
            }
            return msg;
        }

        private async void JumpToCode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ChatMessage msg && msg.HasCodeReference)
            {
                if (!_codeBrowserVisible)
                {
                    await LoadCodeBrowserAsync();
                }

                // Try to set branch
                if (CodeBranchSelector.Items.Contains(msg.CodeBranch))
                {
                    CodeBranchSelector.SelectedItem = msg.CodeBranch;
                }

                var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
                if (proj == null || string.IsNullOrEmpty(proj.fullName)) return;

                CodeViewerWelcome.Visibility = Visibility.Collapsed;
                CodeViewerContent.Visibility = Visibility.Collapsed;
                CodeContentLoading.Visibility = Visibility.Visible;
                CodeViewerFilePath.Text = msg.CodeFilePath;

                try
                {
                    using var ghClient = new HttpClient();
                    ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                    ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                    var contentResp = await ghClient.GetStringAsync(
                        $"https://api.github.com/repos/{proj.fullName}/contents/{msg.CodeFilePath}?ref={msg.CodeBranch}");
                    var contentJson = JObject.Parse(contentResp);

                    string encoding = contentJson["encoding"]?.ToString() ?? "";
                    string content = contentJson["content"]?.ToString() ?? "";

                    if (encoding == "base64" && !string.IsNullOrEmpty(content))
                    {
                        byte[] bytes = Convert.FromBase64String(content);
                        string decoded = System.Text.Encoding.UTF8.GetString(bytes);

                        CodeViewerContent.Text = decoded;
                        string ext = System.IO.Path.GetExtension(msg.CodeFilePath);
                        CodeViewerContent.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext) ?? HighlightingManager.Instance.GetDefinitionByExtension(".txt");
                        CodeViewerContent.Visibility = Visibility.Visible;

                        // Give it time to render then select text
                        await Task.Delay(100);
                        
                        // Select the code snippet
                        if (!string.IsNullOrEmpty(msg.CodeSelectedText))
                        {
                            int idx = CodeViewerContent.Text.IndexOf(msg.CodeSelectedText);
                            if (idx >= 0)
                            {
                                CodeViewerContent.Focus();
                                CodeViewerContent.Select(idx, msg.CodeSelectedText.Length);
                                
                                // Scroll to specific line (AvalonEdit is 1-indexed for lines)
                                int lineIndex = CodeViewerContent.Text.Substring(0, idx).Split('\n').Length;
                                CodeViewerContent.ScrollToLine(Math.Max(1, lineIndex - 5));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    CodeViewerContent.Text = $"Error dynamically loading file: {ex.Message}";
                    CodeViewerContent.Visibility = Visibility.Visible;
                }

                CodeContentLoading.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateProjectBadge(string projectName)
        {
            var project = _projects.FirstOrDefault(p => p.name == projectName);
            if (project == null) return;
            if (!_projectChannels.TryGetValue(projectName, out var channels)) return;

            project.UnreadMentionCount = channels.Sum(c => c.UnreadMentionCount);
            project.HasUnreadMessages = channels.Any(c => c.HasUnreadMessages);
        }

        private void StartNotificationPolling()
        {
            _notificationTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _notificationTimer.Tick += async (s, e) => await PollNotificationsAsync();
            _notificationTimer.Start();
        }

        private async Task PollNotificationsAsync()
        {
            if (_isPolling) return;
            _isPolling = true;
            try
            {
                foreach (var project in _projects)
                {
                    if (!_projectChannels.TryGetValue(project.name, out var channels)) continue;
                    foreach (var channel in channels)
                    {
                        if (channel.Id == _selectedChannelId) continue;

                        try
                        {
                            var resp = await _apiClient.GetStringAsync($"messages/channel/{channel.Id}");
                            var arr = JArray.Parse(resp);
                            int newCount = arr.Count;

                            if (!_lastSeenMessageCount.TryGetValue(channel.Id, out int lastSeen))
                            {
                                // Prima oara cand vedem canalul — stabilim baseline, fara badge
                                _lastSeenMessageCount[channel.Id] = newCount;
                                continue;
                            }

                            if (newCount > lastSeen)
                            {
                                int mentions = 0;
                                for (int i = lastSeen; i < arr.Count; i++)
                                {
                                    string content = arr[i]["content"]?.ToString() ?? "";
                                    if (content.Contains("@" + _username, StringComparison.OrdinalIgnoreCase))
                                        mentions++;
                                }
                                channel.UnreadMentionCount += mentions;
                                channel.HasUnreadMessages = true;
                                _lastSeenMessageCount[channel.Id] = newCount;
                                UpdateProjectBadge(project.name);
                            }
                        }
                        catch { }
                    }
                }
            }
            finally { _isPolling = false; }
        }

        protected override void OnClosed(EventArgs e)
        {
            _notificationTimer?.Stop();
            base.OnClosed(e);
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

        public bool HasCodeReference { get; set; } = false;
        public string CodeFilePath { get; set; } = "";
        public string CodeBranch { get; set; } = "";
        public string CodeSelectedText { get; set; } = "";
        public string DisplayContent { get; set; } = "";
    }

    public class ChannelItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int UnreadMentionCount { get; set; }
        public bool HasUnreadMessages { get; set; }
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

    public class GitTreeNode
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public ObservableCollection<GitTreeNode> Children { get; set; } = new ObservableCollection<GitTreeNode>();
        public string Icon => IsDirectory ? "📁" : "📄";
    }

    public class DarkModeColorizer : ICSharpCode.AvalonEdit.Rendering.DocumentColorizingTransformer
    {
        protected override void ColorizeLine(ICSharpCode.AvalonEdit.Document.DocumentLine line)
        {
            ChangeLinePart(line.Offset, line.EndOffset, (element) =>
            {
                if (element.TextRunProperties.ForegroundBrush is SolidColorBrush brush)
                {
                    var c = brush.Color;
                    if (c.R == 201 && c.G == 209 && c.B == 217) return; // Ignore default text color

                    if (c.R < 30 && c.G < 30 && c.B < 30) // Turn pure black into normal text
                    {
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(201, 209, 217)));
                    }
                    else
                    {
                        // Lighten standard syntax colors (makes dark blue into light blue, dark red to light red)
                        byte r = (byte)Math.Min(255, c.R + 80);
                        byte g = (byte)Math.Min(255, c.G + 80);
                        byte b = (byte)Math.Min(255, c.B + 80);
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(r, g, b)));
                    }
                }
            });
        }
    }

    public static class TextBlockHelper
    {
        public static readonly DependencyProperty FormattedTextProperty =
            DependencyProperty.RegisterAttached("FormattedText", typeof(string), typeof(TextBlockHelper), new PropertyMetadata(string.Empty, OnFormattedTextChanged));

        public static void SetFormattedText(DependencyObject obj, string value)
        {
            obj.SetValue(FormattedTextProperty, value);
        }

        public static string GetFormattedText(DependencyObject obj)
        {
            return (string)obj.GetValue(FormattedTextProperty);
        }

        private static void OnFormattedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock textBlock)
            {
                var text = e.NewValue as string ?? string.Empty;
                textBlock.Inlines.Clear();

                if (string.IsNullOrEmpty(text)) return;

                var parts = System.Text.RegularExpressions.Regex.Split(text, @"(@[a-zA-Z0-9_\-]+)");
                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;

                    if (part.StartsWith("@") && part.Length > 1)
                    {
                        var run = new System.Windows.Documents.Run(part)
                        {
                            Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E3B341")),
                            FontWeight = FontWeights.Bold
                        };
                        textBlock.Inlines.Add(run);
                    }
                    else
                    {
                        textBlock.Inlines.Add(new System.Windows.Documents.Run(part));
                    }
                }
            }
        }
    }
}