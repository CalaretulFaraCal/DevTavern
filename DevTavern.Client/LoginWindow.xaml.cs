using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using DevTavern.Client.Services;
using Newtonsoft.Json.Linq;

namespace DevTavern.Client
{
    public partial class LoginWindow : Window
    {
        private readonly GitHubAuthService _githubAuth = new GitHubAuthService();
        private readonly HttpClient _apiClient;
        public ObservableCollection<RepoItem> MyRepos { get; set; } = new ObservableCollection<RepoItem>();
        private string _accessToken = "";

        public LoginWindow()
        {
            InitializeComponent();
            _apiClient = new HttpClient { BaseAddress = new Uri("https://devtavern.onrender.com/api/") };
            ReposList.ItemsSource = MyRepos;
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Waiting for browser login...";
                LoginButton.IsEnabled = false;

                _accessToken = await _githubAuth.LoginAndGetTokenAsync();
                StatusText.Text = "Authenticated! ✓ Loading projects...";

                await FetchReposAutomated();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Login error: {ex.Message}");
                StatusText.Text = "Error obtaining token.";
                LoginButton.IsEnabled = true;
            }
        }

        private async Task FetchReposAutomated()
        {
            var response = await _apiClient.GetStringAsync($"projects/github/my-projects?githubPersonalAccessToken={_accessToken}");
            var jsonArray = JArray.Parse(response);
            MyRepos.Clear();

            foreach (var repo in jsonArray)
            {
                MyRepos.Add(new RepoItem
                {
                    id = repo["id"]?.ToString() ?? string.Empty,
                    name = repo["name"]?.ToString() ?? string.Empty,
                    isPrivate = repo["isPrivate"]?.ToObject<bool>() ?? false,
                    isSelected = true
                });
            }

            StatusText.Text = "Select the projects you want to import:";
            ReposContainer.Visibility = Visibility.Visible;
            ActionButtons.Visibility = Visibility.Visible;
            LoginButton.Visibility = Visibility.Collapsed;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in MyRepos) r.isSelected = true;
            RefreshList();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in MyRepos) r.isSelected = false;
            RefreshList();
        }

        private void RefreshList()
        {
            ReposList.ItemsSource = null;
            ReposList.ItemsSource = MyRepos;
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedRepos = new List<RepoItem>();
            foreach (var r in MyRepos)
            {
                if (r.isSelected) selectedRepos.Add(r);
            }

            // Fetch GitHub user info
            string username = "user";
            string avatarUrl = "";
            string githubId = "";
            int currentUserId = 0;
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
                var userResponse = await client.GetStringAsync("https://api.github.com/user");
                var userJson = JObject.Parse(userResponse);
                username = userJson["login"]?.ToString() ?? "user";
                avatarUrl = userJson["avatar_url"]?.ToString() ?? "";
                githubId = userJson["id"]?.ToString() ?? username;

                // Cautam pe DB utilizatorul sau il adaugam
                var usersResp = await _apiClient.GetStringAsync("users");
                var usersArr = JArray.Parse(usersResp);
                var existingUser = usersArr.FirstOrDefault(u => u["gitHubId"]?.ToString() == githubId);
                
                if (existingUser != null)
                {
                    currentUserId = existingUser["id"]?.ToObject<int>() ?? 0;
                }
                else
                {
                    var postData = new { GitHubId = githubId, Username = username, AvatarUrl = avatarUrl };
                    var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(postData), System.Text.Encoding.UTF8, "application/json");
                    var createResp = await _apiClient.PostAsync("users", content);
                    if (createResp.IsSuccessStatusCode)
                    {
                        var newUserJson = JObject.Parse(await createResp.Content.ReadAsStringAsync());
                        currentUserId = newUserJson["id"]?.ToObject<int>() ?? 0;
                    }
                }
            }
            catch { /* fallback to defaults */ }

            var mainWindow = new MainWindow(_accessToken, selectedRepos, username, avatarUrl, currentUserId);
            mainWindow.Show();
            this.Close();
        }
    }

    public class RepoItem
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public int DbId { get; set; }
        public bool isPrivate { get; set; }
        public bool isSelected { get; set; }
        public string IconLetters { get; set; } = "";
        public string VisibilityLabel => isPrivate ? "🔒 Private" : "🌐 Public";
    }
}
