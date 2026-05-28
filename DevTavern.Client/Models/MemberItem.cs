using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DevTavern.Client
{
    public class MemberItem : INotifyPropertyChanged
    {
        public string Username { get; set; } = "";

        private string _displayName = "";
        public string DisplayName
        {
            get => string.IsNullOrEmpty(_displayName) ? Username : _displayName;
            set { _displayName = value; OnPropertyChanged(); }
        }

        public string Initials { get; set; } = "";
        public string Role { get; set; } = "Member";
        public bool IsOnline { get; set; } = false;
        public string? AvatarUrl { get; set; } = null;
        public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);
        public bool IsHeader { get; set; } = false;

        public List<string> DevRoles { get; set; } = new List<string>();
        public string RoleBadges => DevRoles != null && DevRoles.Count > 0 ? string.Join(" · ", DevRoles) : "";
        public bool HasDevRoles => DevRoles != null && DevRoles.Count > 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
