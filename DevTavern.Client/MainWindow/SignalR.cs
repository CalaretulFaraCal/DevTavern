using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;

namespace DevTavern.Client
{
    public partial class MainWindow
    {
        private async Task SetupSignalRAsync()
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl("https://devtavern.onrender.com/chat")
                .Build();

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

                    if (_lastSeenMessageCount.ContainsKey(_selectedChannelId))
                        _lastSeenMessageCount[_selectedChannelId] = Messages.Count(m => !m.IsSystemMessage && !m.IsDateSeparator);

                    Application.Current.Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToEnd(),
                        System.Windows.Threading.DispatcherPriority.Background);
                });
            });

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

            _hubConnection.On<int, string>("ChannelCreated", (channelId, channelName) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_selectedProject != null && _projectChannels.TryGetValue(_selectedProject, out var channels))
                    {
                        if (!channels.Any(c => c.Id == channelId))
                            channels.Add(new ChannelItem { Id = channelId, Name = channelName });
                    }
                });
            });

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
                    if (idx > 0 && Messages[idx - 1].IsDateSeparator)
                    {
                        bool orphaned = idx >= Messages.Count || Messages[idx].IsDateSeparator;
                        if (orphaned) Messages.RemoveAt(idx - 1);
                    }
                });
            });

            _hubConnection.On<string>("UserWentOnline", (username) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _globalOnlineUsers.Add(username);
                    RefreshMembersList();
                });
            });

            _hubConnection.On<string>("UserWentOffline", (username) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _globalOnlineUsers.Remove(username);
                    RefreshMembersList();
                });
            });

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
                    channel.VoiceMembers.Add(new VoiceMember
                    {
                        Username = joinedUsername,
                        DisplayName = GetDisplayName(joinedUsername),
                        AvatarUrl = GetAvatarUrl(joinedUsername)
                    });
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
                foreach (var u in onlineUsers) _globalOnlineUsers.Add(u);
            }
            catch
            {
                ChatSubtitle.Text = "Offline Mode (Real-time sync disabled)";
            }
        }
    }
}
