using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DevTavern.Client
{
    public class KeybindEntry : INotifyPropertyChanged
    {
        private string _key = "Click to bind";
        private string _action = "Toggle Mute";

        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(); }
        }

        public string Action
        {
            get => _action;
            set { _action = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
