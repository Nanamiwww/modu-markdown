using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CodexMarkdownReader.Core;
using CodexMarkdownReader.Rendering;
using CodexMarkdownReader.UI;
using Microsoft.Win32;

[assembly: AssemblyTitle("墨读 Markdown")]
[assembly: AssemblyDescription("简洁、离线、可携带的原生 Windows Markdown 阅读器")]
[assembly: AssemblyCompany("Codex")]
[assembly: AssemblyProduct("墨读 Markdown")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]
[assembly: AssemblyInformationalVersion("2.1")]
[assembly: ComVisible(false)]

namespace CodexMarkdownReader
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            args = args ?? new string[0];
            bool selfTest = args.Length > 0 && String.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase);
            // Used by the self-test: renders one document in a separate process, so input that could crash cannot stop the test run.
            if (args.Length > 1 && String.Equals(args[0], "--render-probe", StringComparison.OrdinalIgnoreCase)) return Tests.SelfTest.RenderProbe(args[1], args.Length > 2 ? args[2] : null);
            try
            {
                if (selfTest) return Tests.SelfTest.Run(args.Length > 1 ? args[1] : null);

                List<string> files = new List<string>();
                List<string> missing = new List<string>();
                foreach (string arg in args)
                {
                    if (String.IsNullOrWhiteSpace(arg) || arg.StartsWith("--", StringComparison.Ordinal)) continue;
                    string full;
                    try { full = Path.GetFullPath(arg); }
                    catch { missing.Add(arg); continue; }
                    if (File.Exists(full)) files.Add(full); else missing.Add(arg);
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnLastWindowClose;
                app.DispatcherUnhandledException += OnUnhandledException;
                ReaderApp.Initialize(app);

                MainWindow first = new MainWindow(files.Count > 0 ? files[0] : null, null, true);
                first.PendingStartupMessage = missing.Count > 0 ? "找不到以下文件：\n" + String.Join("\n", missing.ToArray()) : null;
                first.Show();
                for (int i = 1; i < files.Count; i++) new MainWindow(files[i], null, false).Show();
                return app.Run();
            }
            catch (Exception ex)
            {
                WriteCrashLog(ex);
                if (!selfTest) MessageBox.Show(ex.Message, "墨读 Markdown", MessageBoxButton.OK, MessageBoxImage.Error);
                return 2;
            }
        }

        private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteCrashLog(e.Exception);
            // Keep unsaved work safe before anything else.
            foreach (Window window in Application.Current.Windows)
            {
                MainWindow main = window as MainWindow;
                if (main != null) main.EmergencySaveDraft();
            }
            ThemedDialog.Inform(null, "出现了意外错误",
                "墨读已自动保存未保存的内容，可以继续使用。如果问题反复出现，请把下面这个文件发给开发者：\n" + CrashLogPath + "\n\n" + e.Exception.Message,
                DialogIcon.Warning);
            e.Handled = true;
        }

        public static string CrashLogPath { get { return Path.Combine(AppSettings.DataDirectory, "crash.log"); } }

        private static void WriteCrashLog(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataDirectory);
                File.AppendAllText(CrashLogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  v" + ReaderApp.Version + "\r\n" + ex + "\r\n\r\n", Encoding.UTF8);
            }
            catch { }
        }
    }

    /// <summary>Process-wide state shared by all windows: settings, theme and the image cache.</summary>
    public static class ReaderApp
    {
        public static AppSettings Settings { get; private set; }
        public static Palette Palette { get; private set; }
        public static ImageCache Images { get; private set; }
        public static string Version { get { return "2.1"; } }

        public static void Initialize(Application app)
        {
            Settings = AppSettings.Load();
            Images = new ImageCache();
            app.Resources.MergedDictionaries.Add(StyleResources.Load());
            ApplyTheme();
            // Windows is shutting down: keep unsaved work as drafts instead of blocking the logoff.
            app.SessionEnding += delegate
            {
                foreach (MainWindow window in MainWindows) window.PrepareForSessionEnd();
            };
            SystemEvents.UserPreferenceChanged += delegate(object sender, UserPreferenceChangedEventArgs e)
            {
                if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color && e.Category != UserPreferenceCategory.Accessibility) return;
                Application current = Application.Current;
                if (current == null) return;
                current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { RefreshSystemTheme(); }));
            };
        }

        /// <summary>Only for the self-test: styles and palette without a running Application.</summary>
        internal static void InitializeForTests(AppSettings settings)
        {
            Settings = settings;
            Images = new ImageCache();
            Palette = Palette.Create(false);
        }

        public static void RefreshSystemTheme()
        {
            Palette next = ThemeManager.Resolve(Settings.Theme);
            if (Palette != null && next.IsDark == Palette.IsDark && next.IsHighContrast == Palette.IsHighContrast) return;
            ApplyTheme();
        }

        public static void SetTheme(ThemeMode mode)
        {
            Settings.Theme = mode;
            Settings.Save();
            ApplyTheme();
        }

        public static void ApplyTheme()
        {
            Palette = ThemeManager.Resolve(Settings.Theme);
            Application app = Application.Current;
            if (app == null) return;
            ThemeManager.ApplyTo(app.Resources, Palette);
            foreach (Window window in app.Windows)
            {
                MainWindow main = window as MainWindow;
                if (main != null) main.OnThemeChanged();
            }
        }

        public static IEnumerable<MainWindow> MainWindows
        {
            get
            {
                List<MainWindow> list = new List<MainWindow>();
                if (Application.Current == null) return list;
                foreach (Window window in Application.Current.Windows)
                {
                    MainWindow main = window as MainWindow;
                    if (main != null) list.Add(main);
                }
                return list;
            }
        }

        public static void OpenInNewWindow(string path, DraftInfo draft)
        {
            MainWindow window;
            try { window = new MainWindow(path, draft, false); }
            catch (Exception ex)
            {
                ThemedDialog.Inform(null, "无法打开新窗口", ex.Message, DialogIcon.Warning);
                return;
            }
            window.Show();
            window.Activate();
        }

        /// <summary>Applies the app-wide view preferences (sidebar, zoom, reading width) to every other window.</summary>
        public static void ApplySharedSettings(MainWindow origin)
        {
            foreach (MainWindow window in MainWindows) if (window != origin) window.ApplySharedSettings();
        }

        public static void NotifyRecentChanged()
        {
            foreach (MainWindow window in MainWindows) window.RefreshRecentLists();
        }
    }
}
