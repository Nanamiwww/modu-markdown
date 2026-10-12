using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexMarkdownReader.Rendering
{
    /// <summary>Renders a formula with the native layout engine to a 2× PNG for HTML export.</summary>
    internal static class MathImageRenderer
    {
        public static MathImage Render(string latex, bool block)
        {
            SolidColorBrush ink = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28));
            ink.Freeze();
            string tag;
            FrameworkElement visual = FlowDocumentRenderer.CreateMathVisual(latex, block, ink, 16, out tag);
            // A formula that cannot be typeset is exported as its source text (selectable), not as a picture of it.
            if (FlowDocumentRenderer.IsMathFallback(visual)) return null;
            visual.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            Size size = visual.DesiredSize;
            if (size.Width < 1 || size.Height < 1 || size.Width > 4000 || size.Height > 2000) return null;
            visual.Arrange(new Rect(size));
            visual.UpdateLayout();
            const double scale = 2.0;
            int width = (int)Math.Ceiling(size.Width * scale);
            int height = (int)Math.Ceiling(size.Height * scale);
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (MemoryStream stream = new MemoryStream())
            {
                encoder.Save(stream);
                MathImage image = new MathImage();
                image.DataUri = "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
                image.Width = size.Width;
                image.Height = size.Height;
                image.Depth = Math.Max(0, FlowDocumentRenderer.MathDepth(visual));
                image.Tag = tag;
                return image;
            }
        }
    }
}
