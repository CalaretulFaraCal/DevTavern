using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DevTavern.Client
{
    public class ChannelItem : INotifyPropertyChanged
    {
        public int Id { get; set; }

        private string _name = "";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public int Type { get; set; } = 0;
        public string VoiceGroupKey { get; set; } = "";

        private int _unreadMentionCount;
        public int UnreadMentionCount
        {
            get => _unreadMentionCount;
            set { _unreadMentionCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMentions)); }
        }
        public bool HasMentions => _unreadMentionCount > 0;

        private bool _hasUnreadMessages;
        public bool HasUnreadMessages
        {
            get => _hasUnreadMessages;
            set { _hasUnreadMessages = value; OnPropertyChanged(); }
        }

        private bool _isJoined;
        public bool IsJoined
        {
            get => _isJoined;
            set { _isJoined = value; OnPropertyChanged(); }
        }

        public ObservableCollection<VoiceMember> VoiceMembers { get; } = [];

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
