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

            // Primim mesaje live de la toti utilizatorii conectati
            // Hub-ul trimite la ALL (inclusiv noi), deci ignoram propriile mesaje ca sa nu le duplicam
            _hubConnection.On<string, string>("ReceiveMessage", (senderUsername, messageContent) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Ignoram mesajele trimise de noi (le-am afisat deja local in SendMessage)
                    if (senderUsername == _username) return;

                    if (_selectedChannelId > 0)
                    {
                        Messages.Add(new ChatMessage
                        {
                            Username = senderUsername,
                            Initials = senderUsername.Length >= 2 ? senderUsername.Substring(0, 2).ToUpper() : senderUsername.ToUpper(),
                            AvatarColor = "#8B949E",
                            UsernameColor = "#E6EDF3",
                            Content = messageContent,
                            Timestamp = DateTime.Now.ToString("HH:mm"),
                            IsSystemMessage = false
                        });
                        Application.Current.Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToEnd(),
                            System.Windows.Threading.DispatcherPriority.Background);
                    }
                });
            });

            try 
            { 
                await _hubConnection.StartAsync(); 
                ChatSubtitle.Text = $"Connected to Taverna Link ✓";
            } 
            catch (Exception ex)
            {
                ChatSubtitle.Text = $"Offline Mode (Real-time sync disabled)";
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

                    // Members — initializare locala (serverul nu are un endpoint dedicat pentru membri)
                    _projectMembers[project.name] = new ObservableCollection<MemberItem>
                    {
                        new MemberItem
                        {
                            Username = _username,
                            Initials = UserInitials.Text,
                            Role = "Owner",
                            IsOnline = true,
                            AvatarUrl = _avatarUrl
                        }
                    };
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

        private void ProjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProjectList.SelectedItem is RepoItem selected)
            {
                _selectedProject = selected.name;
                SelectedProjectName.Text = selected.name;

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
                    MembersList.ItemsSource = members;
                }

                HomeView.Visibility = Visibility.Collapsed;
                ChatView.Visibility = Visibility.Visible;
            }
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
                            IsSystemMessage = false
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

                Application.Current.Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToEnd(),
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

        private void MessageInput_KeyDown(object sender, KeyEventArgs e)
        {
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
                IsSystemMessage = false
            });

            Application.Current.Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToEnd(),
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
                    await _hubConnection.InvokeAsync("SendLiveMessage", _selectedChannelId.ToString(), _username, text);
                }
            }
            catch { }
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
    }
}