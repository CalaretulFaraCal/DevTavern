using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DevTavern.Client
{
    public class ChatMessage : INotifyPropertyChanged
    {
        public string Username { get; set; } = "";

        private string _displayName = "";
        public string DisplayName
        {
            get => string.IsNullOrEmpty(_displayName) ? Username : _displayName;
            set { _displayName = value; OnPropertyChanged(); }
        }

        public string Initials { get; set; } = "";
        public string AvatarColor { get; set; } = "#8B949E";
        public string UsernameColor { get; set; } = "#E6EDF3";
        public string? AvatarUrl { get; set; } = null;
        public string Timestamp { get; set; } = "";
        public bool IsSystemMessage { get; set; } = false;
        public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);
        public bool IsMentioningMe { get; set; } = false;
        public bool IsOwnMessage { get; set; } = false;
        public int MessageId { get; set; } = 0;

        private string _content = "";
        public string Content
        {
            get => _content;
            set { _content = value; OnPropertyChanged(); }
        }

        private string _displayContent = "";
        public string DisplayContent
        {
            get => _displayContent;
            set { _displayContent = value; OnPropertyChanged(); }
        }

        private bool _isEdited;
        public bool IsEdited
        {
            get => _isEdited;
            set { _isEdited = value; OnPropertyChanged(); }
        }

        private bool _isDeleted;
        public bool IsDeleted
        {
            get => _isDeleted;
            set { _isDeleted = value; OnPropertyChanged(); OnPropertyChanged(nameof(ContentColor)); }
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set { _isEditing = value; OnPropertyChanged(); }
        }

        public string EditContent { get; set; } = "";

        public string ContentColor => IsDeleted ? "#6E7681" : "#E6EDF3";

        public bool HasCodeReference { get; set; } = false;
        public string CodeFilePath { get; set; } = "";
        public string CodeBranch { get; set; } = "";
        public string CodeSelectedText { get; set; } = "";
        public string CodeFileExtension => string.IsNullOrEmpty(CodeFilePath)
            ? ""
            : Path.GetExtension(CodeFilePath);

        private string _imageUrl = "";
        public string ImageUrl
        {
            get => _imageUrl;
            set
            {
                if (_imageUrl != value)
                {
                    _imageUrl = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasImage));
                    UpdateImageMedia();
                }
            }
        }

        public bool HasImage => !string.IsNullOrEmpty(ImageUrl);

        private ImageSource? _imageMedia;
        public ImageSource? ImageMedia
        {
            get => _imageMedia;
            private set { _imageMedia = value; OnPropertyChanged(); }
        }

        private void UpdateImageMedia()
        {
            if (string.IsNullOrEmpty(_imageUrl)) { ImageMedia = null; return; }
            try
            {
                string base64Data = _imageUrl;
                var match = Regex.Match(_imageUrl, @"data:image/(?<type>.+?);base64,(?<data>.+)");
                if (match.Success) base64Data = match.Groups["data"].Value;

                byte[] imageBytes = Convert.FromBase64String(base64Data);
                var bitmap = new BitmapImage();
                using (var ms = new MemoryStream(imageBytes))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                }
                bitmap.Freeze();
                ImageMedia = bitmap;
            }
            catch { ImageMedia = null; }
        }

        public bool IsDateSeparator { get; set; } = false;
        public string DateLabel { get; set; } = "";
        public DateTime MessageDate { get; set; } = DateTime.MinValue;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
