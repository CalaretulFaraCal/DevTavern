using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DevTavern.Client
{
    public partial class MainWindow
    {
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
                var jsonArray = JArray.Parse(response);

                var currentRepoIds = new HashSet<string>(_projects.Select(p => p.id));

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

                ImportStatusText.Text = ImportableRepos.Count == 0
                    ? "No new repositories to import."
                    : "Select projects to import to DevTavern.";
            }
            catch (Exception ex)
            {
                ImportStatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void ImportProjectsOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
            => ImportProjectsOverlay.Visibility = Visibility.Collapsed;

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
            if (newSelectedRepos.Count == 0) { ImportProjectsOverlay.Visibility = Visibility.Collapsed; return; }

            ImportStatusText.Text = "Importing and syncing...";

            foreach (var r in newSelectedRepos)
            {
                r.IconLetters = GenerateIconLetters(r.name);
                _projects.Add(r);
            }

            foreach (var project in newSelectedRepos)
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

                        var cResp = await _apiClient.PostAsync($"channels/generate-defaults/{project.DbId}", null);
                        if (cResp.IsSuccessStatusCode)
                        {
                            var cArr = JArray.Parse(await cResp.Content.ReadAsStringAsync());
                            var channelsList = new ObservableCollection<ChannelItem>();
                            foreach (var c in cArr)
                                channelsList.Add(new ChannelItem { Id = c["id"]?.ToObject<int>() ?? 0, Name = c["name"]?.ToString() ?? "" });
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
                }
                catch { }
            }

            ProjectList.ItemsSource = null;
            ProjectList.ItemsSource = _projects;

            try { System.IO.File.WriteAllText("installed_projects.cache", JsonConvert.SerializeObject(_projects)); } catch { }

            ImportProjectsOverlay.Visibility = Visibility.Collapsed;
        }
    }
}
