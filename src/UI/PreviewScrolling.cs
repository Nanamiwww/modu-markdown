using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace CodexMarkdownReader.UI
{
    /// <summary>
    /// Scrolls the preview to an element. Positions come from TextPointer.GetCharacterRect in pixels of the viewer,
    /// while the scroll offsets of a zoomed document are in unzoomed units: the distance is converted with the ratio
    /// between the two, then corrected once from the movement actually observed.
    /// </summary>
    internal static class PreviewScrolling
    {
        /// <summary>Top of the element in pixels from the top of the visible area, or NaN when it has no layout yet.</summary>
        public static double ViewTop(TextElement element)
        {
            try
            {
                Rect rect = element.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                return rect.IsEmpty ? Double.NaN : rect.Top;
            }
            catch (Exception)
            {
                return Double.NaN;
            }
        }

        /// <summary>Scroll offset units per pixel of the visible area: 1, or 1 / zoom when the offsets are unzoomed.</summary>
        public static double UnitsPerPixel(ScrollViewer scroll)
        {
            if (scroll == null || scroll.ViewportHeight <= 0) return 1;
            FrameworkElement presenter = null;
            try { presenter = scroll.Template == null ? null : scroll.Template.FindName("PART_ScrollContentPresenter", scroll) as FrameworkElement; }
            catch (Exception) { presenter = null; }
            if (presenter == null) presenter = MainWindow.FindVisualChild<ScrollContentPresenter>(scroll);
            double pixels = presenter != null && presenter.ActualHeight > 0 ? presenter.ActualHeight : scroll.ActualHeight;
            if (pixels <= 0) return 1;
            double units = scroll.ViewportHeight / pixels;
            return units > 0.05 && units < 20 ? units : 1;
        }

        /// <summary>Height of the visible area in pixels.</summary>
        public static double ViewportPixels(ScrollViewer scroll)
        {
            return scroll == null ? 0 : scroll.ViewportHeight / UnitsPerPixel(scroll);
        }

        /// <summary>Puts the element <paramref name="margin"/> pixels below the top of the visible area, as far as the document allows.</summary>
        public static void ScrollToTop(ScrollViewer scroll, TextElement element, double margin)
        {
            if (scroll == null || element == null) return;
            double top = ViewTop(element);
            if (Double.IsNaN(top))
            {
                element.BringIntoView();
                return;
            }
            double units = UnitsPerPixel(scroll);
            double before = scroll.VerticalOffset;
            if (Math.Abs(top - margin) < 2) return;
            scroll.ScrollToVerticalOffset(Clamp(scroll, before + (top - margin) * units));
            scroll.UpdateLayout();
            double after = ViewTop(element);
            double moved = scroll.VerticalOffset - before;
            if (Double.IsNaN(after) || Math.Abs(after - margin) < 2) return;
            double target;
            if (Math.Abs(moved) > 0.5)
            {
                double pixelsPerUnit = (top - after) / moved;
                // Positions that do not follow the scroll are measured from the start of the document.
                target = pixelsPerUnit > 0.05 ? scroll.VerticalOffset + (after - margin) / pixelsPerUnit : (after - margin) * units;
            }
            else target = scroll.VerticalOffset + (after - margin) * units;
            target = Clamp(scroll, target);
            if (Math.Abs(target - scroll.VerticalOffset) < 0.5) return;
            scroll.ScrollToVerticalOffset(target);
            scroll.UpdateLayout();
        }

        /// <summary>Scrolls by the pixel distance <paramref name="pixels"/> (positive: further down).</summary>
        public static void ScrollByPixels(ScrollViewer scroll, double pixels)
        {
            if (scroll == null) return;
            scroll.ScrollToVerticalOffset(Clamp(scroll, scroll.VerticalOffset + pixels * UnitsPerPixel(scroll)));
        }

        private static double Clamp(ScrollViewer scroll, double offset)
        {
            double max = Math.Max(0, scroll.ScrollableHeight);
            return Math.Max(0, Math.Min(offset, max));
        }
    }
}
