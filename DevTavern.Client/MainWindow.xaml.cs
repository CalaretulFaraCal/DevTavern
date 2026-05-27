using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
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
using NAudio.CoreAudioApi;
using NAudio.Wave;

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
        private string _displayName;

        private string? _selectedProject;
        private int _selectedChannelId;
        private bool _membersPanelVisible = false;
        private ChannelItem? _editingChannel = null;

        // Persist sidebar width across project switches
        private double _channelPanelWidth = 240;


        // Per-channel message drafts
        private readonly Dictionary<int, string> _channelDrafts = new();

        // Channels per project: projectName -> list of channels
        private readonly Dictionary<string, ObservableCollection<ChannelItem>> _projectChannels = new();

        // Voice channels per project (client-side only)
        private readonly Dictionary<string, ObservableCollection<ChannelItem>> _projectVoiceChannels = new();
        private ChannelItem? _currentVoiceChannel = null;
        private string? _currentVoiceGroupKey = null;
        private bool _isMuted = false;
        private bool _isDeafened = false;
        private bool _capturingPttKey = false;
        private string _pttKey = "Caps Lock";
        private bool _isPttActive = false;
        private double _vadThresholdRms = 2000.0;
        private double _inputVolumeScale = 1.0;
        private double _outputVolumeScale = 1.0;
        private bool _isVadMode = true;
        
        public ObservableCollection<ActivityItem> HomeActivities { get; set; } = new ObservableCollection<ActivityItem>();

        private static readonly string _voiceSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevTavern", "voice_settings.json");
        private static readonly string _soundSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevTavern", "sound_settings.json");
        private static readonly string _serverIconsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevTavern", "server_icons.json");
        private RepoItem? _settingsTargetProject = null;

        // Crop overlay state
        private const double CropViewportSize = 360.0;
        private const double CropCircleRadius  = 150.0;
        private BitmapImage? _cropBitmap;
        private double _cropOrigWidth, _cropOrigHeight;
        private double _cropScale;
        private double _cropTx, _cropTy;
        private Point  _cropLastMouse;
        private bool   _cropIsDragging = false;
        private AppSoundSettings _soundSettings = new();

        public static readonly string[] KeybindActions = { "Push to Mute", "Push to Deafen", "Toggle Mute", "Toggle Deafen" };
        public ObservableCollection<KeybindEntry> Keybinds { get; } = new();
        private static readonly string _keybindsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevTavern", "keybinds.json");
        private KeybindEntry? _capturingKeybindEntry = null;
        private string _capturingKeybindPrevKey = "";
        private bool _pushMuteActive = false;
        private bool _pushDeafenActive = false;

        private readonly Dictionary<string, CancellationTokenSource> _speakingTimers = new();

        // NAudio Voice Chat properties
        private WaveInEvent? _waveIn;
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _waveProvider;

        // Members per project
        private readonly Dictionary<string, ObservableCollection<MemberItem>> _projectMembers = new();
        private readonly HashSet<string> _globalOnlineUsers = new();

        // Notifications
        private readonly Dictionary<int, string> _channelToProject = new();
        private readonly Dictionary<int, int> _lastSeenMessageCount = new();
        private System.Windows.Threading.DispatcherTimer? _notificationTimer;
        private bool _isPolling = false;

        public ObservableCollection<ChatMessage> Messages { get; set; } = new ObservableCollection<ChatMessage>();

        private string _soundPackFolder = "Sounds_Default";

        private void PlaySound(string fileName, SoundConfig? config = null)
        {
            if (config != null && !config.Enabled) return;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", _soundPackFolder, fileName);
                if (!File.Exists(path)) return;
                float volume = config != null ? (float)(config.Volume / 100.0) : 1.0f;
                var reader = new AudioFileReader(path) { Volume = volume };
                var wo = new WaveOutEvent();
                wo.Init(reader);
                wo.Play();
                wo.PlaybackStopped += (s, e) => { wo.Dispose(); reader.Dispose(); };
            }
            catch { }
        }

        public MainWindow(string accessToken, List<RepoItem> projects, string username, string avatarUrl, int currentUserId, string displayName = "")
        {
            InitializeComponent();
            _soundSettings = LoadSoundSettings();

            _currentUserId = currentUserId;
            _apiClient = new HttpClient { BaseAddress = new Uri("https://devtavern.onrender.com/api/") };

            _accessToken = accessToken;
            _projects = projects;
            _username = username;
            _avatarUrl = avatarUrl;
            _displayName = string.IsNullOrEmpty(displayName) ? username : displayName;

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
                
            ProfileBarUsername.Text = _displayName;
            PopupUsername.Text = _displayName;
            ResolveDisplayNameAsync(_username, name =>
            {
                _displayName = name;
                ProfileBarUsername.Text = name;
                PopupUsername.Text = name;
            });

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

            HomeActivityFeed.ItemsSource = HomeActivities;

            // Show home view on startup
            ShowHomeView();

        }

        public async Task InitializeAsync()
        {
            _fullNameCache[_username] = _displayName;

            // Load custom server icons from local storage
            var savedIcons = LoadServerIcons();
            foreach (var p in _projects)
                if (savedIcons.TryGetValue(p.id, out var iconPath))
                    p.CustomImagePath = iconPath;

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

                    if (messageContent.Contains("@" + _username, StringComparison.OrdinalIgnoreCase))
                        PlaySound("mention.wav", _soundSettings.Mention);
                    else
                        PlaySound("mesaje.wav", _soundSettings.Message);

                    DateTime now = DateTime.Now;
                    var lastReal = Messages.LastOrDefault(m => !m.IsDateSeparator && !m.IsSystemMessage);
                    if (lastReal == null || lastReal.MessageDate.Date != now.Date)
                        Messages.Add(new ChatMessage { IsDateSeparator = true, DateLabel = FormatDateLabel(now.Date) });

                    Messages.Add(ParseMessageContent(new ChatMessage
                    {
                        Username = senderUsername,
                        Initials = senderUsername.Length >= 2 ? senderUsername.Substring(0, 2).ToUpper() : senderUsername.ToUpper(),
                        AvatarColor = "#8B949E",
                        UsernameColor = "#E6EDF3",
                        AvatarUrl = string.IsNullOrEmpty(senderAvatarUrl) ? null : senderAvatarUrl,
                        Content = messageContent,
                        Timestamp = now.ToString("HH:mm"),
                        IsSystemMessage = false,
                        IsMentioningMe = messageContent.Contains("@" + _username, StringComparison.OrdinalIgnoreCase),
                        MessageDate = now
                    }));
                    // Tine lastSeenMessageCount sincronizat pentru canalul activ
                    if (_lastSeenMessageCount.ContainsKey(_selectedChannelId))
                        _lastSeenMessageCount[_selectedChannelId] = Messages.Count(m => !m.IsSystemMessage && !m.IsDateSeparator);

                    Application.Current.Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToEnd(),
                        System.Windows.Threading.DispatcherPriority.Background);
                });
            });

            // Notificare real-time cand cineva trimite un mesaj pe alt canal din proiect
            _hubConnection.On<int, string, string>("ChannelMessageReceived", (channelId, senderUsername, content) =>
            {
                if (senderUsername == _username) return;
                if (channelId == _selectedChannelId) return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ChannelItem? targetChannel = null;
                    string? targetProject = null;
                    foreach (var kvp in _projectChannels)
                    {
                        var ch = kvp.Value.FirstOrDefault(c => c.Id == channelId);
                        if (ch != null) { targetChannel = ch; targetProject = kvp.Key; break; }
                    }
                    if (targetChannel == null) return;

                    targetChannel.HasUnreadMessages = true;
                    if (content.Contains("@" + _username, StringComparison.OrdinalIgnoreCase))
                        targetChannel.UnreadMentionCount++;

                    UpdateProjectBadge(targetProject ?? "");
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

            _hubConnection.On<int, string>("MessageEdited", (messageId, newContent) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var msg = Messages.FirstOrDefault(m => m.MessageId == messageId);
                    if (msg == null) return;
                    msg.Content = newContent;
                    msg.IsEdited = true;
                    ParseMessageContent(msg);
                });
            });

            _hubConnection.On<int>("MessageDeleted", (messageId) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var msg = Messages.FirstOrDefault(m => m.MessageId == messageId);
                    if (msg == null) return;
                    int idx = Messages.IndexOf(msg);
                    Messages.RemoveAt(idx);
                    // Sterge separatorul de data daca a ramas fara mesaje dupa el
                    if (idx > 0 && Messages[idx - 1].IsDateSeparator)
                    {
                        bool orphaned = idx >= Messages.Count || Messages[idx].IsDateSeparator;
                        if (orphaned) Messages.RemoveAt(idx - 1);
                    }
                });
            });

            // Primim notificare cand un utilizator intra in aplicatie global
            _hubConnection.On<string>("UserWentOnline", (username) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _globalOnlineUsers.Add(username);
                    RefreshMembersList();
                });
            });

            // Primim notificare cand un utilizator a inchis aplicatia
            _hubConnection.On<string>("UserWentOffline", (username) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _globalOnlineUsers.Remove(username);
                    RefreshMembersList();
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

            // Primim stream audio de la alti utilizatori din canalul de voce
            _hubConnection.On<string, byte[]>("ReceiveAudioBuffer", (senderUsername, audioData) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (!_isDeafened && _waveProvider != null)
                    {
                        if (Math.Abs(_outputVolumeScale - 1.0) > 0.01)
                        {
                            for (int i = 0; i < audioData.Length - 1; i += 2)
                            {
                                int sample = BitConverter.ToInt16(audioData, i);
                                sample = Math.Clamp((int)(sample * _outputVolumeScale), short.MinValue, short.MaxValue);
                                audioData[i] = (byte)(sample & 0xFF);
                                audioData[i + 1] = (byte)((sample >> 8) & 0xFF);
                            }
                        }
                        try { _waveProvider.AddSamples(audioData, 0, audioData.Length); } catch { }
                    }

                    var member = FindVoiceMember(senderUsername);
                    if (member == null) return;
                    member.IsSpeaking = true;

                    if (_speakingTimers.TryGetValue(senderUsername, out var existing))
                        existing.Cancel();
                    var cts = new CancellationTokenSource();
                    _speakingTimers[senderUsername] = cts;
                    _ = Task.Delay(400, cts.Token).ContinueWith(_ =>
                        Application.Current?.Dispatcher.BeginInvoke(() => member.IsSpeaking = false),
                        TaskContinuationOptions.OnlyOnRanToCompletion);
                });
            });

            _hubConnection.On<string, string>("UserJoinedVoice", (channelKey, joinedUsername) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var channel = FindVoiceChannelByKey(channelKey);
                    if (channel == null) return;
                    if (channel.VoiceMembers.Any(m => m.Username == joinedUsername)) return;
                    channel.VoiceMembers.Add(new VoiceMember { Username = joinedUsername, DisplayName = GetDisplayName(joinedUsername), AvatarUrl = GetAvatarUrl(joinedUsername) });
                });
            });

            _hubConnection.On<string, string>("UserLeftVoice", (channelKey, leftUsername) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var channel = FindVoiceChannelByKey(channelKey);
                    if (channel == null) return;
                    var member = channel.VoiceMembers.FirstOrDefault(m => m.Username == leftUsername);
                    if (member != null) channel.VoiceMembers.Remove(member);
                });
            });

            _hubConnection.On<string, string, bool, bool>("VoiceStateChanged", (channelKey, username, isMuted, isDeafened) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var channel = FindVoiceChannelByKey(channelKey);
                    if (channel == null) return;
                    var member = channel.VoiceMembers.FirstOrDefault(m => m.Username == username);
                    if (member == null) return;
                    member.IsMuted = isMuted;
                    member.IsDeafened = isDeafened;
                });
            });

            try 
            { 
                await _hubConnection.StartAsync(); 
                ChatSubtitle.Text = "Connected to Taverna Link";
                
                var onlineUsers = await _hubConnection.InvokeAsync<List<string>>("GoOnline", _username);
                foreach(var u in onlineUsers) _globalOnlineUsers.Add(u);
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
                        var serverImageUrl = pJson["imageUrl"]?.ToString();
                        if (!string.IsNullOrEmpty(serverImageUrl))
                            project.ServerImageUrl = serverImageUrl;

                        // POST /api/channels/generate-defaults/{projectId}
                        // Serverul genereaza 2 canale default (general-tech, off-topic-lounge) sau le returneaza pe cele existente
                        var cResp = await _apiClient.PostAsync($"channels/generate-defaults/{project.DbId}", null);
                        if (cResp.IsSuccessStatusCode)
                        {
                            var cArr = JArray.Parse(await cResp.Content.ReadAsStringAsync());
                            var textChannels = new ObservableCollection<ChannelItem>();
                            var voiceChannels = new ObservableCollection<ChannelItem>();
                            var allItems = new List<ChannelItem>();
                            foreach (var c in cArr)
                            {
                                int chType = 0;
                                var typeToken = c["type"];
                                if (typeToken != null)
                                {
                                    if (typeToken.Type == JTokenType.Integer)
                                        chType = typeToken.ToObject<int>();
                                    else if (typeToken.Type == JTokenType.String)
                                    {
                                        var s = typeToken.ToString();
                                        if (s == "Voice" || s == "2") chType = 2;
                                        else if (s == "OffTopic" || s == "1") chType = 1;
                                    }
                                }
                                allItems.Add(new ChannelItem
                                {
                                    Id = c["id"]?.ToObject<int>() ?? 0,
                                    Name = c["name"]?.ToString() ?? "",
                                    Type = chType
                                });
                            }
                            foreach (var item in allItems.OrderBy(x => x.Id))
                            {
                                if (item.Type == 2)
                                    voiceChannels.Add(item);
                                else
                                    textChannels.Add(item);
                            }
                            _projectChannels[project.name] = textChannels;

                            foreach (var vc in voiceChannels)
                                vc.VoiceGroupKey = $"{project.DbId}_{vc.Name}";
                            _projectVoiceChannels[project.name] = voiceChannels;
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

                                var member = new MemberItem
                                {
                                    Username = memberUsername,
                                    Initials = memberUsername.Length >= 2 ? memberUsername.Substring(0, 2).ToUpper() : memberUsername.ToUpper(),
                                    Role = role,
                                    IsOnline = memberUsername == _username,
                                    AvatarUrl = memberAvatar
                                };
                                _projectMembers[project.name].Add(member);
                                ResolveDisplayNameAsync(memberUsername, name => member.DisplayName = name);
                            }
                        }
                    }
                    catch { }

                    // Genereaza lista locala, chiar daca e goala (eroare API)
                    if (_projectMembers[project.name].Count == 0)
                    {
                        var member = new MemberItem
                        {
                            Username = _username,
                            Initials = UserInitials.Text,
                            Role = "Owner",
                            IsOnline = true,
                            AvatarUrl = _avatarUrl
                        };
                        _projectMembers[project.name].Add(member);
                        ResolveDisplayNameAsync(_username, name => member.DisplayName = name);
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

                    _ = PrefetchFullNamesAsync([.. _projectMembers[project.name].Select(m => m.Username)]);
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

            PopulateAudioDevices();
            LoadVoiceSettings();
            LoadSoundSettingsToUI();
            KeybindsList.ItemsSource = Keybinds;
            foreach (var kb in LoadKeybindList()) Keybinds.Add(kb);
            _inputVolumeScale = InputVolumeSlider.Value / 100.0;
            _outputVolumeScale = OutputVolumeSlider.Value / 100.0;
            _vadThresholdRms = 32767.0 * Math.Pow(10.0, SensitivitySlider.Value / 20.0);
            InputVolumeSlider.ValueChanged += (s, e2) => _inputVolumeScale = InputVolumeSlider.Value / 100.0;
            OutputVolumeSlider.ValueChanged += (s, e2) => _outputVolumeScale = OutputVolumeSlider.Value / 100.0;
            SensitivitySlider.ValueChanged += (s, e2) => _vadThresholdRms = 32767.0 * Math.Pow(10.0, SensitivitySlider.Value / 20.0);

            _ = FetchRecentGitHubActivity();
        }

        private async Task FetchRecentGitHubActivity()
        {
            try
            {
                using var ghClient = new HttpClient();
                ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                var resp = await ghClient.GetAsync($"https://api.github.com/users/{_username}/events/public");
                if (!resp.IsSuccessStatusCode) return;

                var eventsJson = Newtonsoft.Json.Linq.JArray.Parse(await resp.Content.ReadAsStringAsync());
                
                Application.Current.Dispatcher.Invoke(() => HomeActivities.Clear());
                int count = 0;
                foreach (var ev in eventsJson)
                {
                    if (count >= 5) break;

                    string type = ev["type"]?.ToString() ?? "";
                    string repoName = ev["repo"]?["name"]?.ToString() ?? "Unknown";
                    string createdAt = ev["created_at"]?.ToString() ?? "";
                    string timeAgo = "";

                    if (DateTime.TryParse(createdAt, out DateTime dt))
                    {
                        var span = DateTime.UtcNow - dt;
                        if (span.TotalHours < 1) timeAgo = $"{Math.Max(1, (int)span.TotalMinutes)} minutes ago";
                        else if (span.TotalDays < 1) timeAgo = $"{(int)span.TotalHours} hours ago";
                        else timeAgo = $"{(int)span.TotalDays} days ago";
                    }

                    var item = new ActivityItem { ProjectName = repoName.Split('/').Last(), TimeAgo = timeAgo };

                    if (type == "PushEvent")
                    {
                        var commits = ev["payload"]?["commits"] as Newtonsoft.Json.Linq.JArray;
                        int commitCount = commits?.Count ?? 0;
                        string branch = ev["payload"]?["ref"]?.ToString().Replace("refs/heads/", "") ?? "main";

                        item.Icon = "📦";
                        item.ActionTitle = $"Pushed {commitCount} commit{(commitCount != 1 ? "s" : "")} to {branch}";
                        item.ActionDetails = $" · by {_username}";
                    }
                    else if (type == "CreateEvent")
                    {
                        string refType = ev["payload"]?["ref_type"]?.ToString() ?? "";
                        item.Icon = "🚀";
                        item.ActionTitle = $"Created {refType}";
                        item.ActionDetails = $" · by {_username}";
                    }
                    else if (type == "IssueCommentEvent")
                    {
                        item.Icon = "💬";
                        item.ActionTitle = "Commented on an issue";
                        item.ActionDetails = $" · by {_username}";
                    }
                    else if (type == "IssuesEvent")
                    {
                        string action = ev["payload"]?["action"]?.ToString() ?? "";
                        item.Icon = "🐛";
                        item.ActionTitle = $"{char.ToUpper(action[0]) + action.Substring(1)} an issue";
                        item.ActionDetails = $" · by {_username}";
                    }
                    else if (type == "PullRequestEvent")
                    {
                        string action = ev["payload"]?["action"]?.ToString() ?? "";
                        item.Icon = "🔄";
                        item.ActionTitle = $"{char.ToUpper(action[0]) + action.Substring(1)} a pull request";
                        item.ActionDetails = $" · by {_username}";
                    }
                    else
                    {
                        continue;
                    }

                    Application.Current.Dispatcher.Invoke(() => HomeActivities.Add(item));
                    count++;
                }
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (HomeActivities.Count == 0)
                    {
                        HomeActivities.Add(new ActivityItem { Icon = "📭", ActionTitle = "No recent activity found", ProjectName = "Your GitHub", ActionDetails = " · Go make some commits!" });
                    }
                });
            }
            catch { }
        }

        private Dictionary<string, string> LoadServerIcons()
        {
            try
            {
                if (File.Exists(_serverIconsPath))
                    return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(_serverIconsPath)) ?? new();
            }
            catch { }
            return new();
        }

        private void SaveServerIcons()
        {
            try
            {
                var icons = new Dictionary<string, string>();
                foreach (var p in _projects)
                    if (!string.IsNullOrEmpty(p.CustomImagePath))
                        icons[p.id] = p.CustomImagePath;
                Directory.CreateDirectory(Path.GetDirectoryName(_serverIconsPath)!);
                File.WriteAllText(_serverIconsPath, JsonConvert.SerializeObject(icons));
            }
            catch { }
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

        private bool IsDragSelection()
        {
            if (!_listboxButtonHeld) return false;
            var pos = Mouse.GetPosition(this);
            return Math.Abs(pos.X - _listboxDownPos.X) > 2 || Math.Abs(pos.Y - _listboxDownPos.Y) > 2;
        }

        private void CancelDragSelection(ListBox listBox, SelectionChangedEventArgs e)
        {
            _restoringSelection = true;
            listBox.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
            _restoringSelection = false;
        }

        private async void ProjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_restoringSelection) return;
            if (IsDragSelection()) { CancelDragSelection(ProjectList, e); return; }
            MiniProfilePanel.Visibility = Visibility.Collapsed;
            if (ProjectList.SelectedItem is RepoItem selected)
            {
                // ---- SignalR Join/Leave Project Group ----
                string? oldProjectDbId = _projects.FirstOrDefault(p => p.name == _selectedProject)?.DbId.ToString();
                string newProjectDbId = selected.DbId.ToString();

                _selectedProject = selected.name;
                SelectedProjectName.Text = selected.name;
                ServerSettingsHeaderButton.Visibility = Visibility.Visible;

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

                if (!_projectVoiceChannels.ContainsKey(selected.name))
                    _projectVoiceChannels[selected.name] = new ObservableCollection<ChannelItem>();
                VoiceChannelsSectionHeader.Visibility = Visibility.Visible;
                VoiceChannelList.Visibility = Visibility.Visible;
                VoiceChannelList.ItemsSource = _projectVoiceChannels[selected.name];

                // Curata membrii voice stali si cere starea actuala de la server
                if (_projectVoiceChannels.TryGetValue(selected.name, out var vChannels))
                {
                    foreach (var ch in vChannels)
                    {
                        ch.VoiceMembers.Clear();
                        // Daca userul e deja conectat in acest canal, re-adauga-l imediat
                        if (ch == _currentVoiceChannel)
                            ch.VoiceMembers.Add(new VoiceMember { Username = _username, AvatarUrl = _avatarUrl });
                    }
                }
                if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                    try { await _hubConnection.InvokeAsync("RequestProjectVoiceSnapshot", selected.DbId.ToString()); } catch { }
                // VoiceConnectedBar stays visible if the user is in a voice channel from any project

                if (_projectMembers.TryGetValue(selected.name, out var members))
                {
                    RefreshMembersList();
                }

                HomeView.Visibility = Visibility.Collapsed;
                ChatView.Visibility = Visibility.Visible;
                ShowChannelPanel();
                
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

                m.IsOnline = _globalOnlineUsers.Contains(m.Username);

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

        private string? _profileGitHubUrl;
        private readonly Dictionary<string, string> _fullNameCache = [];

        private async Task PrefetchFullNamesAsync(List<string> usernames)
        {
            foreach (var username in usernames)
            {
                if (_fullNameCache.ContainsKey(username)) continue;
                try
                {
                    using var ghClient = new HttpClient();
                    ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                    ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
                    var resp = await ghClient.GetAsync($"https://api.github.com/users/{username}");
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
                        var name = json["name"]?.ToString();
                        _fullNameCache[username] = !string.IsNullOrEmpty(name) ? name : username;
                    }
                }
                catch { }
            }
        }

        private void MemberItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is MemberItem member && !member.IsHeader)
                _ = ShowMiniProfileAsync(member.Username, member.AvatarUrl);
        }

        private void ChatUser_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ChatMessage msg && !msg.IsDateSeparator && !msg.IsSystemMessage)
            {
                _ = ShowMiniProfileAsync(msg.Username, msg.AvatarUrl);
                e.Handled = true;
            }
        }

        private async Task ShowMiniProfileAsync(string username, string? avatarUrl)
        {
            MemberItem? member = null;
            foreach (var col in _projectMembers.Values)
            {
                member = col.FirstOrDefault(x => x.Username == username && !x.IsHeader);
                if (member != null) break;
            }

            string initials = username.Length >= 2 ? username[..2].ToUpper() : username.ToUpper();
            ProfileInitials.Text = member?.Initials ?? initials;

            if (!string.IsNullOrEmpty(avatarUrl))
            {
                ProfileAvatar.Source = new BitmapImage(new Uri(avatarUrl));
                ProfileAvatar.Visibility = Visibility.Visible;
            }
            else
            {
                ProfileAvatar.Source = null;
                ProfileAvatar.Visibility = Visibility.Collapsed;
            }

            ProfileFullName.Text = _fullNameCache.TryGetValue(username, out var cached) ? cached : username;
            ProfileUsername.Text = $"@{username}";
            ProfileRoles.Text = member?.HasDevRoles == true ? member.RoleBadges : (member?.Role ?? "Member");

            ProfileCommonProjects.ItemsSource = _projects
                .Where(p => _projectMembers.TryGetValue(p.name, out var col)
                            && col.Any(x => x.Username == username && !x.IsHeader))
                .Select(p => p.name)
                .ToList();

            _profileGitHubUrl = $"https://github.com/{username}";

            var cursor = Mouse.GetPosition(this);
            double panelW = 260, panelH = 420;
            double left = cursor.X + 14;
            double top = cursor.Y + 14;
            if (left + panelW > ActualWidth)  left = cursor.X - panelW - 14;
            if (top  + panelH > ActualHeight) top  = Math.Max(0, ActualHeight - panelH - 10);
            MiniProfilePanel.Margin = new Thickness(left, top, 0, 0);

            MiniProfilePanel.Visibility = Visibility.Visible;

            // Fetch full name in background if not cached
            ResolveDisplayNameAsync(username, (fullName) =>
            {
                if (MiniProfilePanel.Visibility == Visibility.Visible && ProfileUsername.Text == $"@{username}")
                    ProfileFullName.Text = fullName;
            });
        }

        private void ResolveDisplayNameAsync(string username, Action<string> onResolved)
        {
            if (_fullNameCache.TryGetValue(username, out var cached) && !string.IsNullOrEmpty(cached))
            {
                onResolved(cached);
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    using var ghClient = new HttpClient();
                    ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                    ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
                    var resp = await ghClient.GetAsync($"https://api.github.com/users/{username}");
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
                        var name = json["name"]?.ToString();
                        var fullName = !string.IsNullOrEmpty(name) ? name : username;
                        
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _fullNameCache[username] = fullName;
                            onResolved(fullName);
                        });
                    }
                }
                catch { }
            });
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Close user profile popup when clicking outside it
            if (UserProfilePopup.IsOpen)
            {
                var popupBorder = UserProfilePopup.Child as FrameworkElement;
                if (popupBorder != null)
                {
                    var pos = e.GetPosition(popupBorder);
                    if (pos.X < 0 || pos.Y < 0 || pos.X > popupBorder.ActualWidth || pos.Y > popupBorder.ActualHeight)
                        UserProfilePopup.IsOpen = false;
                }
            }

            if (MiniProfilePanel.Visibility != Visibility.Visible) return;
            var mpos = e.GetPosition(MiniProfilePanel);
            if (mpos.X < 0 || mpos.Y < 0 || mpos.X > MiniProfilePanel.ActualWidth || mpos.Y > MiniProfilePanel.ActualHeight)
            {
                MiniProfilePanel.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

        private void ProfileGitHubLink_Click(object sender, RoutedEventArgs e)
        {
            if (_profileGitHubUrl != null)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_profileGitHubUrl) { UseShellExecute = true });
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

            VoiceChannelsSectionHeader.Visibility = Visibility.Collapsed;
            VoiceChannelList.Visibility = Visibility.Collapsed;
            VoiceChannelList.ItemsSource = null;
            VoiceConnectedBar.Visibility = Visibility.Collapsed;
            if (_currentVoiceChannel != null) { _currentVoiceChannel.IsJoined = false; _currentVoiceChannel = null; }

            ChatView.Visibility = Visibility.Collapsed;
            HomeView.Visibility = Visibility.Visible;

            HomeWelcomeText.Text = $"Welcome back, {_username}!";
            HomeProjectCards.ItemsSource = _projects;

            // Apply Wide Sidebar
            ProjectPanelColumn.Width = new GridLength(240);
            ProjectList.ItemContainerStyle = (Style)FindResource("WideProjectItemStyle");
            ProjectList.ItemTemplate = (DataTemplate)FindResource("WideProjectItemTemplate");
            
            HomeButton.Width = 216;
            HomeIcon.Margin = new Thickness(0,0,8,0);
            HomeText.Visibility = Visibility.Visible;
            if (HomeButton.ToolTip is ToolTip ht) ht.Visibility = Visibility.Collapsed;
            
            AddProjectButton.Width = 216;
            AddProjectIcon.Margin = new Thickness(0,-2,8,0);
            AddProjectButton.Margin = new Thickness(0, 0, 0, 8);
            AddProjectStack.HorizontalAlignment = HorizontalAlignment.Center;
            AddProjectStack.Margin = new Thickness(0);
            AddProjectText.Visibility = Visibility.Visible;
            if (AddProjectButton.ToolTip is string) AddProjectButton.ToolTip = null;

            ProfileBarUsername.Visibility = Visibility.Visible;
            ProfileSettingsButton.Visibility = Visibility.Visible;
            ProfileSettingsButton.Margin = new Thickness(0,0,16,0);
            UserInitials.Margin = new Thickness(0);
            UserAvatarImage.Margin = new Thickness(0);
            ProfileBarUsername.Text = PopupUsername.Text; // Assuming it's already set or initialized
            if (ProfileBarBorder != null) ProfileBarBorder.Padding = new Thickness(16, 12, 16, 12);

            // Save current channel panel width before hiding
            if (ChannelPanelColumn.Width.Value > 0)
                _channelPanelWidth = ChannelPanelColumn.Width.Value;

            // Hide channel panel and its splitter on Home
            ChannelPanelColumn.Width = new GridLength(0);
            ChannelPanelColumn.MinWidth = 0;
            ChannelPanelColumn.MaxWidth = 0;
            ChannelPanelBorder.Visibility = Visibility.Collapsed;
            ChannelSplitterColumn.Width = new GridLength(0);
            ChannelSplitter.Visibility = Visibility.Collapsed;

            // Hide the Server Settings button when on home (no project selected)
            ServerSettingsHeaderButton.Visibility = Visibility.Collapsed;

            _membersPanelVisible = false;
            MembersPanelColumn.Width = new GridLength(0);
            MembersPanelBorder.Visibility = Visibility.Collapsed;
        }

        private void ShowChannelPanel()
        {

            // Apply Narrow Sidebar
            ProjectPanelColumn.Width = new GridLength(64);
            ProjectList.ItemContainerStyle = (Style)FindResource("NarrowProjectItemStyle");
            ProjectList.ItemTemplate = (DataTemplate)FindResource("NarrowProjectItemTemplate");
            
            HomeButton.Width = 40;
            HomeIcon.Margin = new Thickness(0);
            HomeIcon.HorizontalAlignment = HorizontalAlignment.Center;
            HomeText.Visibility = Visibility.Collapsed;
            if (HomeButton.ToolTip is ToolTip ht2) ht2.Visibility = Visibility.Visible;
            
            AddProjectButton.Width = 40;
            AddProjectIcon.Margin = new Thickness(0,-2,0,0);
            AddProjectIcon.HorizontalAlignment = HorizontalAlignment.Center;
            AddProjectButton.Margin = new Thickness(0, 0, 0, 8);
            AddProjectStack.HorizontalAlignment = HorizontalAlignment.Center;
            AddProjectStack.Margin = new Thickness(0);
            AddProjectText.Visibility = Visibility.Collapsed;
            AddProjectButton.ToolTip = "Add Project";

            UserInitials.Margin = new Thickness(0);
            UserAvatarImage.Margin = new Thickness(0);
            if (ProfileBarBorder != null) ProfileBarBorder.Padding = new Thickness(16, 12, 16, 12);
            if (ProfileBarUsername != null) ProfileBarUsername.Visibility = Visibility.Visible;
            if (ProfileSettingsButton != null)
            {
                ProfileSettingsButton.Visibility = Visibility.Visible;
                ProfileSettingsButton.Margin = new Thickness(0, 0, 16, 0);
            }

            ChannelPanelColumn.MinWidth = 180;
            ChannelPanelColumn.MaxWidth = 360;
            // Restore persisted width
            ChannelPanelColumn.Width = new GridLength(Math.Max(180, Math.Min(360, _channelPanelWidth)));
            ChannelPanelBorder.Visibility = Visibility.Visible;
            // Show the splitter so user can resize the channel panel
            ChannelSplitterColumn.Width = new GridLength(4);
            ChannelSplitter.Visibility = Visibility.Visible;
        }

        // Navigate to a project by clicking a dashboard card
        private void HomeProjectCard_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is RepoItem project)
            {
                var idx = _projects.IndexOf(project);
                if (idx >= 0)
                    ProjectList.SelectedIndex = idx;
            }
        }

        // User avatar bar clicked → show popup flyout
        private void UserAvatarBar_Click(object sender, MouseButtonEventArgs e)
        {
            // Prefer the real GitHub display name cached from API; fall back to _displayName, then @username
            string realName = _username;
            if (_fullNameCache.TryGetValue(_username, out var cached) && !string.IsNullOrEmpty(cached))
                realName = cached;
            else if (!string.IsNullOrEmpty(_displayName))
                realName = _displayName;
            PopupUsername.Text = realName;
            UserProfilePopup.IsOpen = !UserProfilePopup.IsOpen;
        }



        private void PopupLogout_Click(object sender, MouseButtonEventArgs e)
        {
            UserProfilePopup.IsOpen = false;
            LogoutButton_Click(sender, e);
        }

        // Sound pack toggle in Settings → Appearance section
        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            _soundPackFolder = "Sounds_Themed";
            if (ThemeModeLabel != null) ThemeModeLabel.Text = "Currently: Themed";
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _soundPackFolder = "Sounds_Default";
            if (ThemeModeLabel != null) ThemeModeLabel.Text = "Currently: Default";
        }

        // Bubble scroll events from ListBox up to the parent ScrollViewer
        // Prevent drag-selection on ListBoxes.
        // WPF fires synthetic PreviewMouseLeftButtonDown on items entered while button is held,
        // AND does drag-selection in ListBox.OnMouseMove — both need to be blocked.
        private Point _listboxDownPos;
        private bool  _listboxDragging = false;
        private bool  _listboxButtonHeld = false;
        private bool  _restoringSelection = false;

        private void ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_listboxButtonHeld)
            {
                // Synthetic button-down fired because mouse entered a new item while button is held.
                // This fires BEFORE PreviewMouseMove, so _listboxDragging would still be false here —
                // using _listboxButtonHeld instead catches it reliably.
                _listboxDragging = true;
                e.Handled = true;
                return;
            }
            _listboxDownPos = e.GetPosition(this);
            _listboxButtonHeld = true;
            _listboxDragging = false;
        }

        private void ListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _listboxDragging = false; _listboxButtonHeld = false; return; }
            if (!_listboxDragging)
            {
                var pos = e.GetPosition(this);
                if (Math.Abs(pos.X - _listboxDownPos.X) > 2 ||
                    Math.Abs(pos.Y - _listboxDownPos.Y) > 2)
                    _listboxDragging = true;
            }
            if (_listboxDragging) e.Handled = true;
        }

        private void ListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _listboxDragging = false;
            _listboxButtonHeld = false;
        }

        private void ChannelList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                ((UIElement)sender).RaiseEvent(eventArg);
            }
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for future theme switching logic
        }


        private async void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_restoringSelection) return;
            if (IsDragSelection()) { CancelDragSelection(ChannelList, e); return; }
            if (ChannelList.SelectedItem is ChannelItem selectedChannel && _selectedProject != null)
            {
                // Save draft for the old channel
                if (_selectedChannelId > 0)
                {
                    _channelDrafts[_selectedChannelId] = MessageInput.Text ?? "";
                }

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
                    DateTime? prevMsgDate = null;
                    foreach (var m in mArr)
                    {
                        string content = m["content"]?.ToString() ?? "";
                        DateTime msgLocalTime = DateTime.Now;
                        try { msgLocalTime = m["sentAt"]?.ToObject<DateTime>().ToLocalTime() ?? DateTime.Now; }
                        catch { }
                        string time = msgLocalTime.ToString("HH:mm");

                        bool msgIsDeleted = m["isDeleted"]?.ToObject<bool>() ?? false;
                        if (msgIsDeleted) continue;

                        int msgUserId = m["userId"]?.ToObject<int>() ?? 0;
                        string msgUsernameFromApi = m["user"]?["username"]?.ToString() ?? "";
                        string msgAvatarFromApi = m["user"]?["avatarUrl"]?.ToString() ?? "";

                        bool isOwn = (msgUserId != 0 && msgUserId == _currentUserId)
                                  || string.Equals(msgUsernameFromApi, _username, StringComparison.OrdinalIgnoreCase);

                        string msgUsername = isOwn ? _username
                            : (!string.IsNullOrEmpty(msgUsernameFromApi) ? msgUsernameFromApi : $"User#{msgUserId}");
                        string msgAvatarUrl = isOwn ? _avatarUrl : msgAvatarFromApi;

                        if (!prevMsgDate.HasValue || msgLocalTime.Date != prevMsgDate.Value)
                        {
                            Messages.Add(new ChatMessage { IsDateSeparator = true, DateLabel = FormatDateLabel(msgLocalTime.Date) });
                            prevMsgDate = msgLocalTime.Date;
                        }
                        Messages.Add(ParseMessageContent(new ChatMessage
                        {
                            MessageId = m["id"]?.ToObject<int>() ?? 0,
                            Username = msgUsername,
                            Initials = msgUsername.Length >= 2 ? msgUsername.Substring(0, 2).ToUpper() : msgUsername.ToUpper(),
                            AvatarColor = isOwn ? "#238636" : "#8B949E",
                            UsernameColor = isOwn ? "#238636" : "#E6EDF3",
                            AvatarUrl = string.IsNullOrEmpty(msgAvatarUrl) ? null : msgAvatarUrl,
                            Content = content,
                            Timestamp = time,
                            IsSystemMessage = false,
                            IsMentioningMe = content.Contains("@" + _username, StringComparison.OrdinalIgnoreCase),
                            MessageDate = msgLocalTime,
                            IsOwnMessage = isOwn && !msgIsDeleted,
                            IsEdited = m["isEdited"]?.ToObject<bool>() ?? false,
                            IsDeleted = msgIsDeleted
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
                _lastSeenMessageCount[_selectedChannelId] = Messages.Count(m => !m.IsSystemMessage && !m.IsDateSeparator);
                UpdateProjectBadge(_selectedProject ?? "");

                // Restore draft for the new channel
                if (_channelDrafts.TryGetValue(_selectedChannelId, out var draft))
                {
                    MessageInput.Text = draft;
                    MessageInput.CaretIndex = draft.Length;
                }
                else
                {
                    MessageInput.Text = "";
                }

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

        private async void VoiceChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_restoringSelection) return;
            if (IsDragSelection()) { CancelDragSelection(VoiceChannelList, e); return; }
            if (_selectedProject == null) return;
            if (VoiceChannelList.SelectedItem is not ChannelItem selected) return;
            VoiceChannelList.SelectedIndex = -1;

            if (_currentVoiceChannel == selected) return;
            LeaveCurrentVoiceChannel();
            selected.IsJoined = true;
            selected.VoiceMembers.Add(new VoiceMember { Username = _username, DisplayName = _displayName, AvatarUrl = _avatarUrl });
            _currentVoiceChannel = selected;
            var projectDbId = _projects.FirstOrDefault(p => p.name == _selectedProject)?.DbId ?? 0;
            _currentVoiceGroupKey = $"{projectDbId}_{selected.Name}";
            VoiceConnectedChannelName.Text = $"#{selected.Name}";
            VoiceConnectedBar.Visibility = Visibility.Visible;

            // Redam sunetul INSTANT, inainte de delay-ul de la net
            PlaySound("intrare_voice.wav", _soundSettings.JoinVoice);

            if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
            {
                try { await _hubConnection.InvokeAsync("JoinVoiceChannel", _currentVoiceGroupKey, _username); } catch { }
            }

            StartAudioCaptureAndPlayback();
        }

        private VoiceMember? FindVoiceMember(string username)
        {
            foreach (var channels in _projectVoiceChannels.Values)
                foreach (var ch in channels)
                {
                    var m = ch.VoiceMembers.FirstOrDefault(v => v.Username == username);
                    if (m != null) return m;
                }
            return null;
        }

        private string? GetAvatarUrl(string username)
        {
            if (username == _username) return _avatarUrl;
            foreach (var members in _projectMembers.Values)
            {
                var m = members.FirstOrDefault(x => x.Username == username);
                if (!string.IsNullOrEmpty(m?.AvatarUrl)) return m.AvatarUrl;
            }
            return null;
        }

        private string GetDisplayName(string username)
        {
            if (username == _username) return _displayName;
            foreach (var members in _projectMembers.Values)
            {
                var m = members.FirstOrDefault(x => x.Username == username);
                if (m != null && !string.IsNullOrEmpty(m.DisplayName) && m.DisplayName != m.Username) return m.DisplayName;
            }
            if (_fullNameCache.TryGetValue(username, out var name)) return name;
            return username;
        }

        private ChannelItem? FindVoiceChannelByKey(string channelKey)
        {
            foreach (var kvp in _projectVoiceChannels)
            {
                var ch = kvp.Value.FirstOrDefault(c => c.VoiceGroupKey == channelKey);
                if (ch != null) return ch;
            }
            return null;
        }

        private void StartAudioCaptureAndPlayback()
        {
            try
            {
                _waveIn = new WaveInEvent { DeviceNumber = GetWaveInDeviceIndex(InputDeviceCombo.SelectedItem?.ToString()) };
                _waveIn.WaveFormat = new WaveFormat(44100, 16, 1);
                _waveIn.BufferMilliseconds = 100;
                _waveIn.DataAvailable += async (s, args) =>
                {
                    bool transmitting = false;

                    if (!_isMuted && _currentVoiceChannel != null && _currentVoiceGroupKey != null
                        && _hubConnection != null && _hubConnection.State == HubConnectionState.Connected
                        && (_isVadMode || _isPttActive))
                    {
                        byte[] buffer = new byte[args.BytesRecorded];
                        Array.Copy(args.Buffer, buffer, args.BytesRecorded);

                        // Apply input volume scaling
                        if (Math.Abs(_inputVolumeScale - 1.0) > 0.01)
                        {
                            for (int i = 0; i < buffer.Length - 1; i += 2)
                            {
                                int sample = BitConverter.ToInt16(buffer, i);
                                sample = Math.Clamp((int)(sample * _inputVolumeScale), short.MinValue, short.MaxValue);
                                buffer[i] = (byte)(sample & 0xFF);
                                buffer[i + 1] = (byte)((sample >> 8) & 0xFF);
                            }
                        }

                        // VAD gate: in VAD mode, only send if RMS exceeds threshold
                        bool vadPassed = !_isVadMode;
                        if (_isVadMode)
                        {
                            double sumSq = 0;
                            int count = buffer.Length / 2;
                            for (int i = 0; i < buffer.Length - 1; i += 2)
                            {
                                double sample = BitConverter.ToInt16(buffer, i);
                                sumSq += sample * sample;
                            }
                            vadPassed = Math.Sqrt(sumSq / count) >= _vadThresholdRms;
                        }

                        if (vadPassed)
                        {
                            transmitting = true;
                            try { await _hubConnection.InvokeAsync("SendAudioBuffer", _currentVoiceGroupKey, _username, buffer); } catch { }
                        }
                    }

                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        TransmittingDot.Fill = new SolidColorBrush(transmitting
                            ? Color.FromRgb(0x3F, 0xB9, 0x50)
                            : Color.FromRgb(0x48, 0x4F, 0x58));
                        var localMember = FindVoiceMember(_username);
                        if (localMember != null) localMember.IsSpeaking = transmitting;
                    });
                };

                _waveProvider = new BufferedWaveProvider(new WaveFormat(44100, 16, 1));
                _waveProvider.DiscardOnBufferOverflow = true;

                _waveOut = new WaveOutEvent { DeviceNumber = GetWaveOutDeviceIndex(OutputDeviceCombo.SelectedItem?.ToString()) };
                _waveOut.Init(_waveProvider);

                _waveOut.Play();
                _waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not start audio devices: {ex.Message}");
            }
        }

        private async void LeaveCurrentVoiceChannel()
        {
            // UI reset trebuie sa fie sincron (inainte de orice await), altfel un join concurent
            // seteaza bara Visible si dupa await o suprascrie cu Collapsed.
            VoiceConnectedBar.Visibility = Visibility.Collapsed;
            ResetMuteDeafen();
            StopAudioCaptureAndPlayback();

            if (_currentVoiceChannel != null)
            {
                // Captura referintele inainte de await ca sa nu fie suprascrise de un join concurent
                var leavingChannel = _currentVoiceChannel;
                var leavingKey = _currentVoiceGroupKey;
                _currentVoiceChannel = null;
                _currentVoiceGroupKey = null;

                PlaySound("iesire_voice.wav", _soundSettings.LeaveVoice);

                leavingChannel.IsJoined = false;
                var selfMember = leavingChannel.VoiceMembers.FirstOrDefault(m => m.Username == _username);
                if (selfMember != null) leavingChannel.VoiceMembers.Remove(selfMember);

                if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected && leavingKey != null)
                {
                    try { await _hubConnection.InvokeAsync("LeaveVoiceChannel", leavingKey, _username); } catch { }
                }
            }
        }

        private void StopAudioCaptureAndPlayback()
        {
            if (_waveIn != null)
            {
                try { _waveIn.StopRecording(); } catch { }
                _waveIn.Dispose();
                _waveIn = null;
            }
            if (_waveOut != null)
            {
                try { _waveOut.Stop(); } catch { }
                _waveOut.Dispose();
                _waveOut = null;
            }
            _waveProvider = null;
        }

        private void ResetMuteDeafen()
        {
            _isMuted = false;
            _isDeafened = false;
            MuteIcon.Fill = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
            DeafenIcon.Fill = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
        }

        private async void MuteVoice_Click(object sender, RoutedEventArgs e) => await ExecuteToggleMuteAsync();
        private async void DeafenVoice_Click(object sender, RoutedEventArgs e) => await ExecuteToggleDeafenAsync();

        private async Task ExecuteToggleMuteAsync()
        {
            _isMuted = !_isMuted;
            if (_isMuted) PlaySound("mute.wav", _soundSettings.Mute);
            else PlaySound("unmute.wav", _soundSettings.Unmute);
            MuteIcon.Fill = new SolidColorBrush(_isMuted
                ? Color.FromRgb(0xDA, 0x36, 0x33) : Color.FromRgb(0x8B, 0x94, 0x9E));
            var me = _currentVoiceChannel?.VoiceMembers.FirstOrDefault(m => m.Username == _username);
            if (me != null) me.IsMuted = _isMuted;
            if (_currentVoiceGroupKey != null && _hubConnection?.State == HubConnectionState.Connected)
                try { await _hubConnection.InvokeAsync("BroadcastVoiceState", _currentVoiceGroupKey, _username, _isMuted, _isDeafened); } catch { }
        }

        private async Task ExecuteToggleDeafenAsync()
        {
            _isDeafened = !_isDeafened;
            if (_isDeafened) PlaySound("mute.wav", _soundSettings.Deafen);
            else PlaySound("unmute.wav", _soundSettings.Undeafen);
            DeafenIcon.Fill = new SolidColorBrush(_isDeafened
                ? Color.FromRgb(0xDA, 0x36, 0x33) : Color.FromRgb(0x8B, 0x94, 0x9E));
            var me = _currentVoiceChannel?.VoiceMembers.FirstOrDefault(m => m.Username == _username);
            if (me != null) me.IsDeafened = _isDeafened;
            if (_currentVoiceGroupKey != null && _hubConnection?.State == HubConnectionState.Connected)
                try { await _hubConnection.InvokeAsync("BroadcastVoiceState", _currentVoiceGroupKey, _username, _isMuted, _isDeafened); } catch { }
        }

        private void VoiceChannelList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void VoiceSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            PopulateAudioDevices();
            VoiceSettingsOverlay.Visibility = Visibility.Visible;
        }

        private void ApplicationSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            PopulateAudioDevices();
            VoiceSettingsOverlay.Visibility = Visibility.Visible;
        }

        private void PopulateAudioDevices()
        {
            var prevInput = InputDeviceCombo.SelectedItem?.ToString();
            var prevOutput = OutputDeviceCombo.SelectedItem?.ToString();

            InputDeviceCombo.Items.Clear();
            OutputDeviceCombo.Items.Clear();

            try
            {
                var enumerator = new MMDeviceEnumerator();
                foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                    InputDeviceCombo.Items.Add(d.FriendlyName);
                foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    OutputDeviceCombo.Items.Add(d.FriendlyName);
            }
            catch { }

            if (InputDeviceCombo.Items.Count == 0) InputDeviceCombo.Items.Add("No input device");
            if (OutputDeviceCombo.Items.Count == 0) OutputDeviceCombo.Items.Add("No output device");

            RestoreDeviceSelection(prevInput, prevOutput);
        }

        private int GetWaveInDeviceIndex(string? friendlyName)
        {
            if (string.IsNullOrEmpty(friendlyName)) return 0;
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var name = WaveIn.GetCapabilities(i).ProductName;
                if (friendlyName.Contains(name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }

        private int GetWaveOutDeviceIndex(string? friendlyName)
        {
            if (string.IsNullOrEmpty(friendlyName)) return 0;
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var name = WaveOut.GetCapabilities(i).ProductName;
                if (friendlyName.Contains(name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }

        private void RestoreDeviceSelection(string? inputName, string? outputName)
        {
            InputDeviceCombo.SelectedIndex = 0;
            for (int i = 0; i < InputDeviceCombo.Items.Count; i++)
                if (InputDeviceCombo.Items[i]?.ToString() == inputName) { InputDeviceCombo.SelectedIndex = i; break; }

            OutputDeviceCombo.SelectedIndex = 0;
            for (int i = 0; i < OutputDeviceCombo.Items.Count; i++)
                if (OutputDeviceCombo.Items[i]?.ToString() == outputName) { OutputDeviceCombo.SelectedIndex = i; break; }
        }

        private void LoadVoiceSettings()
        {
            try
            {
                if (!File.Exists(_voiceSettingsPath)) return;
                var s = JsonConvert.DeserializeObject<VoiceSettings>(File.ReadAllText(_voiceSettingsPath));
                if (s == null) return;

                InputVolumeSlider.Value = s.InputVolume;
                OutputVolumeSlider.Value = s.OutputVolume;
                SensitivitySlider.Value = s.Sensitivity;
                _pttKey = s.PttKey;
                PttKeyDisplay.Text = s.PttKey;

                if (s.IsPttMode) PttModeRadio.IsChecked = true;
                else VadModeRadio.IsChecked = true;

                RestoreDeviceSelection(s.InputDevice, s.OutputDevice);
            }
            catch { }
        }

        private void SaveVoiceSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_voiceSettingsPath)!);
                var s = new VoiceSettings
                {
                    InputVolume = InputVolumeSlider.Value,
                    OutputVolume = OutputVolumeSlider.Value,
                    Sensitivity = SensitivitySlider.Value,
                    IsPttMode = PttModeRadio.IsChecked == true,
                    PttKey = _pttKey,
                    InputDevice = InputDeviceCombo.SelectedItem?.ToString() ?? "",
                    OutputDevice = OutputDeviceCombo.SelectedItem?.ToString() ?? ""
                };
                File.WriteAllText(_voiceSettingsPath, JsonConvert.SerializeObject(s, Formatting.Indented));
            }
            catch { }
        }

        private class VoiceSettings
        {
            public double InputVolume { get; set; } = 100;
            public double OutputVolume { get; set; } = 100;
            public double Sensitivity { get; set; } = -40;
            public bool IsPttMode { get; set; } = false;
            public string PttKey { get; set; } = "Caps Lock";
            public string InputDevice { get; set; } = "";
            public string OutputDevice { get; set; } = "";
        }

        private class SoundConfig
        {
            public bool Enabled { get; set; } = true;
            public double Volume { get; set; } = 50;
        }

        private class AppSoundSettings
        {
            public SoundConfig Startup { get; set; } = new();
            public SoundConfig JoinVoice { get; set; } = new();
            public SoundConfig LeaveVoice { get; set; } = new();
            public SoundConfig Mute { get; set; } = new();
            public SoundConfig Unmute { get; set; } = new();
            public SoundConfig Deafen { get; set; } = new();
            public SoundConfig Undeafen { get; set; } = new();
            public SoundConfig Message { get; set; } = new();
            public SoundConfig Mention { get; set; } = new();
        }

        private static AppSoundSettings LoadSoundSettings()
        {
            try
            {
                if (File.Exists(_soundSettingsPath))
                {
                    var loaded = JsonConvert.DeserializeObject<AppSoundSettings>(File.ReadAllText(_soundSettingsPath));
                    if (loaded != null) return loaded;
                }
            }
            catch { }
            return new AppSoundSettings();
        }

        private void SaveSoundSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_soundSettingsPath)!);
                File.WriteAllText(_soundSettingsPath, JsonConvert.SerializeObject(_soundSettings, Formatting.Indented));
            }
            catch { }
        }

        private void LoadSoundSettingsToUI()
        {

            SoundJoinVoiceCheck.IsChecked = _soundSettings.JoinVoice.Enabled;
            SoundJoinVoiceSlider.Value = _soundSettings.JoinVoice.Volume;
            SoundLeaveVoiceCheck.IsChecked = _soundSettings.LeaveVoice.Enabled;
            SoundLeaveVoiceSlider.Value = _soundSettings.LeaveVoice.Volume;
            SoundMuteCheck.IsChecked = _soundSettings.Mute.Enabled;
            SoundMuteSlider.Value = _soundSettings.Mute.Volume;
            SoundUnmuteCheck.IsChecked = _soundSettings.Unmute.Enabled;
            SoundUnmuteSlider.Value = _soundSettings.Unmute.Volume;
            SoundDeafenCheck.IsChecked = _soundSettings.Deafen.Enabled;
            SoundDeafenSlider.Value = _soundSettings.Deafen.Volume;
            SoundUndeafenCheck.IsChecked = _soundSettings.Undeafen.Enabled;
            SoundUndeafenSlider.Value = _soundSettings.Undeafen.Volume;
            SoundMessageCheck.IsChecked = _soundSettings.Message.Enabled;
            SoundMessageSlider.Value = _soundSettings.Message.Volume;
            SoundMentionCheck.IsChecked = _soundSettings.Mention.Enabled;
            SoundMentionSlider.Value = _soundSettings.Mention.Volume;
        }

        private void SaveSoundSettingsFromUI()
        {

            _soundSettings.JoinVoice.Enabled = SoundJoinVoiceCheck.IsChecked == true;
            _soundSettings.JoinVoice.Volume = SoundJoinVoiceSlider.Value;
            _soundSettings.LeaveVoice.Enabled = SoundLeaveVoiceCheck.IsChecked == true;
            _soundSettings.LeaveVoice.Volume = SoundLeaveVoiceSlider.Value;
            _soundSettings.Mute.Enabled = SoundMuteCheck.IsChecked == true;
            _soundSettings.Mute.Volume = SoundMuteSlider.Value;
            _soundSettings.Unmute.Enabled = SoundUnmuteCheck.IsChecked == true;
            _soundSettings.Unmute.Volume = SoundUnmuteSlider.Value;
            _soundSettings.Deafen.Enabled = SoundDeafenCheck.IsChecked == true;
            _soundSettings.Deafen.Volume = SoundDeafenSlider.Value;
            _soundSettings.Undeafen.Enabled = SoundUndeafenCheck.IsChecked == true;
            _soundSettings.Undeafen.Volume = SoundUndeafenSlider.Value;
            _soundSettings.Message.Enabled = SoundMessageCheck.IsChecked == true;
            _soundSettings.Message.Volume = SoundMessageSlider.Value;
            _soundSettings.Mention.Enabled = SoundMentionCheck.IsChecked == true;
            _soundSettings.Mention.Volume = SoundMentionSlider.Value;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void SetTabActive(Button tab, bool active)
        {
            var border = FindVisualChild<Border>(tab);
            var text = FindVisualChild<TextBlock>(tab);
            var green = new SolidColorBrush(Color.FromRgb(0x23, 0x86, 0x36));
            var grey = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
            if (border != null) border.BorderBrush = active ? green : Brushes.Transparent;
            if (text != null) text.Foreground = active ? green : grey;
        }

        private void SettingsTabVoice_Click(object sender, RoutedEventArgs e)
        {
            VoiceTabContent.Visibility = Visibility.Visible;
            AppTabContent.Visibility = Visibility.Collapsed;
            KeybindsTabContent.Visibility = Visibility.Collapsed;
            SetTabActive(SettingsTabVoiceBtn, true);
            SetTabActive(SettingsTabAppBtn, false);
            SetTabActive(SettingsTabKeybindsBtn, false);
        }

        private void SettingsTabApp_Click(object sender, RoutedEventArgs e)
        {
            VoiceTabContent.Visibility = Visibility.Collapsed;
            AppTabContent.Visibility = Visibility.Visible;
            KeybindsTabContent.Visibility = Visibility.Collapsed;
            SetTabActive(SettingsTabVoiceBtn, false);
            SetTabActive(SettingsTabAppBtn, true);
            SetTabActive(SettingsTabKeybindsBtn, false);
        }

        private void SettingsTabKeybinds_Click(object sender, RoutedEventArgs e)
        {
            VoiceTabContent.Visibility = Visibility.Collapsed;
            AppTabContent.Visibility = Visibility.Collapsed;
            KeybindsTabContent.Visibility = Visibility.Visible;
            SetTabActive(SettingsTabVoiceBtn, false);
            SetTabActive(SettingsTabAppBtn, false);
            SetTabActive(SettingsTabKeybindsBtn, true);
        }

        private void CaptureKeybind_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not KeybindEntry entry) return;
            _capturingKeybindPrevKey = entry.Key;
            _capturingKeybindEntry = entry;
            entry.Key = "Press a key...";
        }

        private void AddKeybind_Click(object sender, RoutedEventArgs e)
        {
            Keybinds.Add(new KeybindEntry());
            SaveKeybinds();
        }

        private void DeleteKeybind_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not KeybindEntry entry) return;
            Keybinds.Remove(entry);
            SaveKeybinds();
        }

        private List<KeybindEntry> LoadKeybindList()
        {
            try
            {
                if (File.Exists(_keybindsPath))
                {
                    var loaded = JsonConvert.DeserializeObject<List<KeybindEntry>>(File.ReadAllText(_keybindsPath));
                    if (loaded != null) return loaded;
                }
            }
            catch { }
            return new List<KeybindEntry>();
        }

        private void SaveKeybinds()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_keybindsPath)!);
                File.WriteAllText(_keybindsPath, JsonConvert.SerializeObject(Keybinds.ToList(), Formatting.Indented));
            }
            catch { }
        }



        private void PreviewJoinVoice_Click(object sender, RoutedEventArgs e)
            => PlaySound("intrare_voice.wav", new SoundConfig { Enabled = true, Volume = SoundJoinVoiceSlider.Value });

        private void PreviewLeaveVoice_Click(object sender, RoutedEventArgs e)
            => PlaySound("iesire_voice.wav", new SoundConfig { Enabled = true, Volume = SoundLeaveVoiceSlider.Value });

        private void PreviewMute_Click(object sender, RoutedEventArgs e)
            => PlaySound("mute.wav", new SoundConfig { Enabled = true, Volume = SoundMuteSlider.Value });

        private void PreviewUnmute_Click(object sender, RoutedEventArgs e)
            => PlaySound("unmute.wav", new SoundConfig { Enabled = true, Volume = SoundUnmuteSlider.Value });

        private void PreviewDeafen_Click(object sender, RoutedEventArgs e)
            => PlaySound("mute.wav", new SoundConfig { Enabled = true, Volume = SoundDeafenSlider.Value });

        private void PreviewUndeafen_Click(object sender, RoutedEventArgs e)
            => PlaySound("unmute.wav", new SoundConfig { Enabled = true, Volume = SoundUndeafenSlider.Value });

        private void PreviewMessage_Click(object sender, RoutedEventArgs e)
            => PlaySound("mesaje.wav", new SoundConfig { Enabled = true, Volume = SoundMessageSlider.Value });

        private void PreviewMention_Click(object sender, RoutedEventArgs e)
            => PlaySound("mention.wav", new SoundConfig { Enabled = true, Volume = SoundMentionSlider.Value });

        private void VoiceSettingsOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_capturingPttKey) { _capturingPttKey = false; PttCaptureHint.Visibility = Visibility.Collapsed; PttKeyButton.IsEnabled = true; }
            _capturingKeybindEntry = null;
            SaveVoiceSettings();
            SaveSoundSettingsFromUI();
            SaveSoundSettings();
            SaveKeybinds();
            VoiceSettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void CloseVoiceSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_capturingPttKey) { _capturingPttKey = false; PttCaptureHint.Visibility = Visibility.Collapsed; PttKeyButton.IsEnabled = true; }
            _capturingKeybindEntry = null;
            SaveVoiceSettings();
            SaveSoundSettingsFromUI();
            SaveSoundSettings();
            SaveKeybinds();
            VoiceSettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void VadMode_Checked(object sender, RoutedEventArgs e)
        {
            if (SensitivityPanel == null) return;
            SensitivityPanel.Visibility = Visibility.Visible;
            PttKeyPanel.Visibility = Visibility.Collapsed;
            _isVadMode = true;
        }

        private void PttMode_Checked(object sender, RoutedEventArgs e)
        {
            if (PttKeyPanel == null) return;
            SensitivityPanel.Visibility = Visibility.Collapsed;
            PttKeyPanel.Visibility = Visibility.Visible;
            _isVadMode = false;
        }

        private void PttKeyButton_Click(object sender, RoutedEventArgs e)
        {
            _capturingPttKey = true;
            PttKeyButton.IsEnabled = false;
            PttCaptureHint.Visibility = Visibility.Visible;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (_capturingPttKey)
            {
                e.Handled = true;
                _capturingPttKey = false;
                PttCaptureHint.Visibility = Visibility.Collapsed;
                PttKeyButton.IsEnabled = true;
                if (e.Key != Key.Escape)
                {
                    _pttKey = new KeyConverter().ConvertToString(e.Key) ?? e.Key.ToString();
                    PttKeyDisplay.Text = _pttKey;
                }
                return;
            }

            if (_capturingKeybindEntry != null)
            {
                e.Handled = true;
                _capturingKeybindEntry.Key = e.Key != Key.Escape
                    ? (new KeyConverter().ConvertToString(e.Key) ?? e.Key.ToString())
                    : _capturingKeybindPrevKey;
                _capturingKeybindEntry = null;
                return;
            }

            if (!_isVadMode && !_isPttActive)
            {
                var keyStr = new KeyConverter().ConvertToString(e.Key) ?? e.Key.ToString();
                if (keyStr == _pttKey) _isPttActive = true;
            }

            if (!e.IsRepeat)
            {
                var kStr = new KeyConverter().ConvertToString(e.Key) ?? e.Key.ToString();
                foreach (var kb in Keybinds)
                {
                    if (kb.Key != kStr) continue;
                    switch (kb.Action)
                    {
                        case "Toggle Mute":   _ = ExecuteToggleMuteAsync();   e.Handled = true; break;
                        case "Toggle Deafen": _ = ExecuteToggleDeafenAsync(); e.Handled = true; break;
                        case "Push to Mute":
                            if (!_pushMuteActive && !_isMuted) { _pushMuteActive = true; _ = ExecuteToggleMuteAsync(); e.Handled = true; }
                            break;
                        case "Push to Deafen":
                            if (!_pushDeafenActive && !_isDeafened) { _pushDeafenActive = true; _ = ExecuteToggleDeafenAsync(); e.Handled = true; }
                            break;
                    }
                }
            }

            base.OnPreviewKeyDown(e);
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            if (!_isVadMode && _isPttActive)
            {
                var keyStr = new KeyConverter().ConvertToString(e.Key) ?? e.Key.ToString();
                if (keyStr == _pttKey) _isPttActive = false;
            }

            var kStr = new KeyConverter().ConvertToString(e.Key) ?? e.Key.ToString();
            foreach (var kb in Keybinds)
            {
                if (kb.Key != kStr) continue;
                switch (kb.Action)
                {
                    case "Push to Mute":
                        if (_pushMuteActive && _isMuted) { _pushMuteActive = false; _ = ExecuteToggleMuteAsync(); }
                        break;
                    case "Push to Deafen":
                        if (_pushDeafenActive && _isDeafened) { _pushDeafenActive = false; _ = ExecuteToggleDeafenAsync(); }
                        break;
                }
            }

            base.OnPreviewKeyUp(e);
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (_capturingPttKey)
            {
                e.Handled = true;
                _capturingPttKey = false;
                PttCaptureHint.Visibility = Visibility.Collapsed;
                PttKeyButton.IsEnabled = true;
                _pttKey = e.ChangedButton switch
                {
                    MouseButton.Left   => "Mouse Left",
                    MouseButton.Right  => "Mouse Right",
                    MouseButton.Middle => "Mouse Middle",
                    MouseButton.XButton1 => "Mouse 4",
                    MouseButton.XButton2 => "Mouse 5",
                    _ => e.ChangedButton.ToString()
                };
                PttKeyDisplay.Text = _pttKey;
                return;
            }

            if (!_isVadMode && !_isPttActive)
            {
                var mouseStr = e.ChangedButton switch
                {
                    MouseButton.Left     => "Mouse Left",
                    MouseButton.Right    => "Mouse Right",
                    MouseButton.Middle   => "Mouse Middle",
                    MouseButton.XButton1 => "Mouse 4",
                    MouseButton.XButton2 => "Mouse 5",
                    _ => e.ChangedButton.ToString()
                };
                if (mouseStr == _pttKey) _isPttActive = true;
            }

            base.OnPreviewMouseDown(e);
        }

        protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
        {
            if (!_isVadMode && _isPttActive)
            {
                var mouseStr = e.ChangedButton switch
                {
                    MouseButton.Left     => "Mouse Left",
                    MouseButton.Right    => "Mouse Right",
                    MouseButton.Middle   => "Mouse Middle",
                    MouseButton.XButton1 => "Mouse 4",
                    MouseButton.XButton2 => "Mouse 5",
                    _ => e.ChangedButton.ToString()
                };
                if (mouseStr == _pttKey) _isPttActive = false;
            }
            base.OnPreviewMouseUp(e);
        }

        private void LeaveVoiceChannel_Click(object sender, RoutedEventArgs e)
        {
            LeaveCurrentVoiceChannel();
        }

        private void AddVoiceChannelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProject == null) return;
            NewVoiceChannelNameInput.Text = "";
            AddVoiceChannelOverlay.Visibility = Visibility.Visible;
            NewVoiceChannelNameInput.Focus();
        }

        private void CancelAddVoiceChannel_Click(object sender, RoutedEventArgs e)
            => AddVoiceChannelOverlay.Visibility = Visibility.Collapsed;

        private void AddVoiceChannelOverlay_MouseDown(object sender, MouseButtonEventArgs e)
            => AddVoiceChannelOverlay.Visibility = Visibility.Collapsed;

        private async void ConfirmAddVoiceChannel_Click(object sender, RoutedEventArgs e)
        {
            var name = NewVoiceChannelNameInput.Text.Trim();
            if (string.IsNullOrEmpty(name) || _selectedProject == null) return;
            if (!_projectVoiceChannels.TryGetValue(_selectedProject, out var voiceChannels)) return;

            var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
            if (proj == null || proj.DbId == 0) return;

            if (voiceChannels.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("A voice channel with this name already exists.", "DevTavern",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var postData = new { Name = name, Type = 2 };
                var content = new StringContent(JsonConvert.SerializeObject(postData), System.Text.Encoding.UTF8, "application/json");
                var resp = await _apiClient.PostAsync($"channels/project/{proj.DbId}", content);
                if (resp.IsSuccessStatusCode)
                {
                    var cJson = JObject.Parse(await resp.Content.ReadAsStringAsync());
                    voiceChannels.Add(new ChannelItem
                    {
                        Id = cJson["id"]?.ToObject<int>() ?? 0,
                        Name = name,
                        Type = 2,
                        VoiceGroupKey = $"{proj.DbId}_{name}"
                    });
                }
            }
            catch { }

            AddVoiceChannelOverlay.Visibility = Visibility.Collapsed;
        }

        private void NewVoiceChannelNameInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { ConfirmAddVoiceChannel_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.Escape) { AddVoiceChannelOverlay.Visibility = Visibility.Collapsed; e.Handled = true; }
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

                // Filter members matching the query by username or display name
                if (_selectedProject != null && _projectMembers.TryGetValue(_selectedProject, out var members))
                {
                    var filtered = members.Where(m => m.Username.ToLower().Contains(query) || m.DisplayName.ToLower().Contains(query)).ToList();
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
        private string? _attachedImagePath = null;
        
        private void AttachImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp",
                Title = "Select an Image to Attach"
            };
            if (dlg.ShowDialog() == true)
            {
                _attachedImagePath = dlg.FileName;
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(_attachedImagePath);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 160;
                bmp.EndInit();
                AttachedImageThumbnail.Source = bmp;
                AttachedImagePreviewBar.Visibility = Visibility.Visible;
            }
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            _attachedImagePath = null;
            AttachedImageThumbnail.Source = null;
            AttachedImagePreviewBar.Visibility = Visibility.Collapsed;
        }

        private string CompressImageToBase64(string imagePath, int maxWidth = 800)
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imagePath);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = maxWidth;
                bmp.EndInit();

                var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                encoder.QualityLevel = 70;
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));

                using var ms = new System.IO.MemoryStream();
                encoder.Save(ms);
                return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
            }
            catch { return ""; }
        }

        private async void SendMessage()
        {
            var text = MessageInput.Text?.Trim() ?? "";
            
            string finalContent = text;
            if (!string.IsNullOrEmpty(_attachedImagePath) && System.IO.File.Exists(_attachedImagePath))
            {
                string base64 = CompressImageToBase64(_attachedImagePath);
                if (!string.IsNullOrEmpty(base64))
                    finalContent += (string.IsNullOrEmpty(finalContent) ? "" : "\n") + $"[IMAGE:{base64}]";
                _attachedImagePath = null;
                AttachedImageThumbnail.Source = null;
                AttachedImagePreviewBar.Visibility = Visibility.Collapsed;
            }

            if (string.IsNullOrEmpty(finalContent) || _selectedProject == null || _selectedChannelId == 0) return;

            // Afisam mesajul local imediat (optimistic UI)
            DateTime sendTime = DateTime.Now;
            var lastReal = Messages.LastOrDefault(m => !m.IsDateSeparator && !m.IsSystemMessage);
            if (lastReal == null || lastReal.MessageDate.Date != sendTime.Date)
                Messages.Add(new ChatMessage { IsDateSeparator = true, DateLabel = FormatDateLabel(sendTime.Date) });

            var msg = ParseMessageContent(new ChatMessage
            {
                Username = _username,
                Initials = _username.Length >= 2 ? _username.Substring(0, 2).ToUpper() : _username.ToUpper(),
                AvatarColor = "#238636",
                UsernameColor = "#238636",
                AvatarUrl = string.IsNullOrEmpty(_avatarUrl) ? null : _avatarUrl,
                Content = finalContent,
                Timestamp = sendTime.ToString("HH:mm"),
                IsSystemMessage = false,
                IsMentioningMe = text.Contains("@" + _username, StringComparison.OrdinalIgnoreCase),
                MessageDate = sendTime,
                IsOwnMessage = true
            });
            Messages.Add(msg);

            await Application.Current.Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToEnd(),
                System.Windows.Threading.DispatcherPriority.Background);

            MessageInput.Text = "";
            MessageInput.Focus();

            try
            {
                // Salvare in DB
                var postData = new { Content = finalContent, UserId = _currentUserId, ChannelId = _selectedChannelId };
                var content = new StringContent(JsonConvert.SerializeObject(postData), System.Text.Encoding.UTF8, "application/json");
                var sendResp = await _apiClient.PostAsync("messages", content);
                if (sendResp.IsSuccessStatusCode)
                {
                    var respJson = JObject.Parse(await sendResp.Content.ReadAsStringAsync());
                    msg.MessageId = respJson["id"]?.ToObject<int>() ?? 0;
                }

                // Trimitere live catre grupul canalului curent (SignalR)
                if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                {
                    await _hubConnection.InvokeAsync("SendLiveMessage", _selectedChannelId.ToString(), _username, _avatarUrl, finalContent);
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

                    if (channel.Type == 2)
                    {
                        SettingsChannelPrefix.Text = "🔊";
                        SettingsChannelSubtitle.Text = "Voice Channel Settings";
                        SettingsDangerZoneSubtitle.Text = "Permanently delete this voice channel";
                    }
                    else
                    {
                        SettingsChannelPrefix.Text = "#";
                        SettingsChannelSubtitle.Text = "Channel Settings";
                        SettingsDangerZoneSubtitle.Text = "Permanently delete this channel and all its messages";
                    }

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

        private async void SaveChannelName_Click(object sender, RoutedEventArgs e)
        {
            if (_editingChannel == null) return;

            var newName = EditChannelNameInput.Text?.Trim().ToLower().Replace(" ", "-");
            if (string.IsNullOrEmpty(newName)) return;

            if (newName == _editingChannel.Name)
            {
                ChannelSettingsOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            if (_editingChannel.Type == 2)
            {
                if (_selectedProject != null && _projectVoiceChannels.TryGetValue(_selectedProject, out var vCh))
                {
                    if (vCh.Any(c => c.Name == newName && c.Id != _editingChannel.Id))
                    {
                        MessageBox.Show("A voice channel with this name already exists.", "DevTavern",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }
            else
            {
                if (_selectedProject != null && _projectChannels.TryGetValue(_selectedProject, out var channels))
                {
                    if (channels.Any(c => c.Name == newName && c.Id != _editingChannel.Id))
                    {
                        MessageBox.Show("A channel with this name already exists.", "DevTavern",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }

            // Persist to server
            try
            {
                var body = new StringContent(
                    JsonConvert.SerializeObject(new { Name = newName }),
                    System.Text.Encoding.UTF8, "application/json");
                var resp = await _apiClient.PutAsync($"channels/{_editingChannel.Id}/rename", body);
                if (!resp.IsSuccessStatusCode)
                {
                    MessageBox.Show("Failed to rename channel. Please try again.", "DevTavern",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            catch
            {
                MessageBox.Show("Could not reach the server. Please check your connection.", "DevTavern",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Update local state — INotifyPropertyChanged on Name updates the list in-place
            _editingChannel.Name = newName;

            if (_editingChannel.Type == 2)
            {
                if (_currentVoiceChannel?.Id == _editingChannel.Id)
                    VoiceConnectedChannelName.Text = $"#{newName}";
            }
            else
            {
                if (_selectedChannelId == _editingChannel.Id)
                {
                    ChatTitle.Text = newName;
                    ChatSubtitle.Text = $"{_selectedProject} · #{newName}";
                }
            }

            SettingsChannelTitle.Text = newName;
            ChannelSettingsOverlay.Visibility = Visibility.Collapsed;
        }

        // Sterge canalul — arata dialog custom de confirmare
        private void DeleteChannel_Click(object sender, RoutedEventArgs e)
        {
            if (_editingChannel == null || _selectedProject == null) return;

            DeleteConfirmMessage.Text = _editingChannel.Type == 2
                ? $"Are you sure you want to delete voice channel \"{_editingChannel.Name}\"?\nThis action cannot be undone."
                : $"Are you sure you want to delete #{_editingChannel.Name}?\nThis action cannot be undone and all messages will be lost.";
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

                    if (_editingChannel.Type == 2)
                    {
                        if (_projectVoiceChannels.TryGetValue(_selectedProject, out var vChannels))
                            vChannels.Remove(_editingChannel);
                        if (_currentVoiceChannel?.Id == deletedId)
                            LeaveCurrentVoiceChannel();
                    }
                    else
                    {
                        if (_projectChannels.TryGetValue(_selectedProject, out var channels))
                        {
                            channels.Remove(_editingChannel);
                            if (_selectedChannelId == deletedId)
                            {
                                if (channels.Count > 0)
                                    ChannelList.SelectedIndex = 0;
                                else
                                {
                                    Messages.Clear();
                                    ChatTitle.Text = "No channels";
                                    ChatSubtitle.Text = "Create a channel to start chatting";
                                }
                            }
                        }
                        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                            await _hubConnection.InvokeAsync("NotifyChannelDeleted", proj.DbId.ToString(), deletedId);
                    }
                }
            }
            catch { }

            ChannelSettingsOverlay.Visibility = Visibility.Collapsed;
            _editingChannel = null;
        }

        // ========== Message Edit / Delete ==========

        private void EditMessage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuItem)?.Tag is not ChatMessage msg || !msg.IsOwnMessage || msg.IsDeleted) return;
            msg.EditContent = msg.Content;
            msg.IsEditing = true;
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ChatMessage msg)
                msg.IsEditing = false;
        }

        private async void SaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not ChatMessage msg) return;
            await DoSaveEditAsync(msg);
        }

        private async void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb || tb.Tag is not ChatMessage msg) return;
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                await DoSaveEditAsync(msg);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                msg.IsEditing = false;
                e.Handled = true;
            }
        }

        private async Task DoSaveEditAsync(ChatMessage msg)
        {
            var newContent = msg.EditContent?.Trim();
            if (string.IsNullOrWhiteSpace(newContent)) { msg.IsEditing = false; return; }
            if (newContent == msg.Content) { msg.IsEditing = false; return; }

            if (msg.MessageId > 0)
            {
                try
                {
                    var body = new StringContent(
                        JsonConvert.SerializeObject(new { Content = newContent }),
                        System.Text.Encoding.UTF8, "application/json");
                    var resp = await _apiClient.PutAsync($"messages/{msg.MessageId}", body);
                    if (!resp.IsSuccessStatusCode) { msg.IsEditing = false; return; }

                    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                        await _hubConnection.InvokeAsync("EditMessageBroadcast", _selectedChannelId.ToString(), msg.MessageId, newContent);
                }
                catch { msg.IsEditing = false; return; }
            }

            msg.Content = newContent;
            msg.IsEdited = true;
            ParseMessageContent(msg);
            msg.IsEditing = false;
        }

        private async void DeleteMessage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuItem)?.Tag is not ChatMessage msg || !msg.IsOwnMessage) return;
            if (msg.MessageId == 0) { Messages.Remove(msg); return; }
            try
            {
                var resp = await _apiClient.DeleteAsync($"messages/{msg.MessageId}");
                if (!resp.IsSuccessStatusCode) return;
                if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                    await _hubConnection.InvokeAsync("DeleteMessageBroadcast", _selectedChannelId.ToString(), msg.MessageId);
            }
            catch { }
        }

        // ========== Logout ==========

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to log out?", "Logout", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

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

        private string? _targetMemberForRoles = null;

        private void ServerSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProject == null) return;
            _settingsTargetProject = _projects.FirstOrDefault(p => p.name == _selectedProject);
            if (_settingsTargetProject == null) return;

            ServerSettingsProjectName.Text = _settingsTargetProject.name;
            ServerSettingsIconInitials.Text = _settingsTargetProject.IconLetters;

            if (_settingsTargetProject.HasCustomImage && _settingsTargetProject.CustomImageSource != null)
            {
                ServerSettingsIconBrush.ImageSource = _settingsTargetProject.CustomImageSource;
                ServerSettingsIconImageEllipse.Visibility = Visibility.Visible;
                ServerSettingsRemoveBtn.Visibility = Visibility.Visible;
            }
            else
            {
                ServerSettingsIconBrush.ImageSource = null;
                ServerSettingsIconImageEllipse.Visibility = Visibility.Collapsed;
                ServerSettingsRemoveBtn.Visibility = Visibility.Collapsed;
            }

            ServerSettingsOverlay.Visibility = Visibility.Visible;
        }

        private void ServerSettingsOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
            => ServerSettingsOverlay.Visibility = Visibility.Collapsed;

        private void CloseServerSettings_Click(object sender, RoutedEventArgs e)
            => ServerSettingsOverlay.Visibility = Visibility.Collapsed;

        private void ChangeServerIcon_Click(object sender, RoutedEventArgs e)
        {
            var fileDlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Server Icon",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
                Multiselect = false
            };
            if (fileDlg.ShowDialog() != true || _settingsTargetProject == null) return;

            OpenCropOverlay(fileDlg.FileName);
        }

        private async void RemoveServerIcon_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsTargetProject == null) return;
            _settingsTargetProject.CustomImagePath = null;
            _settingsTargetProject.ServerImageUrl = null;
            SaveServerIcons();

            if (_settingsTargetProject.DbId > 0)
            {
                try
                {
                    var body = new StringContent(
                        JsonConvert.SerializeObject(new { ImageUrl = (string?)null }),
                        System.Text.Encoding.UTF8, "application/json");
                    await _apiClient.PutAsync($"projects/{_settingsTargetProject.DbId}/image", body);
                }
                catch { }
            }

            ServerSettingsIconBrush.ImageSource = null;
            ServerSettingsIconImageEllipse.Visibility = Visibility.Collapsed;
            ServerSettingsRemoveBtn.Visibility = Visibility.Collapsed;
        }

        // ========== Crop Image Overlay ==========

        private void OpenCropOverlay(string filePath)
        {
            _cropBitmap = new BitmapImage();
            _cropBitmap.BeginInit();
            _cropBitmap.UriSource = new Uri(filePath);
            _cropBitmap.CacheOption = BitmapCacheOption.OnLoad;
            _cropBitmap.EndInit();

            CropImageElement.Source = _cropBitmap;
            _cropOrigWidth  = _cropBitmap.PixelWidth;
            _cropOrigHeight = _cropBitmap.PixelHeight;

            double minDim = Math.Min(_cropOrigWidth, _cropOrigHeight);
            _cropScale = (CropCircleRadius * 2.0) / minDim;
            _cropTx = 0;
            _cropTy = 0;

            UpdateCropTransform();
            CropImageOverlay.Visibility = Visibility.Visible;
        }

        private void UpdateCropTransform()
        {
            double imgW = _cropOrigWidth  * _cropScale;
            double imgH = _cropOrigHeight * _cropScale;
            System.Windows.Controls.Canvas.SetLeft(CropImageElement, (CropViewportSize - imgW) / 2.0 + _cropTx);
            System.Windows.Controls.Canvas.SetTop(CropImageElement,  (CropViewportSize - imgH) / 2.0 + _cropTy);
            CropImageElement.Width  = imgW;
            CropImageElement.Height = imgH;
        }

        private void ClampCropTranslation()
        {
            double imgW = _cropOrigWidth  * _cropScale;
            double imgH = _cropOrigHeight * _cropScale;
            double imgL = (CropViewportSize - imgW) / 2 + _cropTx;
            double imgT = (CropViewportSize - imgH) / 2 + _cropTy;

            double cropL = CropViewportSize / 2 - CropCircleRadius;
            double cropR = CropViewportSize / 2 + CropCircleRadius;
            double cropT = CropViewportSize / 2 - CropCircleRadius;
            double cropB = CropViewportSize / 2 + CropCircleRadius;

            if (imgL > cropL) _cropTx -= imgL - cropL;
            if (imgL + imgW < cropR) _cropTx += cropR - (imgL + imgW);
            if (imgT > cropT) _cropTy -= imgT - cropT;
            if (imgT + imgH < cropB) _cropTy += cropB - (imgT + imgH);
        }

        private void CropViewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            _cropIsDragging = true;
            _cropLastMouse  = e.GetPosition(CropViewportBorder);
            CropViewportBorder.CaptureMouse();
            e.Handled = true;
        }

        private void CropViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_cropIsDragging) return;
            Point pos = e.GetPosition(CropViewportBorder);
            _cropTx += pos.X - _cropLastMouse.X;
            _cropTy += pos.Y - _cropLastMouse.Y;
            _cropLastMouse = pos;
            ClampCropTranslation();
            UpdateCropTransform();
        }

        private void CropViewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _cropIsDragging = false;
            CropViewportBorder.ReleaseMouseCapture();
        }

        private void CropViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            double minDim   = Math.Min(_cropOrigWidth, _cropOrigHeight);
            double minScale = (CropCircleRadius * 2.0) / minDim;
            _cropScale = Math.Clamp(_cropScale * factor, minScale, minScale * 15.0);
            ClampCropTranslation();
            UpdateCropTransform();
        }

        private void CropOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
            => CropImageOverlay.Visibility = Visibility.Collapsed;

        private void CancelCrop_Click(object sender, RoutedEventArgs e)
            => CropImageOverlay.Visibility = Visibility.Collapsed;

        private async void ApplyCrop_Click(object sender, RoutedEventArgs e)
        {
            if (_cropBitmap == null || _settingsTargetProject == null) return;

            const int    outputSize    = 300;
            const double cornerRadius  = 95.0; // 300 * (14/44) — matches the selected icon shape

            var rtb = new RenderTargetBitmap(outputSize, outputSize, 96, 96, PixelFormats.Pbgra32);
            var dv  = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, outputSize, outputSize), cornerRadius, cornerRadius));

                double imgW = _cropOrigWidth  * _cropScale;
                double imgH = _cropOrigHeight * _cropScale;
                double relLeft = (CropViewportSize - imgW) / 2.0 + _cropTx - (CropViewportSize / 2.0 - CropCircleRadius);
                double relTop  = (CropViewportSize - imgH) / 2.0 + _cropTy - (CropViewportSize / 2.0 - CropCircleRadius);

                dc.DrawImage(_cropBitmap, new Rect(relLeft, relTop, imgW, imgH));
                dc.Pop();
            }
            rtb.Render(dv);

            // Save locally as cache
            string iconsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DevTavern", "ServerIcons");
            Directory.CreateDirectory(iconsDir);
            string outPath = Path.Combine(iconsDir, $"{_settingsTargetProject.id}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var stream = File.Create(outPath))
                encoder.Save(stream);

            _settingsTargetProject.CustomImagePath = outPath;
            SaveServerIcons();

            // Upload to server as base64 data URL
            if (_settingsTargetProject.DbId > 0)
            {
                try
                {
                    var bytes = File.ReadAllBytes(outPath);
                    var base64 = Convert.ToBase64String(bytes);
                    var dataUrl = $"data:image/png;base64,{base64}";
                    var body = new StringContent(
                        JsonConvert.SerializeObject(new { ImageUrl = dataUrl }),
                        System.Text.Encoding.UTF8, "application/json");
                    var resp = await _apiClient.PutAsync($"projects/{_settingsTargetProject.DbId}/image", body);
                    if (resp.IsSuccessStatusCode)
                        _settingsTargetProject.ServerImageUrl = dataUrl;
                }
                catch { }
            }

            ServerSettingsIconBrush.ImageSource = _settingsTargetProject.CustomImageSource;
            ServerSettingsIconImageEllipse.Visibility = Visibility.Visible;
            ServerSettingsRemoveBtn.Visibility = Visibility.Visible;

            CropImageOverlay.Visibility = Visibility.Collapsed;
        }

        private void MemberAssignRole_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is MemberItem member)
            {
                OpenRoleSelectionFor(member.Username);
            }
        }

        private void OpenRoleSelectionFor(string targetUsername)
        {
            _targetMemberForRoles = targetUsername;
            
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
                var me = members.FirstOrDefault(m => m.Username == targetUsername);
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

        private void ProfileSettings_Click(object sender, RoutedEventArgs e)
        {
            PopulateAudioDevices();
            VoiceSettingsOverlay.Visibility = Visibility.Visible;
            
            // Activate Voice Tab
            VoiceTabContent.Visibility = Visibility.Visible;
            AppTabContent.Visibility = Visibility.Collapsed;
            KeybindsTabContent.Visibility = Visibility.Collapsed;
            SetTabActive(SettingsTabVoiceBtn, true);
            SetTabActive(SettingsTabAppBtn, false);
            SetTabActive(SettingsTabKeybindsBtn, false);
        }

        private void PopupSettings_Click(object sender, MouseButtonEventArgs e)
        {
            UserProfilePopup.IsOpen = false;
            PopulateAudioDevices();
            VoiceSettingsOverlay.Visibility = Visibility.Visible;
            
            // Activate App Tab (General Settings)
            VoiceTabContent.Visibility = Visibility.Collapsed;
            AppTabContent.Visibility = Visibility.Visible;
            KeybindsTabContent.Visibility = Visibility.Collapsed;
            SetTabActive(SettingsTabVoiceBtn, false);
            SetTabActive(SettingsTabAppBtn, true);
            SetTabActive(SettingsTabKeybindsBtn, false);
        }

        private async void SaveRoles_Click(object sender, RoutedEventArgs e)
        {
            RoleSelectionOverlay.Visibility = Visibility.Collapsed;

            if (_selectedProject != null && _projectMembers.TryGetValue(_selectedProject, out var members) && _targetMemberForRoles != null)
            {
                var me = members.FirstOrDefault(m => m.Username == _targetMemberForRoles);
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

                                var member = new MemberItem
                                {
                                    Username = memberUsername,
                                    Initials = memberUsername.Length >= 2 ? memberUsername.Substring(0, 2).ToUpper() : memberUsername.ToUpper(),
                                    Role = role,
                                    IsOnline = memberUsername == _username,
                                    AvatarUrl = memberAvatar
                                };
                                _projectMembers[project.name].Add(member);
                                ResolveDisplayNameAsync(memberUsername, name => member.DisplayName = name);
                            }
                        }
                    }
                    catch { }

                    if (_projectMembers[project.name].Count == 0)
                    {
                        var member = new MemberItem
                        {
                            Username = _username,
                            Initials = UserInitials.Text,
                            Role = "Owner",
                            IsOnline = true,
                            AvatarUrl = _avatarUrl
                        };
                        _projectMembers[project.name].Add(member);
                        ResolveDisplayNameAsync(_username, name => member.DisplayName = name);
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
        private static string FormatDateLabel(DateTime date)
        {
            DateTime today = DateTime.Today;
            if (date == today) return "Today";
            if (date == today.AddDays(-1)) return "Yesterday";
            return date.ToString("dddd, MMMM d");
        }

        private ChatMessage ParseMessageContent(ChatMessage msg)
        {
            if (!msg.IsSystemMessage && !msg.IsDateSeparator && !string.IsNullOrEmpty(msg.Username))
            {
                ResolveDisplayNameAsync(msg.Username, name => msg.DisplayName = name);
            }

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

            // Handle [IMAGE:...]
            while (msg.DisplayContent.Contains("[IMAGE:"))
            {
                int startIndex = msg.DisplayContent.IndexOf("[IMAGE:");
                int endIndex = msg.DisplayContent.IndexOf("]", startIndex);
                if (endIndex > startIndex)
                {
                    string imgData = msg.DisplayContent.Substring(startIndex + 7, endIndex - startIndex - 7);
                    msg.ImageUrl = imgData; // Sets ImageUrl and triggers HasImage automatically
                    
                    string before = msg.DisplayContent.Substring(0, startIndex).Trim();
                    string after = msg.DisplayContent.Substring(endIndex + 1).Trim();
                    msg.DisplayContent = (before + "\n" + after).Trim();
                }
                else break;
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
                            int newCount = arr.Count(m => !(m["isDeleted"]?.ToObject<bool>() ?? false));

                            if (!_lastSeenMessageCount.TryGetValue(channel.Id, out int lastSeen))
                            {
                                // Prima oara cand vedem canalul — stabilim baseline, fara badge
                                _lastSeenMessageCount[channel.Id] = newCount;
                                continue;
                            }

                            if (newCount > lastSeen)
                            {
                                int mentions = 0;
                                var visibleArr = arr.Where(m => !(m["isDeleted"]?.ToObject<bool>() ?? false)).ToList();
                                for (int i = lastSeen; i < visibleArr.Count; i++)
                                {
                                    string content = visibleArr[i]["content"]?.ToString() ?? "";
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
    public class ChatMessage : INotifyPropertyChanged
    {
        public string Username { get; set; } = "";
        
        private string _displayName = "";
        public string DisplayName
        {
            get => string.IsNullOrEmpty(_displayName) ? Username : _displayName;
            set { _displayName = value; OnPropertyChanged(); }
        }

        public string Initials { get; set; } = "";
        public string AvatarColor { get; set; } = "#8B949E";
        public string UsernameColor { get; set; } = "#E6EDF3";
        public string? AvatarUrl { get; set; } = null;
        public string Timestamp { get; set; } = "";
        public bool IsSystemMessage { get; set; } = false;
        public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);
        public bool IsMentioningMe { get; set; } = false;
        public bool IsOwnMessage { get; set; } = false;
        public int MessageId { get; set; } = 0;

        private string _content = "";
        public string Content
        {
            get => _content;
            set { _content = value; OnPropertyChanged(); }
        }

        private string _displayContent = "";
        public string DisplayContent
        {
            get => _displayContent;
            set { _displayContent = value; OnPropertyChanged(); }
        }

        private bool _isEdited;
        public bool IsEdited
        {
            get => _isEdited;
            set { _isEdited = value; OnPropertyChanged(); }
        }

        private bool _isDeleted;
        public bool IsDeleted
        {
            get => _isDeleted;
            set { _isDeleted = value; OnPropertyChanged(); OnPropertyChanged(nameof(ContentColor)); }
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set { _isEditing = value; OnPropertyChanged(); }
        }

        public string EditContent { get; set; } = "";

        public string ContentColor => IsDeleted ? "#6E7681" : "#E6EDF3";

        public bool HasCodeReference { get; set; } = false;
        public string CodeFilePath { get; set; } = "";
        public string CodeBranch { get; set; } = "";
        public string CodeSelectedText { get; set; } = "";
        public string CodeFileExtension => string.IsNullOrEmpty(CodeFilePath)
            ? ""
            : System.IO.Path.GetExtension(CodeFilePath);

        private string _imageUrl = "";
        public string ImageUrl
        {
            get => _imageUrl;
            set
            {
                if (_imageUrl != value)
                {
                    _imageUrl = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasImage));
                    UpdateImageMedia();
                }
            }
        }
        
        public bool HasImage => !string.IsNullOrEmpty(ImageUrl);

        private System.Windows.Media.ImageSource? _imageMedia;
        public System.Windows.Media.ImageSource? ImageMedia
        {
            get => _imageMedia;
            private set
            {
                _imageMedia = value;
                OnPropertyChanged();
            }
        }

        private void UpdateImageMedia()
        {
            if (string.IsNullOrEmpty(_imageUrl))
            {
                ImageMedia = null;
                return;
            }

            try
            {
                string base64Data = _imageUrl;
                var match = System.Text.RegularExpressions.Regex.Match(_imageUrl, @"data:image/(?<type>.+?);base64,(?<data>.+)");
                if (match.Success)
                {
                    base64Data = match.Groups["data"].Value;
                }
                
                byte[] imageBytes = Convert.FromBase64String(base64Data);
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                using (var ms = new System.IO.MemoryStream(imageBytes))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                }
                bitmap.Freeze(); // Freeze to make it cross-thread accessible
                ImageMedia = bitmap;
            }
            catch
            {
                ImageMedia = null;
            }
        }

        public bool IsDateSeparator { get; set; } = false;
        public string DateLabel { get; set; } = "";
        public DateTime MessageDate { get; set; } = DateTime.MinValue;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class ChannelItem : INotifyPropertyChanged
    {
        public int Id { get; set; }

        private string _name = "";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public int Type { get; set; } = 0;
        public string VoiceGroupKey { get; set; } = "";

        private int _unreadMentionCount;
        public int UnreadMentionCount
        {
            get => _unreadMentionCount;
            set { _unreadMentionCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMentions)); }
        }
        public bool HasMentions => _unreadMentionCount > 0;

        private bool _hasUnreadMessages;
        public bool HasUnreadMessages
        {
            get => _hasUnreadMessages;
            set { _hasUnreadMessages = value; OnPropertyChanged(); }
        }

        private bool _isJoined;
        public bool IsJoined
        {
            get => _isJoined;
            set { _isJoined = value; OnPropertyChanged(); }
        }

        public ObservableCollection<VoiceMember> VoiceMembers { get; } = [];

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class VoiceMember : INotifyPropertyChanged
    {
        public string Username { get; set; } = "";

        private string _displayName = "";
        public string DisplayName
        {
            get => string.IsNullOrEmpty(_displayName) ? Username : _displayName;
            set { _displayName = value; OnPropertyChanged(); }
        }

        private bool _isMuted;
        public bool IsMuted
        {
            get => _isMuted;
            set { _isMuted = value; OnPropertyChanged(); }
        }

        private bool _isDeafened;
        public bool IsDeafened
        {
            get => _isDeafened;
            set { _isDeafened = value; OnPropertyChanged(); }
        }

        private bool _isSpeaking;
        public bool IsSpeaking
        {
            get => _isSpeaking;
            set { _isSpeaking = value; OnPropertyChanged(); }
        }

        public string? AvatarUrl { get; set; } = null;
        public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class MemberItem : INotifyPropertyChanged
    {
        public string Username { get; set; } = "";

        private string _displayName = "";
        public string DisplayName
        {
            get => string.IsNullOrEmpty(_displayName) ? Username : _displayName;
            set { _displayName = value; OnPropertyChanged(); }
        }

        public string Initials { get; set; } = "";
        public string Role { get; set; } = "Member";
        public bool IsOnline { get; set; } = false;
        public string? AvatarUrl { get; set; } = null;
        public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);
        public bool IsHeader { get; set; } = false;
        
        public List<string> DevRoles { get; set; } = new List<string>();
        public string RoleBadges => DevRoles != null && DevRoles.Count > 0 ? string.Join(" · ", DevRoles) : "";
        public bool HasDevRoles => DevRoles != null && DevRoles.Count > 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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

    public static class RichTextBoxHelper
    {
        public static readonly DependencyProperty FormattedTextProperty =
            DependencyProperty.RegisterAttached("FormattedText", typeof(string), typeof(RichTextBoxHelper),
                new PropertyMetadata(string.Empty, (d, e) => RebuildDocument(d as System.Windows.Controls.RichTextBox)));

        public static readonly DependencyProperty FormattedTextIsEditedProperty =
            DependencyProperty.RegisterAttached("FormattedTextIsEdited", typeof(bool), typeof(RichTextBoxHelper),
                new PropertyMetadata(false, (d, e) => RebuildDocument(d as System.Windows.Controls.RichTextBox)));

        public static void SetFormattedText(DependencyObject obj, string value) => obj.SetValue(FormattedTextProperty, value);
        public static string GetFormattedText(DependencyObject obj) => (string)obj.GetValue(FormattedTextProperty);
        public static void SetFormattedTextIsEdited(DependencyObject obj, bool value) => obj.SetValue(FormattedTextIsEditedProperty, value);
        public static bool GetFormattedTextIsEdited(DependencyObject obj) => (bool)obj.GetValue(FormattedTextIsEditedProperty);

        private static void RebuildDocument(System.Windows.Controls.RichTextBox? rtb)
        {
            if (rtb == null) return;
            var text = (string)rtb.GetValue(FormattedTextProperty) ?? string.Empty;
            var isEdited = (bool)rtb.GetValue(FormattedTextIsEditedProperty);

            var doc = new System.Windows.Documents.FlowDocument
            {
                PagePadding = new Thickness(0),
                Background = Brushes.Transparent
            };
            
            var p = new System.Windows.Documents.Paragraph { Margin = new Thickness(0) };

            if (!string.IsNullOrEmpty(text))
            {
                var regex = new System.Text.RegularExpressions.Regex(@"(@[a-zA-Z0-9_\-]+)|(https?:\/\/[^\s]+)");
                var parts = regex.Split(text);
                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;
                    if (part.StartsWith("@") && part.Length > 1)
                    {
                        p.Inlines.Add(new System.Windows.Documents.Run(part)
                        {
                            Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E3B341")),
                            FontWeight = FontWeights.Bold
                        });
                    }
                    else if (part.StartsWith("http://") || part.StartsWith("https://"))
                    {
                        var hl = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(part))
                        {
                            NavigateUri = new Uri(part),
                            Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#58A6FF")),
                            TextDecorations = TextDecorations.Underline
                        };
                        hl.RequestNavigate += (s, e) =>
                        {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                            e.Handled = true;
                        };
                        p.Inlines.Add(hl);
                    }
                    else
                    {
                        p.Inlines.Add(new System.Windows.Documents.Run(part));
                    }
                }
            }

            if (isEdited)
            {
                p.Inlines.Add(new System.Windows.Documents.Run(" (edited)")
                {
                    Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6E7681")),
                    FontStyle = FontStyles.Italic,
                    FontSize = 11
                });
            }
            
            doc.Blocks.Add(p);
            rtb.Document = doc;
        }
    }

    public static class AvalonEditHelper
    {
        public static readonly DependencyProperty BindableTextProperty =
            DependencyProperty.RegisterAttached("BindableText", typeof(string), typeof(AvalonEditHelper),
                new PropertyMetadata(null, OnBindableTextChanged));

        public static readonly DependencyProperty FileExtensionProperty =
            DependencyProperty.RegisterAttached("FileExtension", typeof(string), typeof(AvalonEditHelper),
                new PropertyMetadata(null, OnFileExtensionChanged));

        public static string? GetBindableText(DependencyObject obj) => (string?)obj.GetValue(BindableTextProperty);
        public static void SetBindableText(DependencyObject obj, string? value) => obj.SetValue(BindableTextProperty, value);

        public static string? GetFileExtension(DependencyObject obj) => (string?)obj.GetValue(FileExtensionProperty);
        public static void SetFileExtension(DependencyObject obj, string? value) => obj.SetValue(FileExtensionProperty, value);

        private static void OnBindableTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ICSharpCode.AvalonEdit.TextEditor editor)
                editor.Text = (string?)e.NewValue ?? "";
        }

        private static void OnFileExtensionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ICSharpCode.AvalonEdit.TextEditor editor) return;
            string ext = (string?)e.NewValue ?? "";
            if (!string.IsNullOrEmpty(ext))
                editor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinitionByExtension(ext);
            editor.TextArea.Caret.CaretBrush = Brushes.Transparent;
            if (!editor.TextArea.TextView.LineTransformers.OfType<DarkModeColorizer>().Any())
                editor.TextArea.TextView.LineTransformers.Add(new DarkModeColorizer());
        }
    }

    public class KeybindEntry : INotifyPropertyChanged
    {
        private string _key = "Click to bind";
        private string _action = "Toggle Mute";

        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(); }
        }

        public string Action
        {
            get => _action;
            set { _action = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public class ActivityItem : INotifyPropertyChanged
    {
        private string _icon = "";
        public string Icon { get => _icon; set { _icon = value; OnPropertyChanged(); } }

        private string _actionTitle = "";
        public string ActionTitle { get => _actionTitle; set { _actionTitle = value; OnPropertyChanged(); } }

        private string _projectName = "";
        public string ProjectName { get => _projectName; set { _projectName = value; OnPropertyChanged(); } }

        private string _actionDetails = "";
        public string ActionDetails { get => _actionDetails; set { _actionDetails = value; OnPropertyChanged(); } }

        private string _timeAgo = "";
        public string TimeAgo { get => _timeAgo; set { _timeAgo = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}