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
    public partial class MainWindow : Window
    {
        private readonly GitHubAuthService _githubAuth = new GitHubAuthService();
        private readonly HttpClient _apiClient;
        public ObservableCollection<RepoItem> MyRepos { get; set; } = new ObservableCollection<RepoItem>();
        private string _accessToken = "";

        public MainWindow()
        {
            InitializeComponent();
            
            _apiClient = new HttpClient { BaseAddress = new Uri("http://localhost:5114/api/") };
            ReposList.ItemsSource = MyRepos;
        }

        // Custom title bar drag support
        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

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
                    id = repo["id"].ToString(), 
                    name = repo["name"].ToString(), 
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

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedRepos = new List<RepoItem>();
            foreach (var r in MyRepos)
            {
                if (r.isSelected) selectedRepos.Add(r);
            }

            MessageBox.Show($"You selected {selectedRepos.Count} projects for import!");
        }
    }

    public class RepoItem
    {
        public string id { get; set; }
        public string name { get; set; }
        public bool isPrivate { get; set; }
        public bool isSelected { get; set; }
    }
}