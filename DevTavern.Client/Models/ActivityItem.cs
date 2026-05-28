using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DevTavern.Client
{
    public class ActivityItem : INotifyPropertyChanged
    {
        private string _icon = "";
        public string Icon { get => _icon; set { _icon = value; OnPropertyChanged(); } }

        private string _actionTitle = "";
        public string ActionTitle { get => _actionTitle; set { _actionTitle = value; OnPropertyChanged(); } }

        private string _projectName = "";
        public string ProjectName { get => _projectName; set { _projectName = value; OnPropertyChanged(); } }

        private string _actionDetails = "";
        public string ActionDetails { get => _actionDetails; set { _actionDetails = value; OnPropertyChanged(); } }

        private string _timeAgo = "";
        public string TimeAgo { get => _timeAgo; set { _timeAgo = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
