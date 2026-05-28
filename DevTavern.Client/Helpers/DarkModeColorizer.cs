using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace DevTavern.Client
{
    public class DarkModeColorizer : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(ICSharpCode.AvalonEdit.Document.DocumentLine line)
        {
            ChangeLinePart(line.Offset, line.EndOffset, (element) =>
            {
                if (element.TextRunProperties.ForegroundBrush is SolidColorBrush brush)
                {
                    var c = brush.Color;
                    if (c.R == 201 && c.G == 209 && c.B == 217) return;

                    if (c.R < 30 && c.G < 30 && c.B < 30)
                    {
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(201, 209, 217)));
                    }
                    else
                    {
                        byte r = (byte)Math.Min(255, c.R + 80);
                        byte g = (byte)Math.Min(255, c.G + 80);
                        byte b = (byte)Math.Min(255, c.B + 80);
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(r, g, b)));
                    }
                }
            });
        }
    }
}
