using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace DevTavern.Client
{
    public static class AvalonEditHelper
    {
        public static readonly DependencyProperty BindableTextProperty =
            DependencyProperty.RegisterAttached("BindableText", typeof(string), typeof(AvalonEditHelper),
                new PropertyMetadata(null, OnBindableTextChanged));

        public static readonly DependencyProperty FileExtensionProperty =
            DependencyProperty.RegisterAttached("FileExtension", typeof(string), typeof(AvalonEditHelper),
                new PropertyMetadata(null, OnFileExtensionChanged));

        public static string? GetBindableText(DependencyObject obj) => (string?)obj.GetValue(BindableTextProperty);
        public static void SetBindableText(DependencyObject obj, string? value) => obj.SetValue(BindableTextProperty, value);

        public static string? GetFileExtension(DependencyObject obj) => (string?)obj.GetValue(FileExtensionProperty);
        public static void SetFileExtension(DependencyObject obj, string? value) => obj.SetValue(FileExtensionProperty, value);

        private static void OnBindableTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ICSharpCode.AvalonEdit.TextEditor editor)
                editor.Text = (string?)e.NewValue ?? "";
        }

        private static void OnFileExtensionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ICSharpCode.AvalonEdit.TextEditor editor) return;
            string ext = (string?)e.NewValue ?? "";
            if (!string.IsNullOrEmpty(ext))
                editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
            editor.TextArea.Caret.CaretBrush = Brushes.Transparent;
            if (!editor.TextArea.TextView.LineTransformers.OfType<DarkModeColorizer>().Any())
                editor.TextArea.TextView.LineTransformers.Add(new DarkModeColorizer());
        }
    }
}
