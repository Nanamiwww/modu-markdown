using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CodexMarkdownReader.UI;

namespace CodexMarkdownReader.Core
{
    public enum ViewMode { Split, Source, Preview }
    public enum SidebarTab { Outline, Recent }

    /// <summary>Per-user settings stored as a small UTF-8 key=value file. Unknown keys are ignored.</summary>
    public sealed class AppSettings
    {
        public const int MaxRecentFiles = 15;

        public ThemeMode Theme = ThemeMode.System;
        public bool SyncScroll = true;
        public ViewMode View = ViewMode.Split;
        public bool SidebarVisible = true;
        public SidebarTab Sidebar = SidebarTab.Outline;
        public double SidebarWidth = 248;
        public double Width = 1320;
        public double Height = 840;
        public double Left = Double.NaN;
        public double Top = Double.NaN;
        public double SplitRatio = 0.5;
        public bool Maximized;
        public bool SuppressDefaultAppReminder;
        public bool OpenLastFileOnStartup = true;
        public int Zoom = 100;
        public int ReadingWidth = 860;
        public string LastFile = "";
        public string LastDirectory = "";
        public List<string> RecentFiles = new List<string>();

        // The in-process regression harness redirects settings without touching a user's profile.
        internal static string TestDataDirectory = null;
        public static string DataDirectory { get { return TestDataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMarkdownReader"); } }
        public static string SettingsPath { get { return Path.Combine(DataDirectory, "settings.ini"); } }

        public static AppSettings Load()
        {
            AppSettings value = new AppSettings();
            try
            {
                if (File.Exists(SettingsPath)) value.Apply(File.ReadAllLines(SettingsPath, Encoding.UTF8));
            }
            catch { }
            value.Normalize();
            return value;
        }

        private void Apply(string[] lines)
        {
            bool legacyHideSource = false, sawView = false;
            foreach (string line in lines)
            {
                int p = line.IndexOf('=');
                if (p <= 0) continue;
                string key = line.Substring(0, p).Trim();
                string raw = line.Substring(p + 1).Trim();
                double number;
                bool flag;
                int integer;
                switch (key)
                {
                    case "Theme": { ThemeMode mode; if (Enum.TryParse<ThemeMode>(raw, true, out mode)) Theme = mode; break; }
                    case "View": { ViewMode mode; if (Enum.TryParse<ViewMode>(raw, true, out mode)) { View = mode; sawView = true; } break; }
                    case "Sidebar": { SidebarTab tab; if (Enum.TryParse<SidebarTab>(raw, true, out tab)) Sidebar = tab; break; }
                    case "SyncScroll": if (Boolean.TryParse(raw, out flag)) SyncScroll = flag; break;
                    case "HideSource": if (Boolean.TryParse(raw, out flag)) legacyHideSource = flag; break;
                    case "SidebarVisible": if (Boolean.TryParse(raw, out flag)) SidebarVisible = flag; break;
                    case "SidebarWidth": if (TryDouble(raw, out number)) SidebarWidth = number; break;
                    case "Width": if (TryDouble(raw, out number)) Width = number; break;
                    case "Height": if (TryDouble(raw, out number)) Height = number; break;
                    case "Left": if (TryDouble(raw, out number)) Left = number; break;
                    case "Top": if (TryDouble(raw, out number)) Top = number; break;
                    case "SplitRatio": if (TryDouble(raw, out number)) SplitRatio = number; break;
                    case "Maximized": if (Boolean.TryParse(raw, out flag)) Maximized = flag; break;
                    case "SuppressDefaultAppReminder": if (Boolean.TryParse(raw, out flag)) SuppressDefaultAppReminder = flag; break;
                    case "OpenLastFileOnStartup": if (Boolean.TryParse(raw, out flag)) OpenLastFileOnStartup = flag; break;
                    case "Zoom": if (Int32.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) Zoom = integer; break;
                    case "ReadingWidth": if (Int32.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) ReadingWidth = integer; break;
                    case "LastFile": LastFile = Decode(raw); break;
                    case "LastDirectory": LastDirectory = Decode(raw); break;
                    case "Recent": { string path = Decode(raw); if (path.Length > 0) RecentFiles.Add(path); break; }
                }
            }
            // v1.11 and earlier stored "HideSource"; map it onto the new view modes.
            if (!sawView && legacyHideSource) View = ViewMode.Preview;
        }

        private static bool TryDouble(string raw, out double value)
        {
            return Double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private void Normalize()
        {
            Width = Clamp(Width, 760, 4000, 1320);
            Height = Clamp(Height, 520, 3000, 840);
            SplitRatio = Clamp(SplitRatio, 0.18, 0.82, 0.5);
            SidebarWidth = Clamp(SidebarWidth, 180, 480, 248);
            if (!IsFinite(Left) || !IsFinite(Top)) { Left = Double.NaN; Top = Double.NaN; }
            Zoom = Math.Max(50, Math.Min(250, Zoom));
            if (ReadingWidth != 0) ReadingWidth = Math.Max(560, Math.Min(1600, ReadingWidth));
            List<string> unique = new List<string>();
            foreach (string path in RecentFiles)
            {
                if (String.IsNullOrWhiteSpace(path)) continue;
                bool exists = false;
                foreach (string item in unique) if (String.Equals(item, path, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                if (!exists) unique.Add(path);
                if (unique.Count >= MaxRecentFiles) break;
            }
            RecentFiles = unique;
        }

        public void AddRecent(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return;
            RecentFiles.RemoveAll(delegate(string item) { return String.Equals(item, path, StringComparison.OrdinalIgnoreCase); });
            RecentFiles.Insert(0, path);
            if (RecentFiles.Count > MaxRecentFiles) RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        }

        public void RemoveRecent(string path)
        {
            RecentFiles.RemoveAll(delegate(string item) { return String.Equals(item, path, StringComparison.OrdinalIgnoreCase); });
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                List<string> lines = new List<string>();
                lines.Add("Theme=" + Theme.ToString());
                lines.Add("View=" + View.ToString());
                lines.Add("HideSource=" + (View == ViewMode.Preview).ToString());
                lines.Add("SyncScroll=" + SyncScroll.ToString());
                lines.Add("SidebarVisible=" + SidebarVisible.ToString());
                lines.Add("Sidebar=" + Sidebar.ToString());
                lines.Add("SidebarWidth=" + SidebarWidth.ToString(CultureInfo.InvariantCulture));
                lines.Add("Width=" + Width.ToString(CultureInfo.InvariantCulture));
                lines.Add("Height=" + Height.ToString(CultureInfo.InvariantCulture));
                lines.Add("Left=" + Left.ToString(CultureInfo.InvariantCulture));
                lines.Add("Top=" + Top.ToString(CultureInfo.InvariantCulture));
                lines.Add("SplitRatio=" + SplitRatio.ToString(CultureInfo.InvariantCulture));
                lines.Add("Maximized=" + Maximized.ToString());
                lines.Add("SuppressDefaultAppReminder=" + SuppressDefaultAppReminder.ToString());
                lines.Add("OpenLastFileOnStartup=" + OpenLastFileOnStartup.ToString());
                lines.Add("Zoom=" + Zoom.ToString(CultureInfo.InvariantCulture));
                lines.Add("ReadingWidth=" + ReadingWidth.ToString(CultureInfo.InvariantCulture));
                lines.Add("LastFile=" + Encode(LastFile));
                lines.Add("LastDirectory=" + Encode(LastDirectory));
                foreach (string path in RecentFiles) lines.Add("Recent=" + Encode(path));
                string temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllLines(temporary, lines.ToArray(), new UTF8Encoding(false));
                if (File.Exists(SettingsPath))
                {
                    try { File.Replace(temporary, SettingsPath, null); }
                    catch { File.Copy(temporary, SettingsPath, true); File.Delete(temporary); }
                }
                else File.Move(temporary, SettingsPath);
            }
            catch { }
        }

        internal static string Encode(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")); }
        internal static string Decode(string s) { try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); } catch { return ""; } }
        private static bool IsFinite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static double Clamp(double value, double min, double max, double fallback) { return IsFinite(value) ? Math.Max(min, Math.Min(max, value)) : fallback; }
    }
}
