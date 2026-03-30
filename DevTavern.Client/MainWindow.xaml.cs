using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DevTavern.Client
{
    public partial class MainWindow : Window
    {
        private readonly string _accessToken;
        private readonly List<RepoItem> _projects;
        private readonly string _username;
        private readonly string _avatarUrl;
        private string? _selectedProject;
        private bool _membersPanelVisible = false;

        // Channels per project: projectName -> list of channels
        private readonly Dictionary<string, ObservableCollection<ChannelItem>> _projectChannels = new();

        // Members per project (stub for now)
        private readonly Dictionary<string, ObservableCollection<MemberItem>> _projectMembers = new();

        public ObservableCollection<ChatMessage> Messages { get; set; } = new ObservableCollection<ChatMessage>();

        // Messages per channel: "projectName/channelName" -> list of messages
        private readonly Dictionary<string, ObservableCollection<ChatMessage>> _channelMessages = new();
        private string? _selectedChannel;

        public MainWindow(string accessToken, List<RepoItem> projects, string username, string avatarUrl)
        {
            InitializeComponent();

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

            // Initialize channels for each project with a default "general" channel
            foreach (var project in _projects)
            {
                var channels = new ObservableCollection<ChannelItem>
                {
                    new ChannelItem { Name = "general" }
                };
                _projectChannels[project.name] = channels;

                // Initialize members (add current user)
                var members = new ObservableCollection<MemberItem>
                {
                    new MemberItem
                    {
                        Username = _username,
                        Initials = _username.Length >= 2 ? _username.Substring(0, 2).ToUpper() : _username.ToUpper(),
                        Role = "Owner",
                        IsOnline = true,
                        AvatarUrl = string.IsNullOrEmpty(_avatarUrl) ? null : _avatarUrl
                    }
                };
                _projectMembers[project.name] = members;
            }

            MessageInput.Text = "";

            // Show home view on startup
            ShowHomeView();
        }

        private string GenerateIconLetters(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";

            // Split by common separators
            var parts = name.Split(new[] { '-', '_', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
            {
                // Take first letter of first two parts
                return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}";
            }

            // For single-word names, use first 2 chars
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

                // Show channels UI
                ChannelsSectionHeader.Visibility = Visibility.Visible;
                ChannelList.Visibility = Visibility.Visible;

                // Load channels for this project
                if (_projectChannels.TryGetValue(selected.name, out var channels))
                {
                    ChannelList.ItemsSource = channels;

                    // Auto-select "general" channel
                    if (channels.Count > 0)
                    {
                        ChannelList.SelectedIndex = 0;
                    }
                }

                // Load members for this project
                if (_projectMembers.TryGetValue(selected.name, out var members))
                {
                    MembersList.ItemsSource = members;
                }

                // Switch to chat view
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
            // Deselect project
            ProjectList.SelectedIndex = -1;
            _selectedProject = null;
            _selectedChannel = null;

            // Reset channel panel to home state
            SelectedProjectName.Text = "Home";
            ChannelsSectionHeader.Visibility = Visibility.Collapsed;
            ChannelList.Visibility = Visibility.Collapsed;
            ChannelList.ItemsSource = null;

            // Switch views
            ChatView.Visibility = Visibility.Collapsed;
            HomeView.Visibility = Visibility.Visible;

            // Update home content
            HomeWelcomeText.Text = $"Welcome back, {_username}!";
            HomeProjectCountText.Text = $"{_projects.Count} project{(_projects.Count != 1 ? "s" : "")} imported";
            HomeProjectList.ItemsSource = _projects;

            // Hide members panel
            _membersPanelVisible = false;
            MembersPanelColumn.Width = new GridLength(0);
            MembersPanelBorder.Visibility = Visibility.Collapsed;
        }

        private void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChannelList.SelectedItem is ChannelItem selectedChannel && _selectedProject != null)
            {
                // Save current messages
                if (_selectedChannel != null)
                {
                    _channelMessages[_selectedChannel] = new ObservableCollection<ChatMessage>(Messages);
                }

                var channelKey = $"{_selectedProject}/{selectedChannel.Name}";
                _selectedChannel = channelKey;

                ChatTitle.Text = selectedChannel.Name;
                ChatSubtitle.Text = $"{_selectedProject} · #{selectedChannel.Name}";

                // Load messages for this channel
                if (_channelMessages.TryGetValue(channelKey, out var existingMessages))
                {
                    Messages.Clear();
                    foreach (var msg in existingMessages)
                    {
                        Messages.Add(msg);
                    }
                }
                else
                {
                    Messages.Clear();
                    Messages.Add(new ChatMessage
                    {
                        IsSystemMessage = true,
                        Content = $"Welcome to #{selectedChannel.Name}! This is the beginning of the conversation.",
                        Timestamp = DateTime.Now.ToString("HH:mm")
                    });
                }

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

        private void CreateChannel()
        {
            var channelName = NewChannelNameInput.Text?.Trim().ToLower().Replace(" ", "-");
            if (string.IsNullOrEmpty(channelName) || _selectedProject == null) return;

            if (_projectChannels.TryGetValue(_selectedProject, out var channels))
            {
                // Check for duplicate
                if (channels.Any(c => c.Name == channelName))
                {
                    MessageBox.Show("A channel with this name already exists.", "DevTavern",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var newChannel = new ChannelItem { Name = channelName };
                channels.Add(newChannel);

                // Select the new channel
                ChannelList.SelectedItem = newChannel;
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

        private void SendMessage()
        {
            var text = MessageInput.Text?.Trim();
            if (string.IsNullOrEmpty(text) || _selectedProject == null || _selectedChannel == null) return;

            Messages.Add(new ChatMessage
            {
                Username = _username,
                Initials = _username.Length >= 2
                    ? _username.Substring(0, 2).ToUpper()
                    : _username.ToUpper(),
                AvatarColor = "#238636",
                UsernameColor = "#238636",
                AvatarUrl = string.IsNullOrEmpty(_avatarUrl) ? null : _avatarUrl,
                Content = text,
                Timestamp = DateTime.Now.ToString("HH:mm"),
                IsSystemMessage = false
            });

            // Save to channel messages
            _channelMessages[_selectedChannel] = new ObservableCollection<ChatMessage>(Messages);

            MessageInput.Text = "";
            MessageInput.Focus();
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