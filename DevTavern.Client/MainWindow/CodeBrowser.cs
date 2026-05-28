using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using Newtonsoft.Json.Linq;

namespace DevTavern.Client
{
    public partial class MainWindow
    {
        private bool _codeBrowserVisible = false;
        private string _codeBrowserCurrentBranch = "";
        private bool _codeBranchChanging = false;

        private void BrowseCodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_codeBrowserVisible)
            {
                CodeBrowserPanel.Visibility = Visibility.Collapsed;
                CodeBrowserSplitter.Visibility = Visibility.Collapsed;
                SplitterColumn.Width = new System.Windows.GridLength(0);
                MembersPanelColumn.Width = new System.Windows.GridLength(0);
                _codeBrowserVisible = false;
            }
            else
            {
                _ = LoadCodeBrowserAsync();
            }
        }

        private async Task LoadCodeBrowserAsync()
        {
            if (_selectedProject == null) return;
            var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
            if (proj == null || string.IsNullOrEmpty(proj.fullName)) return;

            CodeBrowserTitle.Text = proj.name;
            CodeBrowserSubtitle.Text = proj.fullName;

            CodeTreeView.ItemsSource = null;
            CodeViewerContent.Text = "";
            CodeViewerFilePath.Text = "Select a file to view its contents";
            CodeViewerWelcome.Visibility = Visibility.Visible;
            CodeContentLoading.Visibility = Visibility.Collapsed;
            CodeTreeLoading.Visibility = Visibility.Visible;

            if (_membersPanelVisible)
            {
                MembersPanelBorder.Visibility = Visibility.Collapsed;
                _membersPanelVisible = false;
            }

            SplitterColumn.Width = new System.Windows.GridLength(5);
            MembersPanelColumn.Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
            CodeBrowserSplitter.Visibility = Visibility.Visible;
            CodeBrowserPanel.Visibility = Visibility.Visible;
            _codeBrowserVisible = true;

            try
            {
                using var ghClient = new HttpClient();
                ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                var branchResp = await ghClient.GetStringAsync($"https://api.github.com/repos/{proj.fullName}/branches");
                var branchArr = JArray.Parse(branchResp);
                var branchNames = branchArr.Select(b => b["name"]?.ToString() ?? "").Where(n => n.Length > 0).ToList();

                _codeBranchChanging = true;
                CodeBranchSelector.ItemsSource = branchNames;

                int defaultIdx = branchNames.IndexOf("main");
                if (defaultIdx < 0) defaultIdx = branchNames.IndexOf("master");
                if (defaultIdx < 0 && branchNames.Count > 0) defaultIdx = 0;

                CodeBranchSelector.SelectedIndex = defaultIdx;
                _codeBranchChanging = false;

                if (defaultIdx >= 0)
                {
                    _codeBrowserCurrentBranch = branchNames[defaultIdx];
                    await LoadBranchTree(proj.fullName, _codeBrowserCurrentBranch);
                }
            }
            catch (Exception ex)
            {
                CodeTreeLoading.Text = $"Error: {ex.Message}";
            }
        }

        private async void CodeBranchSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_codeBranchChanging) return;
            if (CodeBranchSelector.SelectedItem is string branch && !string.IsNullOrEmpty(branch))
            {
                _codeBrowserCurrentBranch = branch;
                var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
                if (proj != null && !string.IsNullOrEmpty(proj.fullName))
                {
                    CodeViewerContent.Text = "";
                    CodeViewerFilePath.Text = "Select a file to view its contents";
                    CodeViewerWelcome.Visibility = Visibility.Visible;
                    CodeContentLoading.Visibility = Visibility.Collapsed;
                    await LoadBranchTree(proj.fullName, branch);
                }
            }
        }

        private async Task LoadBranchTree(string fullName, string branch)
        {
            CodeTreeView.ItemsSource = null;
            CodeTreeLoading.Text = "Loading file tree...";
            CodeTreeLoading.Visibility = Visibility.Visible;

            try
            {
                using var ghClient = new HttpClient();
                ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                var treeResp = await ghClient.GetStringAsync($"https://api.github.com/repos/{fullName}/git/trees/{branch}?recursive=1");
                var treeJson = JObject.Parse(treeResp);
                var treeArr = treeJson["tree"] as JArray;

                if (treeArr == null) { CodeTreeLoading.Text = "No files found."; return; }

                var root = new List<GitTreeNode>();
                var nodeMap = new Dictionary<string, GitTreeNode>();

                var sortedItems = treeArr
                    .OrderBy(i => i["type"]?.ToString() == "blob" ? 1 : 0)
                    .ThenBy(i => i["path"]?.ToString())
                    .ToList();

                foreach (var item in sortedItems)
                {
                    string path = item["path"]?.ToString() ?? "";
                    string type = item["type"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(path)) continue;

                    var parts = path.Split('/');
                    string name = parts[parts.Length - 1];

                    var node = new GitTreeNode
                    {
                        Name = name,
                        FullPath = path,
                        IsDirectory = type == "tree",
                        Children = new ObservableCollection<GitTreeNode>()
                    };

                    nodeMap[path] = node;

                    if (parts.Length == 1)
                    {
                        root.Add(node);
                    }
                    else
                    {
                        string parentPath = string.Join("/", parts.Take(parts.Length - 1));
                        if (nodeMap.TryGetValue(parentPath, out var parentNode))
                            parentNode.Children.Add(node);
                        else
                            root.Add(node);
                    }
                }

                SortTreeNodes(root);
                CodeTreeView.ItemsSource = root;
                CodeTreeLoading.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                CodeTreeLoading.Text = $"Error: {ex.Message}";
            }
        }

        private void SortTreeNodes(List<GitTreeNode> nodes)
        {
            nodes.Sort((a, b) =>
            {
                if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var node in nodes)
            {
                if (node.Children.Count > 0)
                {
                    var childList = node.Children.ToList();
                    SortTreeNodes(childList);
                    node.Children = new ObservableCollection<GitTreeNode>(childList);
                }
            }
        }

        private async void CodeTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is GitTreeNode selectedNode && !selectedNode.IsDirectory)
            {
                var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
                if (proj == null || string.IsNullOrEmpty(proj.fullName)) return;

                CodeViewerWelcome.Visibility = Visibility.Collapsed;
                CodeViewerContent.Visibility = Visibility.Collapsed;
                CodeContentLoading.Visibility = Visibility.Visible;
                CodeViewerFilePath.Text = selectedNode.FullPath;

                try
                {
                    using var ghClient = new HttpClient();
                    ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                    ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                    var contentResp = await ghClient.GetStringAsync(
                        $"https://api.github.com/repos/{proj.fullName}/contents/{selectedNode.FullPath}?ref={_codeBrowserCurrentBranch}");
                    var contentJson = JObject.Parse(contentResp);

                    string encoding = contentJson["encoding"]?.ToString() ?? "";
                    string content = contentJson["content"]?.ToString() ?? "";
                    int size = contentJson["size"]?.ToObject<int>() ?? 0;

                    if (encoding == "base64" && !string.IsNullOrEmpty(content))
                    {
                        byte[] bytes = Convert.FromBase64String(content);
                        string decoded = System.Text.Encoding.UTF8.GetString(bytes);
                        CodeViewerContent.Text = decoded;
                        string ext = System.IO.Path.GetExtension(selectedNode.FullPath);
                        CodeViewerContent.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext) ?? HighlightingManager.Instance.GetDefinitionByExtension(".txt");
                        CodeViewerContent.Visibility = Visibility.Visible;
                    }
                    else if (size > 1_000_000)
                    {
                        CodeViewerContent.Text = "⚠ File too large to display.";
                        CodeViewerContent.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        CodeViewerContent.Text = "⚠ Binary file — cannot display content.";
                        CodeViewerContent.Visibility = Visibility.Visible;
                    }
                }
                catch (Exception ex)
                {
                    CodeViewerContent.Text = $"Error loading file: {ex.Message}";
                    CodeViewerContent.Visibility = Visibility.Visible;
                }

                CodeContentLoading.Visibility = Visibility.Collapsed;
            }
        }

        private void CloseCodeBrowser_Click(object sender, RoutedEventArgs e)
        {
            CodeBrowserPanel.Visibility = Visibility.Collapsed;
            CodeBrowserSplitter.Visibility = Visibility.Collapsed;
            SplitterColumn.Width = new System.Windows.GridLength(0);
            MembersPanelColumn.Width = new System.Windows.GridLength(0);
            _codeBrowserVisible = false;
        }

        private void QuoteSelectedCode_Click(object sender, RoutedEventArgs e)
        {
            if (CodeViewerContent.SelectedText.Length > 0 && _selectedChannelId > 0)
            {
                string selectedCode = CodeViewerContent.SelectedText;
                string filePath = CodeViewerFilePath.Text;
                string separator = filePath.Contains("/") ? "/" : "\\";
                string fileName = filePath.Contains(separator) ? filePath.Substring(filePath.LastIndexOf(separator) + 1) : filePath;

                string currentText = MessageInput.Text ?? "";
                if (currentText.Length > 0 && !currentText.EndsWith("\n"))
                    currentText += "\n";

                currentText += $"[CodeRef: {_codeBrowserCurrentBranch}|{filePath}]\nFrom `{fileName}`:\n```\n{selectedCode}\n```\n";
                MessageInput.Text = currentText;
                MessageInput.CaretIndex = MessageInput.Text.Length;
                MessageInput.Focus();
            }
            else if (_selectedChannelId == 0)
            {
                MessageBox.Show("Please select a channel before quoting code.", "DevTavern", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void JumpToCode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ChatMessage msg && msg.HasCodeReference)
            {
                if (!_codeBrowserVisible)
                    await LoadCodeBrowserAsync();

                if (CodeBranchSelector.Items.Contains(msg.CodeBranch))
                    CodeBranchSelector.SelectedItem = msg.CodeBranch;

                var proj = _projects.FirstOrDefault(p => p.name == _selectedProject);
                if (proj == null || string.IsNullOrEmpty(proj.fullName)) return;

                CodeViewerWelcome.Visibility = Visibility.Collapsed;
                CodeViewerContent.Visibility = Visibility.Collapsed;
                CodeContentLoading.Visibility = Visibility.Visible;
                CodeViewerFilePath.Text = msg.CodeFilePath;

                try
                {
                    using var ghClient = new HttpClient();
                    ghClient.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Client");
                    ghClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                    var contentResp = await ghClient.GetStringAsync(
                        $"https://api.github.com/repos/{proj.fullName}/contents/{msg.CodeFilePath}?ref={msg.CodeBranch}");
                    var contentJson = JObject.Parse(contentResp);

                    string encoding = contentJson["encoding"]?.ToString() ?? "";
                    string content = contentJson["content"]?.ToString() ?? "";

                    if (encoding == "base64" && !string.IsNullOrEmpty(content))
                    {
                        byte[] bytes = Convert.FromBase64String(content);
                        string decoded = System.Text.Encoding.UTF8.GetString(bytes);
                        CodeViewerContent.Text = decoded;
                        string ext = System.IO.Path.GetExtension(msg.CodeFilePath);
                        CodeViewerContent.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext) ?? HighlightingManager.Instance.GetDefinitionByExtension(".txt");
                        CodeViewerContent.Visibility = Visibility.Visible;

                        await Task.Delay(100);
                        if (!string.IsNullOrEmpty(msg.CodeSelectedText))
                        {
                            int idx = CodeViewerContent.Text.IndexOf(msg.CodeSelectedText);
                            if (idx >= 0)
                            {
                                CodeViewerContent.Focus();
                                CodeViewerContent.Select(idx, msg.CodeSelectedText.Length);
                                int lineIndex = CodeViewerContent.Text.Substring(0, idx).Split('\n').Length;
                                CodeViewerContent.ScrollToLine(Math.Max(1, lineIndex - 5));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    CodeViewerContent.Text = $"Error loading file: {ex.Message}";
                    CodeViewerContent.Visibility = Visibility.Visible;
                }

                CodeContentLoading.Visibility = Visibility.Collapsed;
            }
        }
    }
}
