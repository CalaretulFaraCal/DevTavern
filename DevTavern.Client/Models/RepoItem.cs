using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DevTavern.Client
{
    public class RepoItem : INotifyPropertyChanged
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string fullName { get; set; } = string.Empty;
        public string owner { get; set; } = string.Empty;
        public int DbId { get; set; }
        public bool isPrivate { get; set; }
        public bool isSelected { get; set; }
        public string IconLetters { get; set; } = "";
        public string VisibilityLabel => isPrivate ? "Private" : "Public";

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

        private string? _customImagePath;
        public string? CustomImagePath
        {
            get => _customImagePath;
            set
            {
                _customImagePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCustomImage));
                OnPropertyChanged(nameof(CustomImageSource));
            }
        }

        private string? _serverImageUrl;
        public string? ServerImageUrl
        {
            get => _serverImageUrl;
            set
            {
                _serverImageUrl = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCustomImage));
                OnPropertyChanged(nameof(CustomImageSource));
            }
        }

        public bool HasCustomImage =>
            !string.IsNullOrEmpty(_serverImageUrl) ||
            (!string.IsNullOrEmpty(_customImagePath) && File.Exists(_customImagePath));

        public ImageSource? CustomImageSource
        {
            get
            {
                if (!string.IsNullOrEmpty(_serverImageUrl))
                {
                    try
                    {
                        if (_serverImageUrl.StartsWith("data:"))
                        {
                            var comma = _serverImageUrl.IndexOf(',');
                            var base64 = comma >= 0 ? _serverImageUrl.Substring(comma + 1) : _serverImageUrl;
                            var bytes = Convert.FromBase64String(base64);
                            var bmp = new BitmapImage();
                            using var ms = new System.IO.MemoryStream(bytes);
                            bmp.BeginInit();
                            bmp.StreamSource = ms;
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                            return bmp;
                        }
                        else
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(_serverImageUrl);
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                            return bmp;
                        }
                    }
                    catch { }
                }
                if (string.IsNullOrEmpty(_customImagePath) || !File.Exists(_customImagePath)) return null;
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(_customImagePath!);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bmp.EndInit();
                    return bmp;
                }
                catch { return null; }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
