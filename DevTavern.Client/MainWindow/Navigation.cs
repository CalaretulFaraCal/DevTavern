using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.AspNetCore.SignalR.Client;

namespace DevTavern.Client
{
    public partial class MainWindow
    {
        private async void ProjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_restoringSelection) return;
            if (IsDragSelection()) { CancelDragSelection(ProjectList, e); return; }
            MiniProfilePanel.Visibility = Visibility.Collapsed;
            if (ProjectList.SelectedItem is RepoItem selected)
            {
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
                            await _hubConnection.InvokeAsync("LeaveProject", oldProjectDbId);
                        await _hubConnection.InvokeAsync("JoinProject", newProjectDbId);
                    }
                }
                catch { }

                ChannelsSectionHeader.Visibility = Visibility.Visible;
                ChannelList.Visibility = Visibility.Visible;

                if (_projectChannels.TryGetValue(selected.name, out var channels))
                {
                    ChannelList.ItemsSource = channels;
                    if (channels.Count > 0) ChannelList.SelectedIndex = 0;
                }

                if (!_projectVoiceChannels.ContainsKey(selected.name))
                    _projectVoiceChannels[selected.name] = new System.Collections.ObjectModel.ObservableCollection<ChannelItem>();
                VoiceChannelsSectionHeader.Visibility = Visibility.Visible;
                VoiceChannelList.Visibility = Visibility.Visible;
                VoiceChannelList.ItemsSource = _projectVoiceChannels[selected.name];

                if (_projectVoiceChannels.TryGetValue(selected.name, out var vChannels))
                {
                    foreach (var ch in vChannels)
                    {
                        ch.VoiceMembers.Clear();
                        if (ch == _currentVoiceChannel)
                            ch.VoiceMembers.Add(new VoiceMember { Username = _username, AvatarUrl = _avatarUrl });
                    }
                }
                if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                    try { await _hubConnection.InvokeAsync("RequestProjectVoiceSnapshot", selected.DbId.ToString()); } catch { }

                if (_projectMembers.TryGetValue(selected.name, out var members))
                    RefreshMembersList();

                HomeView.Visibility = Visibility.Collapsed;
                ChatView.Visibility = Visibility.Visible;
                ShowChannelPanel();

                if (_codeBrowserVisible)
                    _ = LoadCodeBrowserAsync();
            }
        }

        private void HomeButton_Click(object sender, MouseButtonEventArgs e) => ShowHomeView();

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

            ProjectPanelColumn.Width = new System.Windows.GridLength(240);
            ProjectList.ItemContainerStyle = (Style)FindResource("WideProjectItemStyle");
            ProjectList.ItemTemplate = (DataTemplate)FindResource("WideProjectItemTemplate");

            HomeButton.Width = 216;
            HomeIcon.Margin = new Thickness(0, 0, 8, 0);
            HomeText.Visibility = Visibility.Visible;
            if (HomeButton.ToolTip is System.Windows.Controls.ToolTip ht) ht.Visibility = Visibility.Collapsed;

            AddProjectButton.Width = 216;
            AddProjectIcon.Margin = new Thickness(0, -2, 8, 0);
            AddProjectButton.Margin = new Thickness(0, 0, 0, 8);
            AddProjectStack.HorizontalAlignment = HorizontalAlignment.Center;
            AddProjectStack.Margin = new Thickness(0);
            AddProjectText.Visibility = Visibility.Visible;
            if (AddProjectButton.ToolTip is string) AddProjectButton.ToolTip = null;

            ProfileBarUsername.Visibility = Visibility.Visible;
            ProfileSettingsButton.Visibility = Visibility.Visible;
            ProfileSettingsButton.Margin = new Thickness(0, 0, 16, 0);
            UserInitials.Margin = new Thickness(0);
            UserAvatarImage.Margin = new Thickness(0);
            ProfileBarUsername.Text = PopupUsername.Text;
            if (ProfileBarBorder != null) ProfileBarBorder.Padding = new Thickness(16, 12, 16, 12);

            if (ChannelPanelColumn.Width.Value > 0)
                _channelPanelWidth = ChannelPanelColumn.Width.Value;

            ChannelPanelColumn.Width = new System.Windows.GridLength(0);
            ChannelPanelColumn.MinWidth = 0;
            ChannelPanelColumn.MaxWidth = 0;
            ChannelPanelBorder.Visibility = Visibility.Collapsed;
            ChannelSplitterColumn.Width = new System.Windows.GridLength(0);
            ChannelSplitter.Visibility = Visibility.Collapsed;

            ServerSettingsHeaderButton.Visibility = Visibility.Collapsed;

            _membersPanelVisible = false;
            MembersPanelColumn.Width = new System.Windows.GridLength(0);
            MembersPanelBorder.Visibility = Visibility.Collapsed;
        }

        private void ShowChannelPanel()
        {
            ProjectPanelColumn.Width = new System.Windows.GridLength(64);
            ProjectList.ItemContainerStyle = (Style)FindResource("NarrowProjectItemStyle");
            ProjectList.ItemTemplate = (DataTemplate)FindResource("NarrowProjectItemTemplate");

            HomeButton.Width = 40;
            HomeIcon.Margin = new Thickness(0);
            HomeIcon.HorizontalAlignment = HorizontalAlignment.Center;
            HomeText.Visibility = Visibility.Collapsed;
            if (HomeButton.ToolTip is System.Windows.Controls.ToolTip ht2) ht2.Visibility = Visibility.Visible;

            AddProjectButton.Width = 40;
            AddProjectIcon.Margin = new Thickness(0, -2, 0, 0);
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
            ChannelPanelColumn.Width = new System.Windows.GridLength(Math.Max(180, Math.Min(360, _channelPanelWidth)));
            ChannelPanelBorder.Visibility = Visibility.Visible;
            ChannelSplitterColumn.Width = new System.Windows.GridLength(4);
            ChannelSplitter.Visibility = Visibility.Visible;
        }

        private void HomeProjectCard_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is RepoItem project)
            {
                var idx = _projects.IndexOf(project);
                if (idx >= 0) ProjectList.SelectedIndex = idx;
            }
        }

        private void UserAvatarBar_Click(object sender, MouseButtonEventArgs e)
        {
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

        private async void LogoutButton_Click(object sender, EventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Are you sure you want to log out?", "Logout",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result != System.Windows.MessageBoxResult.Yes) return;

            if (_hubConnection != null)
            {
                try { await _hubConnection.StopAsync(); } catch { }
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }

            Services.GitHubAuthService.ClearTokenCache();

            var loginWindow = new LoginWindow();
            loginWindow.Show();
            this.Close();
        }

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
    }
}
