using System.Collections.ObjectModel;

namespace DevTavern.Client
{
    public class GitTreeNode
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public ObservableCollection<GitTreeNode> Children { get; set; } = new ObservableCollection<GitTreeNode>();
        public string Icon => IsDirectory ? "📁" : "📄";
    }
}
