using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NAudio.Wave;

namespace DevTavern.Client
{
    public partial class MainWindow : Window
    {
        // ── Core identity ──────────────────────────────────────────────
        private readonly int _currentUserId;
        private readonly HttpClient _apiClient;
        private HubConnection? _hubConnection;

        private readonly string _accessToken;
        private readonly List<RepoItem> _projects;
        private readonly string _username;
        private readonly string _avatarUrl;
        private string _displayName;

        // ── UI state ──────────────────────────────────────────────────
        private string? _selectedProject;
        private int _selectedChannelId;
        private bool _membersPanelVisible = false;
        private ChannelItem? _editingChannel = null;
        private double _channelPanelWidth = 240;

        // ── Channels ──────────────────────────────────────────────────
        private readonly Dictionary<int, string> _channelDrafts = new();
        private readonly Dictionary<string, ObservableCollection<ChannelItem>> _projectChannels = new();
        private readonly Dictionary<string, ObservableCollection<ChannelItem>> _projectVoiceChannels = new();

        // ── Voice ─────────────────────────────────────────────────────
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

        // ── NAudio ────────────────────────────────────────────────────
        private WaveInEvent? _waveIn;
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _waveProvider;
        private readonly Dictionary<string, CancellationTokenSource> _speakingTimers = new();

        // ── Members ───────────────────────────────────────────────────
        private readonly Dictionary<string, ObservableCollection<MemberItem>> _projectMembers = new();
        private readonly HashSet<string> _globalOnlineUsers = new();

        // ── Notifications ─────────────────────────────────────────────
        private readonly Dictionary<int, string> _channelToProject = new();
        private readonly Dictionary<int, int> _lastSeenMessageCount = new();
        private System.Windows.Threading.DispatcherTimer? _notificationTimer;
        private bool _isPolling = false;

        // ── Messages ──────────────────────────────────────────────────
        public ObservableCollection<ChatMessage> Messages { get; set; } = new ObservableCollection<ChatMessage>();

        // ── Home ──────────────────────────────────────────────────────
        public ObservableCollection<ActivityItem> HomeActivities { get; set; } = new ObservableCollection<ActivityItem>();

        // ── Sound ─────────────────────────────────────────────────────
        private AppSoundSettings _soundSettings = new();
        private string _soundPackFolder = "Sounds_Default";

        // ── Settings file paths ───────────────────────────────────────
        private static readonly string _voiceSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevTavern", "voice_settings.json");
        private static readonly string _soundSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevTavern", "sound_settings.json");
        private static readonly string _serverIconsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevTavern", "server_icons.json");
        private static readonly string _keybindsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevTavern", "keybinds.json");

        // ── Server settings / crop overlay ───────────────────────────
        private RepoItem? _settingsTargetProject = null;
        private const double CropViewportSize = 360.0;
        private const double CropCircleRadius  = 150.0;
        private BitmapImage? _cropBitmap;
        private double _cropOrigWidth, _cropOrigHeight;
        private double _cropScale;
        private double _cropTx, _cropTy;
        private System.Windows.Point _cropLastMouse;
        private bool _cropIsDragging = false;

        // ── Keybinds ──────────────────────────────────────────────────
        public static readonly string[] KeybindActions = { "Push to Mute", "Push to Deafen", "Toggle Mute", "Toggle Deafen" };
        public ObservableCollection<KeybindEntry> Keybinds { get; } = new();
        private KeybindEntry? _capturingKeybindEntry = null;
        private string _capturingKeybindPrevKey = "";
        private bool _pushMuteActive = false;
        private bool _pushDeafenActive = false;

        // ─────────────────────────────────────────────────────────────
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

            foreach (var project in _projects)
                project.IconLetters = GenerateIconLetters(project.name);

            CodeViewerContent.TextArea.Caret.CaretBrush = System.Windows.Media.Brushes.Transparent;
            CodeViewerContent.TextArea.TextView.LineTransformers.Add(new DarkModeColorizer());

            ProjectList.ItemsSource = _projects;
            MessagesList.ItemsSource = Messages;

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

            if (!string.IsNullOrEmpty(_avatarUrl))
            {
                try
                {
                    var bitmap = new BitmapImage(new Uri(_avatarUrl));
                    UserAvatarImage.Source = bitmap;
                    UserAvatarImage.Visibility = Visibility.Visible;
                    UserInitials.Visibility = Visibility.Collapsed;
                }
                catch { }
            }

