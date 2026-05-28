using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DevTavern.Client
{
    public partial class MainWindow
    {
        private async void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_restoringSelection) return;
            if (IsDragSelection()) { CancelDragSelection(ChannelList, e); return; }
            if (ChannelList.SelectedItem is ChannelItem selectedChannel && _selectedProject != null)
            {
                if (_selectedChannelId > 0)
                    _channelDrafts[_selectedChannelId] = MessageInput.Text ?? "";

                int oldChannelId = _selectedChannelId;
                _selectedChannelId = selectedChannel.Id;

                try
                {
                    if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                    {
                        if (oldChannelId > 0)
                            await _hubConnection.InvokeAsync("LeaveChannel", oldChannelId.ToString());
                        await _hubConnection.InvokeAsync("JoinChannel", _selectedChannelId.ToString());
                    }
                }
                catch { }

                ChatTitle.Text = selectedChannel.Name;
                ChatSubtitle.Text = $"{_selectedProject} · #{selectedChannel.Name}";
                Messages.Clear();

                try
                {
                    var mResp = await _apiClient.GetStringAsync($"messages/channel/{selectedChannel.Id}");
                    var mArr = JArray.Parse(mResp);
                    DateTime? prevMsgDate = null;
                    foreach (var m in mArr)
                    {
                        string content = m["content"]?.ToString() ?? "";
                        DateTime msgLocalTime = DateTime.Now;
                        try { msgLocalTime = m["sentAt"]?.ToObject<DateTime>().ToLocalTime() ?? DateTime.Now; } catch { }
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

                if (_selectedProject != null && _projectChannels.TryGetValue(_selectedProject, out var chList))
                {
                    var readCh = chList.FirstOrDefault(c => c.Id == _selectedChannelId);
                    if (readCh != null) { readCh.UnreadMentionCount = 0; readCh.HasUnreadMessages = false; }
                }
                _lastSeenMessageCount[_selectedChannelId] = Messages.Count(m => !m.IsSystemMessage && !m.IsDateSeparator);
                UpdateProjectBadge(_selectedProject ?? "");

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

        private void SendButton_Click(object sender, RoutedEventArgs e) => SendMessage();

        private bool _isMentioning = false;
        private int _mentionStartIndex = -1;

        private void MessageInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = MessageInput.Text ?? "";
            var caretIndex = MessageInput.CaretIndex;

            int atIndex = -1;
            for (int i = caretIndex - 1; i >= 0; i--)
            {
                if (text[i] == '@') { atIndex = i; break; }
                if (text[i] == ' ') break;
            }

            if (atIndex >= 0)
            {
                string query = text.Substring(atIndex + 1, caretIndex - atIndex - 1).ToLower();
                _isMentioning = true;
                _mentionStartIndex = atIndex;

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

        private void MentionPopup_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MentionPopup.SelectedItem is MemberItem member && _isMentioning
                && System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
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
                var postData = new { Content = finalContent, UserId = _currentUserId, ChannelId = _selectedChannelId };
                var content = new System.Net.Http.StringContent(JsonConvert.SerializeObject(postData), System.Text.Encoding.UTF8, "application/json");
                var sendResp = await _apiClient.PostAsync("messages", content);
                if (sendResp.IsSuccessStatusCode)
                {
                    var respJson = JObject.Parse(await sendResp.Content.ReadAsStringAsync());
                    msg.MessageId = respJson["id"]?.ToObject<int>() ?? 0;
                }

                if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                    await _hubConnection.InvokeAsync("SendLiveMessage", _selectedChannelId.ToString(), _username, _avatarUrl, finalContent);
            }
            catch { }
        }

        private void EditMessage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.MenuItem)?.Tag is not ChatMessage msg || !msg.IsOwnMessage || msg.IsDeleted) return;
            msg.EditContent = msg.Content;
            msg.IsEditing = true;
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.Button)?.Tag is ChatMessage msg)
                msg.IsEditing = false;
        }

        private async void SaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.Button)?.Tag is not ChatMessage msg) return;
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
                    var body = new System.Net.Http.StringContent(
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
            if ((sender as System.Windows.Controls.MenuItem)?.Tag is not ChatMessage msg || !msg.IsOwnMessage) return;
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
                ResolveDisplayNameAsync(msg.Username, name => msg.DisplayName = name);

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
                                    msg.CodeSelectedText = rawDisplay.Substring(contentStart, codeBlockEnd - contentStart).Trim('\r', '\n');
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

            while (msg.DisplayContent.Contains("[IMAGE:"))
            {
                int startIndex = msg.DisplayContent.IndexOf("[IMAGE:");
                int endIndex = msg.DisplayContent.IndexOf("]", startIndex);
                if (endIndex > startIndex)
                {
                    string imgData = msg.DisplayContent.Substring(startIndex + 7, endIndex - startIndex - 7);
                    msg.ImageUrl = imgData;
                    string before = msg.DisplayContent.Substring(0, startIndex).Trim();
                    string after = msg.DisplayContent.Substring(endIndex + 1).Trim();
                    msg.DisplayContent = (before + "\n" + after).Trim();
                }
                else break;
            }

            return msg;
        }
    }
}
