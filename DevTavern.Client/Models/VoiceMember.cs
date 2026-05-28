using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DevTavern.Client
{
    public class VoiceMember : INotifyPropertyChanged
    {
        public string Username { get; set; } = "";

        private string _displayName = "";
        public string DisplayName
        {
            get => string.IsNullOrEmpty(_displayName) ? Username : _displayName;
            set { _displayName = value; OnPropertyChanged(); }
        }

        private bool _isMuted;
        public bool IsMuted
        {
            get => _isMuted;
            set { _isMuted = value; OnPropertyChanged(); }
        }

        private bool _isDeafened;
        public bool IsDeafened
        {
            get => _isDeafened;
            set { _isDeafened = value; OnPropertyChanged(); }
        }

        private bool _isSpeaking;
        public bool IsSpeaking
        {
            get => _isSpeaking;
            set { _isSpeaking = value; OnPropertyChanged(); }
        }

        public string? AvatarUrl { get; set; } = null;
        public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
