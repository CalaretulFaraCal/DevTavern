using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DevTavern.Client
{
    public static class RichTextBoxHelper
    {
        public static readonly DependencyProperty FormattedTextProperty =
            DependencyProperty.RegisterAttached("FormattedText", typeof(string), typeof(RichTextBoxHelper),
                new PropertyMetadata(string.Empty, (d, e) => RebuildDocument(d as System.Windows.Controls.RichTextBox)));

        public static readonly DependencyProperty FormattedTextIsEditedProperty =
            DependencyProperty.RegisterAttached("FormattedTextIsEdited", typeof(bool), typeof(RichTextBoxHelper),
                new PropertyMetadata(false, (d, e) => RebuildDocument(d as System.Windows.Controls.RichTextBox)));

        public static void SetFormattedText(DependencyObject obj, string value) => obj.SetValue(FormattedTextProperty, value);
        public static string GetFormattedText(DependencyObject obj) => (string)obj.GetValue(FormattedTextProperty);
        public static void SetFormattedTextIsEdited(DependencyObject obj, bool value) => obj.SetValue(FormattedTextIsEditedProperty, value);
        public static bool GetFormattedTextIsEdited(DependencyObject obj) => (bool)obj.GetValue(FormattedTextIsEditedProperty);

        private static void RebuildDocument(System.Windows.Controls.RichTextBox? rtb)
        {
            if (rtb == null) return;
            var text = (string)rtb.GetValue(FormattedTextProperty) ?? string.Empty;
            var isEdited = (bool)rtb.GetValue(FormattedTextIsEditedProperty);

            var doc = new FlowDocument { PagePadding = new Thickness(0), Background = Brushes.Transparent };
            var p = new Paragraph { Margin = new Thickness(0) };

            if (!string.IsNullOrEmpty(text))
            {
                var regex = new Regex(@"(@[a-zA-Z0-9_\-]+)|(https?:\/\/[^\s]+)");
                var parts = regex.Split(text);
                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;
                    if (part.StartsWith("@") && part.Length > 1)
                    {
                        p.Inlines.Add(new Run(part)
                        {
                            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E3B341")),
                            FontWeight = FontWeights.Bold
                        });
                    }
                    else if (part.StartsWith("http://") || part.StartsWith("https://"))
                    {
                        var hl = new Hyperlink(new Run(part))
                        {
                            NavigateUri = new Uri(part),
                            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#58A6FF")),
                            TextDecorations = TextDecorations.Underline
                        };
                        hl.RequestNavigate += (s, e) =>
                        {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                            e.Handled = true;
                        };
                        p.Inlines.Add(hl);
                    }
                    else
                    {
                        p.Inlines.Add(new Run(part));
                    }
                }
            }

            if (isEdited)
            {
                p.Inlines.Add(new Run(" (edited)")
                {
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6E7681")),
                    FontStyle = FontStyles.Italic,
                    FontSize = 11
                });
            }

            doc.Blocks.Add(p);
            rtb.Document = doc;
        }
    }
}
