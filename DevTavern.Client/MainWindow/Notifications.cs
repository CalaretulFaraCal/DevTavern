using Newtonsoft.Json.Linq;

namespace DevTavern.Client
{
    public partial class MainWindow
    {
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
}
