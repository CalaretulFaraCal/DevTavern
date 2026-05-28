using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DevTavern.Client
{
    public partial class MainWindow
    {
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
            => AddChannelOverlay.Visibility = Visibility.Collapsed;

        private void AddChannelOverlay_MouseDown(object sender, MouseButtonEventArgs e)
            => AddChannelOverlay.Visibility = Visibility.Collapsed;

        private void ConfirmAddChannel_Click(object sender, RoutedEventArgs e) => CreateChannel();

        private void NewChannelNameInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CreateChannel(); e.Handled = true; }
            else if (e.Key == Key.Escape) { AddChannelOverlay.Visibility = Visibility.Collapsed; e.Handled = true; }
        }

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
                    var content = new System.Net.Http.StringContent(JsonConvert.SerializeObject(postData), System.Text.Encoding.UTF8, "application/json");
                    var resp = await _apiClient.PostAsync($"channels/project/{proj.DbId}", content);

                    if (resp.IsSuccessStatusCode)
                    {
                        var cJson = JObject.Parse(await resp.Content.ReadAsStringAsync());
                        var newChannel = new ChannelItem { Id = cJson["id"]?.ToObject<int>() ?? 0, Name = channelName };
                        channels.Add(newChannel);
                        ChannelList.SelectedItem = newChannel;

                        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                            await _hubConnection.InvokeAsync("NotifyChannelCreated", proj.DbId.ToString(), newChannel.Id, newChannel.Name);
                    }
                }
                catch { }
            }

            AddChannelOverlay.Visibility = Visibility.Collapsed;
        }

        private void ChannelSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
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

                    OverviewTabContent.Visibility = Visibility.Visible;
                    PermissionsTabContent.Visibility = Visibility.Collapsed;
                    ChannelSettingsOverlay.Visibility = Visibility.Visible;
                    EditChannelNameInput.Focus();
                    EditChannelNameInput.SelectAll();
                }
            }
            e.Handled = true;
        }

        private static T? FindParent<T>(System.Windows.DependencyObject child) where T : System.Windows.DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && parent is not T)
                parent = VisualTreeHelper.GetParent(parent);
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

            if (newName == _editingChannel.Name) { ChannelSettingsOverlay.Visibility = Visibility.Collapsed; return; }

            if (_editingChannel.Type == 2)
            {
                if (_selectedProject != null && _projectVoiceChannels.TryGetValue(_selectedProject, out var vCh))
                {
                    if (vCh.Any(c => c.Name == newName && c.Id != _editingChannel.Id))
                    {
                        MessageBox.Show("A voice channel with this name already exists.", "DevTavern", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                        MessageBox.Show("A channel with this name already exists.", "DevTavern", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }

            try
            {
                var body = new System.Net.Http.StringContent(
                    JsonConvert.SerializeObject(new { Name = newName }),
                    System.Text.Encoding.UTF8, "application/json");
                var resp = await _apiClient.PutAsync($"channels/{_editingChannel.Id}/rename", body);
                if (!resp.IsSuccessStatusCode)
                {
                    MessageBox.Show("Failed to rename channel. Please try again.", "DevTavern", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            catch
            {
                MessageBox.Show("Could not reach the server. Please check your connection.", "DevTavern", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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

        private void DeleteChannel_Click(object sender, RoutedEventArgs e)
        {
            if (_editingChannel == null || _selectedProject == null) return;
            DeleteConfirmMessage.Text = _editingChannel.Type == 2
                ? $"Are you sure you want to delete voice channel \"{_editingChannel.Name}\"?\nThis action cannot be undone."
                : $"Are you sure you want to delete #{_editingChannel.Name}?\nThis action cannot be undone and all messages will be lost.";
            DeleteConfirmOverlay.Visibility = Visibility.Visible;
        }

        private void DeleteConfirmCancel_Click(object sender, RoutedEventArgs e)
            => DeleteConfirmOverlay.Visibility = Visibility.Collapsed;

        private void DeleteConfirmOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
            => DeleteConfirmOverlay.Visibility = Visibility.Collapsed;

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
                        if (_currentVoiceChannel?.Id == deletedId) LeaveCurrentVoiceChannel();
                    }
                    else
                    {
                        if (_projectChannels.TryGetValue(_selectedProject, out var channels))
                        {
                            channels.Remove(_editingChannel);
                            if (_selectedChannelId == deletedId)
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
                        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                            await _hubConnection.InvokeAsync("NotifyChannelDeleted", proj.DbId.ToString(), deletedId);
                    }
                }
            }
            catch { }

            ChannelSettingsOverlay.Visibility = Visibility.Collapsed;
            _editingChannel = null;
        }
    }
}
