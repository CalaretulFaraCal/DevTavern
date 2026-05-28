using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DevTavern.Client
{
    public partial class MainWindow
    {
        private void RefreshMembersList()
        {
            if (string.IsNullOrEmpty(_selectedProject) || !_projectMembers.TryGetValue(_selectedProject, out var baseMembers))
            {
                MembersList.ItemsSource = null;
                return;
            }

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

            foreach (var m in baseMembers)
            {
                if (m.IsHeader) continue;
                m.IsOnline = _globalOnlineUsers.Contains(m.Username);

                string primaryRole = "ONLINE";
                if (m.DevRoles != null && m.DevRoles.Count > 0)
                {
                    foreach (var h in hierarchy)
                    {
                        if (m.DevRoles.Contains(h)) { primaryRole = h; break; }
                    }
                }

                m.Role = primaryRole == "ONLINE" ? "Member" : primaryRole;
                memberRoles[m] = primaryRole;
            }

            var allCategories = hierarchy.ToList();
            allCategories.Add("ONLINE");

            foreach (var cat in allCategories)
            {
                var membersInCat = baseMembers
                    .Where(m => !m.IsHeader && memberRoles.ContainsKey(m) && memberRoles[m] == cat)
                    .OrderBy(m => m.Username).ToList();

                if (membersInCat.Count > 0)
                {
                    groupedList.Add(new MemberItem { Username = $"{cat.ToUpper()} — {membersInCat.Count}", IsHeader = true });
                    foreach (var m in membersInCat) groupedList.Add(m);
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

        private void ProfileGitHubLink_Click(object sender, RoutedEventArgs e)
        {
            if (_profileGitHubUrl != null)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_profileGitHubUrl) { UseShellExecute = true });
        }

        private void MemberAssignRole_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is MemberItem member)
                OpenRoleSelectionFor(member.Username);
        }

        private string? _targetMemberForRoles = null;

        private void OpenRoleSelectionFor(string targetUsername)
        {
            _targetMemberForRoles = targetUsername;

            foreach (UIElement child in RoleCheckboxesContainer.Children)
                if (child is CheckBox cb) cb.IsChecked = false;

            if (_selectedProject != null && _projectMembers.TryGetValue(_selectedProject, out var members))
            {
                var me = members.FirstOrDefault(m => m.Username == targetUsername);
                if (me != null && me.DevRoles != null)
                {
                    foreach (UIElement child in RoleCheckboxesContainer.Children)
                    {
                        if (child is CheckBox cb && cb.Content is string cbText)
                            if (me.DevRoles.Contains(cbText)) cb.IsChecked = true;
                    }
                }
            }

            RoleSelectionOverlay.Visibility = Visibility.Visible;
        }

        private void RoleSelectionOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
            => RoleSelectionOverlay.Visibility = Visibility.Collapsed;

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
                        if (child is CheckBox cb && cb.IsChecked == true && cb.Content is string cbText)
                            me.DevRoles.Add(cbText);
                    }

                    var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
                    if (proj != null && proj.DbId > 0)
                    {
                        try
                        {
                            string rolesCsv = string.Join(", ", me.DevRoles);
                            var postData = new { Username = _username, DevRoles = rolesCsv };
                            var content = new System.Net.Http.StringContent(JsonConvert.SerializeObject(postData), System.Text.Encoding.UTF8, "application/json");
                            var resp = await _apiClient.PostAsync($"projects/{proj.DbId}/roles", content);

                            if (resp.IsSuccessStatusCode && _hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                                await _hubConnection.InvokeAsync("NotifyRolesChanged", proj.DbId.ToString(), _username, rolesCsv);
                        }
                        catch { }
                    }

                    RefreshMembersList();
                }
            }
        }
    }
}