            MessageInput.Text = "";
            HomeActivityFeed.ItemsSource = HomeActivities;
            ShowHomeView();
        }

        public async Task InitializeAsync()
        {
            _fullNameCache[_username] = _displayName;

            var savedIcons = LoadServerIcons();
            foreach (var p in _projects)
                if (savedIcons.TryGetValue(p.id, out var iconPath))
                    p.CustomImagePath = iconPath;

            await SetupSignalRAsync();

            // Sync projects, channels, and members with backend
            foreach (var project in _projects)
            {
                try
                {
                    var pData = new { GitHubRepoId = project.id, Name = project.name };
                    var pContent = new System.Net.Http.StringContent(JsonConvert.SerializeObject(pData), System.Text.Encoding.UTF8, "application/json");
                    var pResp = await _apiClient.PostAsync("projects", pContent);
                    if (pResp.IsSuccessStatusCode)
                    {
                        var pJson = JObject.Parse(await pResp.Content.ReadAsStringAsync());
                        project.DbId = pJson["id"]?.ToObject<int>() ?? 0;
                        var serverImageUrl = pJson["imageUrl"]?.ToString();
                        if (!string.IsNullOrEmpty(serverImageUrl))
                            project.ServerImageUrl = serverImageUrl;

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
                                if (item.Type == 2) voiceChannels.Add(item);
                                else textChannels.Add(item);
                            }
                            _projectChannels[project.name] = textChannels;
                            foreach (var vc in voiceChannels)
                                vc.VoiceGroupKey = $"{project.DbId}_{vc.Name}";
                            _projectVoiceChannels[project.name] = voiceChannels;
                        }
                    }

                    _projectMembers[project.name] = new ObservableCollection<MemberItem>();
                    try
                    {
                        using var ghClient = new System.Net.Http.HttpClient();
                        ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                        ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                        var collabResp = await ghClient.GetAsync($"https://api.github.com/repos/{project.fullName}/collaborators");
                        if (!collabResp.IsSuccessStatusCode)
                            collabResp = await ghClient.GetAsync($"https://api.github.com/repos/{project.fullName}/contributors");

                        if (collabResp.IsSuccessStatusCode)
                        {
                            var collabJson = JArray.Parse(await collabResp.Content.ReadAsStringAsync());
                            foreach (var collab in collabJson)
                            {
                                string memberUsername = collab["login"]?.ToString() ?? "";
                                string memberAvatar = collab["avatar_url"]?.ToString() ?? "";
                                if (string.IsNullOrEmpty(memberUsername)) continue;

                                string role = memberUsername == _username ? "You" : "Collaborator";
                                var permissions = collab["permissions"];
                                if (permissions != null && permissions["admin"]?.ToObject<bool>() == true)
                                    role = memberUsername == _username ? "Owner (You)" : "Owner";

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
                                    member.DevRoles = devRolesStr.Split(new[] { ", ", "," }, StringSplitOptions.RemoveEmptyEntries).ToList();
                            }
                        }
                    }
                    catch { }

                    _ = PrefetchFullNamesAsync([.. _projectMembers[project.name].Select(m => m.Username)]);
                }
                catch { }
            }

            foreach (var proj in _projects)
                if (_projectChannels.TryGetValue(proj.name, out var chans))
                    foreach (var ch in chans)
                        _channelToProject[ch.Id] = proj.name;

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
                using var ghClient = new System.Net.Http.HttpClient();
                ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                var resp = await ghClient.GetAsync($"https://api.github.com/users/{_username}/events/public");
                if (!resp.IsSuccessStatusCode) return;

                var eventsJson = JArray.Parse(await resp.Content.ReadAsStringAsync());
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
                        var commits = ev["payload"]?["commits"] as JArray;
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
                    else { continue; }

                    Application.Current.Dispatcher.Invoke(() => HomeActivities.Add(item));
                    count++;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (HomeActivities.Count == 0)
                        HomeActivities.Add(new ActivityItem { Icon = "📭", ActionTitle = "No recent activity found", ProjectName = "Your GitHub", ActionDetails = " · Go make some commits!" });
                });
            }
            catch { }
        }

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
                return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}";
            return name.Length >= 2 ? name.Substring(0, 2).ToUpper() : name.ToUpper();
        }
    }
}
