using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CodexMarkdownReader.UI
{
    /// <summary>
    /// Stroke icons drawn on a 16×16 grid. They inherit the foreground of the control that
    /// hosts them, so hover/disabled/theme colors apply automatically and no icon font is needed.
    /// </summary>
    internal static class Icons
    {
        public const string PanelLeft = "M2.5,3.5 H13.5 V12.5 H2.5 Z M6.5,3.5 V12.5";
        public const string ArrowLeft = "M13,8 H3.5 M7.5,4 L3.5,8 L7.5,12";
        public const string ArrowRight = "M3,8 H12.5 M8.5,4 L12.5,8 L8.5,12";
        public const string Folder = "M2,12.5 V3.5 H6.3 L7.8,5 H14 V12.5 Z";
        public const string Save = "M3,2.5 H11 L13.5,5 V13.5 H2.5 V3 Z M5.5,2.5 V5.5 H10 V2.5 M5,13.5 V9.5 H11 V13.5";
        public const string Search = "M11.5,7 A4.5,4.5 0 1 1 2.5,7 A4.5,4.5 0 1 1 11.5,7 Z M10.3,10.3 L13.8,13.8";
        public const string More = "M3.5,8 L3.6,8 M8,8 L8.1,8 M12.5,8 L12.6,8";
        public const string Source = "M5.5,4.5 L2,8 L5.5,11.5 M10.5,4.5 L14,8 L10.5,11.5 M9,3 L7,13";
        public const string Split = "M2.5,3.5 H13.5 V12.5 H2.5 Z M8,3.5 V12.5";
        public const string Preview = "M1.5,8 C3.5,4.3 12.5,4.3 14.5,8 C12.5,11.7 3.5,11.7 1.5,8 Z M9.8,8 A1.8,1.8 0 1 1 6.2,8 A1.8,1.8 0 1 1 9.8,8 Z";
        public const string Copy = "M5.5,5.5 H13 V13 H5.5 Z M3,10.5 V3 H10.5";
        public const string Check = "M3,8.5 L6.5,12 L13,4.5";
        public const string ChevronRight = "M6,3.5 L10.5,8 L6,12.5";
        public const string ChevronUp = "M3.5,10 L8,5.5 L12.5,10";
        public const string ChevronDown = "M3.5,6 L8,10.5 L12.5,6";
        public const string Close = "M4,4 L12,12 M12,4 L4,12";
        public const string Document = "M4,2.5 H9.5 L12.5,5.5 V13.5 H4 Z M9.5,2.5 V5.5 H12.5";
        public const string NewDocument = "M4,2.5 H9.5 L12.5,5.5 V13.5 H4 Z M9.5,2.5 V5.5 H12.5 M8.25,7.5 V11.5 M6.25,9.5 H10.25";
        public const string Outline = "M2.5,4 H13.5 M4.5,8 H13.5 M6.5,12 H13.5";
        public const string Clock = "M14,8 A6,6 0 1 1 2,8 A6,6 0 1 1 14,8 Z M8,4.5 V8 L10.5,9.5";
        public const string Info = "M14,8 A6,6 0 1 1 2,8 A6,6 0 1 1 14,8 Z M8,7.3 V11 M8,5 L8,5.1";
        public const string Warning = "M8,2.5 L14.2,13 H1.8 Z M8,6.5 V9.3 M8,11.2 L8,11.3";
        public const string Print = "M4.5,6 V2.5 H11.5 V6 M4.5,11.5 H2.5 V6 H13.5 V11.5 H11.5 M4.5,9.5 H11.5 V13.5 H4.5 Z";
        public const string Export = "M8,10 V2.5 M5,5.5 L8,2.5 L11,5.5 M3,9 V13.5 H13 V9";
        public const string Reload = "M13,8 A5,5 0 1 1 11.5,4.5 M11.8,1.8 V4.8 H8.8";
        public const string Sun = "M10.5,8 A2.5,2.5 0 1 1 5.5,8 A2.5,2.5 0 1 1 10.5,8 Z M8,1.5 V3 M8,13 V14.5 M1.5,8 H3 M13,8 H14.5 M3.4,3.4 L4.5,4.5 M11.5,11.5 L12.6,12.6 M3.4,12.6 L4.5,11.5 M11.5,4.5 L12.6,3.4";
        public const string Window = "M2.5,3.5 H13.5 V12.5 H2.5 Z M2.5,6 H13.5";
        public const string Link = "M7,9 L9,7 M6.2,5.3 L7.3,4.2 A2.3,2.3 0 0 1 10.6,4.2 L11.8,5.4 A2.3,2.3 0 0 1 11.8,8.7 L10.7,9.8 M9.8,10.7 L8.7,11.8 A2.3,2.3 0 0 1 5.4,11.8 L4.2,10.6 A2.3,2.3 0 0 1 4.2,7.3 L5.3,6.2";
        public const string Keyboard = "M1.5,4.5 H14.5 V11.5 H1.5 Z M4,7 L4.1,7 M6.5,7 L6.6,7 M9,7 L9.1,7 M11.5,7 L11.6,7 M4.5,9.3 H11.5";
        public const string ZoomIn = "M11.5,7 A4.5,4.5 0 1 1 2.5,7 A4.5,4.5 0 1 1 11.5,7 Z M10.3,10.3 L13.8,13.8 M7,5 V9 M5,7 H9";
        public const string Encoding = "M2.5,12.5 L5.5,3.5 L8.5,12.5 M3.5,9.5 H7.5 M10,6 H14 M12,4 V6 M10.5,12.5 C12.5,11 13.5,9 13.5,6";
        public const string Trash = "M3,4.5 H13 M6,4.5 V2.5 H10 V4.5 M4.5,4.5 L5.2,13.5 H10.8 L11.5,4.5";
        public const string Case = "M1.5,12 L4.5,4 L7.5,12 M2.6,9.2 H6.4 M13.8,8.2 A2.3,2.3 0 1 0 13.8,11.3 M13.8,7 V12";

        private static readonly Dictionary<string, Geometry> Cache = new Dictionary<string, Geometry>(StringComparer.Ordinal);

        public static Geometry GetGeometry(string data)
        {
            lock (Cache)
            {
                Geometry geometry;
                if (!Cache.TryGetValue(data, out geometry))
                {
                    geometry = System.Windows.Media.Geometry.Parse(data);
                    geometry.Freeze();
                    Cache[data] = geometry;
                }
                return geometry;
            }
        }

        /// <summary>Creates an icon whose stroke follows the inherited TextElement.Foreground.</summary>
        public static FrameworkElement Create(string data, double size)
        {
            return Create(data, size, 1.35);
        }

        public static FrameworkElement Create(string data, double size, double thickness)
        {
            Path path = new Path();
            path.Data = GetGeometry(data);
            path.StrokeThickness = thickness;
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = PenLineCap.Round;
            path.StrokeLineJoin = PenLineJoin.Round;
            path.Stretch = Stretch.None;
            path.IsHitTestVisible = false;
            path.SnapsToDevicePixels = false;
            Binding binding = new Binding();
            binding.Path = new PropertyPath(TextElement.ForegroundProperty);
            binding.RelativeSource = RelativeSource.Self;
            path.SetBinding(Shape.StrokeProperty, binding);

            Viewbox box = new Viewbox();
            box.Width = size;
            box.Height = size;
            box.Stretch = Stretch.Uniform;
            box.IsHitTestVisible = false;
            Canvas canvas = new Canvas();
            canvas.Width = 16;
            canvas.Height = 16;
            canvas.Children.Add(path);
            box.Child = canvas;
            return box;
        }

        /// <summary>Crisp 1px caption glyphs on a 12×12 grid for the window buttons.</summary>
        public static FrameworkElement CreateCaption(string data)
        {
            Path path = new Path();
            path.Data = GetGeometry(data);
            path.StrokeThickness = 1.0;
            path.StrokeStartLineCap = PenLineCap.Flat;
            path.StrokeEndLineCap = PenLineCap.Flat;
            path.StrokeLineJoin = PenLineJoin.Miter;
            path.Stretch = Stretch.None;
            path.SnapsToDevicePixels = true;
            path.IsHitTestVisible = false;
            Binding binding = new Binding();
            binding.Path = new PropertyPath(TextElement.ForegroundProperty);
            binding.RelativeSource = RelativeSource.Self;
            path.SetBinding(Shape.StrokeProperty, binding);
            Canvas canvas = new Canvas();
            canvas.Width = 12;
            canvas.Height = 12;
            canvas.SnapsToDevicePixels = true;
            canvas.IsHitTestVisible = false;
            canvas.Children.Add(path);
            Grid host = new Grid();
            host.Width = 16; host.Height = 16;
            host.UseLayoutRounding = true;
            host.IsHitTestVisible = false;
            canvas.HorizontalAlignment = HorizontalAlignment.Center;
            canvas.VerticalAlignment = VerticalAlignment.Center;
            host.Children.Add(canvas);
            return host;
        }

        public const string CaptionMinimize = "M 2,6.5 L 10,6.5";
        public const string CaptionMaximize = "M 2.5,2.5 H 9.5 V 9.5 H 2.5 Z";
        public const string CaptionRestore = "M 4.5,4.5 V 2.5 H 9.5 V 7.5 H 7.5 M 2.5,4.5 H 7.5 V 9.5 H 2.5 Z";
        public const string CaptionClose = "M 3,3 L 9,9 M 9,3 L 3,9";
    }
}
