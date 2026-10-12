using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace CodexMarkdownReader.UI
{
    public enum ThemeMode { System, Light, Dark }

    internal static class ReaderTypography
    {
        // Set both language and explicit CJK fallbacks. WPF's default en-US
        // fallback can mix Japanese and Traditional Chinese faces in one line.
        public static readonly XmlLanguage Language = XmlLanguage.GetLanguage("zh-CN");
        public static readonly FontFamily Interface = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, Microsoft YaHei, SimSun");
        public static readonly FontFamily Body = new FontFamily("Segoe UI, Microsoft YaHei, Microsoft YaHei UI, SimSun");
        public static readonly FontFamily Heading = new FontFamily("Segoe UI Semibold, Segoe UI, Microsoft YaHei, Microsoft YaHei UI, SimSun");
        public static readonly FontFamily Code = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei, Microsoft YaHei UI, SimSun");
        public static readonly FontFamily Math = new FontFamily("Cambria Math, Microsoft YaHei, Microsoft YaHei UI, SimSun");
    }

    /// <summary>A named set of colors. Brushes are frozen so renderers can share them across threads and windows.</summary>
    public sealed class Palette
    {
        private readonly Dictionary<string, SolidColorBrush> brushes = new Dictionary<string, SolidColorBrush>(StringComparer.Ordinal);
        public bool IsDark { get; private set; }
        public bool IsHighContrast { get; private set; }

        private Palette(bool dark, bool highContrast)
        {
            IsDark = dark;
            IsHighContrast = highContrast;
        }

        public IEnumerable<string> Keys { get { return brushes.Keys; } }

        public Brush this[string key]
        {
            get
            {
                SolidColorBrush brush;
                return brushes.TryGetValue(key, out brush) ? brush : Brushes.Magenta;
            }
        }

        public Color ColorOf(string key)
        {
            SolidColorBrush brush;
            return brushes.TryGetValue(key, out brush) ? brush.Color : Colors.Magenta;
        }

        private void Set(string key, string hex)
        {
            Set(key, (Color)ColorConverter.ConvertFromString(hex));
        }

        private void Set(string key, Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            brushes[key] = brush;
        }

        public static Palette Create(bool dark)
        {
            Palette p = new Palette(dark, false);
            string[,] table = dark ? DarkColors : LightColors;
            for (int i = 0; i < table.GetLength(0); i++) p.Set(table[i, 0], table[i, 1]);
            return p;
        }

        public static Palette CreateHighContrast()
        {
            Color window = SystemColors.WindowColor;
            bool dark = (window.R * 0.299 + window.G * 0.587 + window.B * 0.114) < 128;
            Palette p = Create(dark);
            p.IsHighContrast = true;
            Color text = SystemColors.WindowTextColor;
            Color highlight = SystemColors.HighlightColor;
            Color highlightText = SystemColors.HighlightTextColor;
            Color gray = SystemColors.GrayTextColor;
            Color hot = SystemColors.HotTrackColor;
            foreach (string key in new string[] { "WindowBg", "ScrollTrack", "TitleBar", "Surface", "Surface2", "SegmentSelected", "TableHeaderBg", "TableStripeBg", "Sidebar", "EditorBg", "PreviewBg", "CodeBg", "InlineCodeBg", "QuoteBg", "MenuBg", "InfoBg", "WarningBg", "KbdBg", "DropOverlayBg" })
                p.Set(key, window);
            foreach (string key in new string[] { "Border", "BorderStrong", "WindowBorder", "CodeBorder", "QuoteBar", "MenuBorder", "InfoBorder", "WarningBorder", "KbdBorder", "Splitter", "ScrollThumb", "Text", "WarningText" })
                p.Set(key, text);
            p.Set("Muted", text);
            p.Set("Faint", gray);
            foreach (string key in new string[] { "Accent", "AccentHover", "Hover", "Pressed", "AccentSoft", "Selection", "ScrollThumbHover", "ScrollThumbActive", "FindMatch", "FindCurrent", "MarkBg" })
                p.Set(key, highlight);
            p.Set("AccentText", highlightText);
            p.Set("Link", hot);
            return p;
        }

        private static readonly string[,] LightColors =
        {
            { "WindowBg", "#F3F4F6" }, { "TitleBar", "#F7F8FA" }, { "Surface", "#FFFFFF" }, { "Surface2", "#EEF0F3" },
            { "Sidebar", "#F7F8FA" }, { "EditorBg", "#FBFBFC" }, { "PreviewBg", "#FFFFFF" }, { "Border", "#E3E6EA" },
            { "BorderStrong", "#CBD0D6" }, { "WindowBorder", "#B8BEC7" }, { "Text", "#1F2328" }, { "Muted", "#5E6772" },
            { "Faint", "#8A939D" }, { "Accent", "#16866A" }, { "AccentHover", "#117358" }, { "AccentText", "#FFFFFF" },
            { "AccentSoft", "#E2F2EC" }, { "Hover", "#E9ECEF" }, { "Pressed", "#DDE1E6" }, { "Link", "#0A7A5F" },
            { "CodeBg", "#F6F7F9" }, { "CodeBorder", "#E3E6EA" }, { "InlineCodeBg", "#EEF0F2" }, { "QuoteBg", "#F6F8F9" },
            { "QuoteBar", "#CFD6DD" }, { "Selection", "#553AAE91" }, { "Splitter", "#E3E6EA" }, { "DropOverlayBg", "#F2FFFFFF" },
            { "MenuBg", "#FFFFFF" }, { "MenuBorder", "#D8DDE3" }, { "Shadow", "#40000000" }, { "ScrollThumb", "#858D97" },
            { "ScrollThumbHover", "#6B737D" }, { "ScrollThumbActive", "#555D67" }, { "ScrollTrack", "#EEF0F3" }, { "InfoBg", "#EAF6F1" }, { "InfoBorder", "#BCE2D4" }, { "WarningBg", "#FFF6DE" },
            { "WarningBorder", "#EED28A" }, { "WarningText", "#6B4E00" }, { "Danger", "#C42B1C" }, { "CloseHover", "#C42B1C" },
            { "MarkBg", "#FFF0A0" }, { "KbdBg", "#F6F8FA" }, { "KbdBorder", "#CFD6DD" }, { "AlertNote", "#0969DA" },
            { "AlertTip", "#1A7F37" }, { "AlertImportant", "#8250DF" }, { "AlertWarning", "#9A6700" }, { "AlertCaution", "#CF222E" },
            { "FindMatch", "#80FFD54F" }, { "HeadingRule", "#E6E9ED" }, { "TableHeaderBg", "#F4F6F8" }, { "TableStripeBg", "#FAFBFC" },
            { "SegmentSelected", "#FFFFFF" }, { "OnAccent", "#FFFFFF" }, { "FindCurrent", "#FFB84D" }
        };

        private static readonly string[,] DarkColors =
        {
            { "WindowBg", "#0D0F12" }, { "TitleBar", "#15181C" }, { "Surface", "#1A1D22" }, { "Surface2", "#20242A" },
            { "Sidebar", "#13161A" }, { "EditorBg", "#111418" }, { "PreviewBg", "#0F1215" }, { "Border", "#262B32" },
            { "BorderStrong", "#373E47" }, { "WindowBorder", "#39414B" }, { "Text", "#E6E8EB" }, { "Muted", "#9BA4AE" },
            { "Faint", "#6D7681" }, { "Accent", "#3AAE8C" }, { "AccentHover", "#4DC19F" }, { "AccentText", "#08140F" },
            { "AccentSoft", "#163029" }, { "Hover", "#23282E" }, { "Pressed", "#2C3239" }, { "Link", "#6ACBAE" },
            { "CodeBg", "#161A1F" }, { "CodeBorder", "#262B32" }, { "InlineCodeBg", "#23282F" }, { "QuoteBg", "#14191B" },
            { "QuoteBar", "#3A424B" }, { "Selection", "#664F8FB0" }, { "Splitter", "#1F242A" }, { "DropOverlayBg", "#F21A1E23" },
            { "MenuBg", "#1F2328" }, { "MenuBorder", "#343B43" }, { "Shadow", "#80000000" }, { "ScrollThumb", "#66707B" },
            { "ScrollThumbHover", "#828C97" }, { "ScrollThumbActive", "#9CA5AF" }, { "ScrollTrack", "#181C21" }, { "InfoBg", "#11241E" }, { "InfoBorder", "#1F4A3C" }, { "WarningBg", "#2A2412" },
            { "WarningBorder", "#5C4B1A" }, { "WarningText", "#E8C66A" }, { "Danger", "#F1707B" }, { "CloseHover", "#C42B1C" },
            { "MarkBg", "#5C4F12" }, { "KbdBg", "#22272E" }, { "KbdBorder", "#3A424B" }, { "AlertNote", "#4493F8" },
            { "AlertTip", "#3FB950" }, { "AlertImportant", "#AB7DF8" }, { "AlertWarning", "#D29922" }, { "AlertCaution", "#F85149" },
            { "FindMatch", "#80D29922" }, { "HeadingRule", "#262B32" }, { "TableHeaderBg", "#181C21" }, { "TableStripeBg", "#12161A" },
            { "SegmentSelected", "#2E343C" }, { "OnAccent", "#FFFFFF" }, { "FindCurrent", "#B7791F" }
        };
    }

    internal static class ThemeManager
    {
        public static bool SystemUsesDarkTheme()
        {
            try
            {
                object raw = Registry.GetValue("HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "AppsUseLightTheme", 1);
                return Convert.ToInt32(raw, CultureInfo.InvariantCulture) == 0;
            }
            catch { return false; }
        }

        public static Palette Resolve(ThemeMode mode)
        {
            if (SystemParameters.HighContrast) return Palette.CreateHighContrast();
            bool dark = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemUsesDarkTheme());
            return Palette.Create(dark);
        }

        public static void ApplyTo(ResourceDictionary resources, Palette palette)
        {
            foreach (string key in palette.Keys)
            {
                resources[key] = palette[key];
                resources[key + "Color"] = palette.ColorOf(key);
            }
        }
    }
}
