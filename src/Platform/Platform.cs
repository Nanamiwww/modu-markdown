using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace CodexMarkdownReader.Platform
{
internal enum DefaultAssociationState { Default, NotDefault, Unknown }

internal static class DefaultAppIntegration
{
    private const string ProgId = "Codex.MarkdownReader.Markdown.1";
    private const string RegisteredAppName = "墨读 Markdown";
    private const string CapabilitiesPath = "Software\\Codex\\MarkdownReader\\Capabilities";
    private const int AssocStrExecutable = 2;
    private const int AssocFNotruncate = 0x20;
    private const int AssocFVerify = 0x40;
    private const int AssocENoAssociation = unchecked((int)0x80070483);
    private const uint ShcneAssocChanged = 0x08000000;
    private static readonly string[] MarkdownExtensions = { ".md", ".markdown" };

    [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AssocQueryStringW(int flags, int str, string association, string extra, StringBuilder output, ref uint outputLength);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    public static DefaultAssociationState GetMarkdownDefaultState()
    {
        string executable;
        bool noAssociation;
        if (!TryQueryExecutable(".md", out executable, out noAssociation))
            return noAssociation ? DefaultAssociationState.NotDefault : DefaultAssociationState.Unknown;
        return SamePath(executable, CurrentExecutablePath()) ? DefaultAssociationState.Default : DefaultAssociationState.NotDefault;
    }

    public static bool RegisterAndOpenSettings(out string error)
    {
        error = "";
        string executable = CurrentExecutablePath();
        if (String.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            error = "无法确定当前程序位置。请把程序放到稳定文件夹后重试。";
            return false;
        }

        try
        {
            string quotedExecutable = "\"" + executable + "\"";
            using (RegistryKey progId = Registry.CurrentUser.CreateSubKey("Software\\Classes\\" + ProgId))
            {
                if (progId == null) throw new InvalidOperationException();
                progId.SetValue("", "Markdown 文档", RegistryValueKind.String);
                // FriendlyTypeName must be an indirect resource string. This portable
                // single-file build has no native string resource, so the ProgID's
                // default value is the correct display-name source.
                progId.DeleteValue("FriendlyTypeName", false);
            }
            using (RegistryKey icon = Registry.CurrentUser.CreateSubKey("Software\\Classes\\" + ProgId + "\\DefaultIcon"))
            {
                if (icon == null) throw new InvalidOperationException();
                icon.SetValue("", quotedExecutable + ",0", RegistryValueKind.String);
            }
            using (RegistryKey shell = Registry.CurrentUser.CreateSubKey("Software\\Classes\\" + ProgId + "\\shell"))
            {
                if (shell == null) throw new InvalidOperationException();
                shell.SetValue("", "open", RegistryValueKind.String);
            }
            using (RegistryKey command = Registry.CurrentUser.CreateSubKey("Software\\Classes\\" + ProgId + "\\shell\\open\\command"))
            {
                if (command == null) throw new InvalidOperationException();
                command.SetValue("", quotedExecutable + " \"%1\"", RegistryValueKind.String);
            }
            foreach (string extension in MarkdownExtensions)
            {
                using (RegistryKey openWith = Registry.CurrentUser.CreateSubKey("Software\\Classes\\" + extension + "\\OpenWithProgids"))
                {
                    if (openWith == null) throw new InvalidOperationException();
                    openWith.SetValue(ProgId, "", RegistryValueKind.String);
                }
            }
            using (RegistryKey capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
            {
                if (capabilities == null) throw new InvalidOperationException();
                capabilities.SetValue("ApplicationName", RegisteredAppName, RegistryValueKind.String);
                capabilities.SetValue("ApplicationDescription", "本地、离线的 Markdown 阅读与编辑应用", RegistryValueKind.String);
                capabilities.SetValue("ApplicationIcon", quotedExecutable + ",0", RegistryValueKind.String);
            }
            using (RegistryKey associations = Registry.CurrentUser.CreateSubKey(CapabilitiesPath + "\\FileAssociations"))
            {
                if (associations == null) throw new InvalidOperationException();
                foreach (string extension in MarkdownExtensions) associations.SetValue(extension, ProgId, RegistryValueKind.String);
            }
            using (RegistryKey registered = Registry.CurrentUser.CreateSubKey("Software\\RegisteredApplications"))
            {
                if (registered == null) throw new InvalidOperationException();
                registered.SetValue(RegisteredAppName, CapabilitiesPath, RegistryValueKind.String);
            }
            SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception)
        {
            error = "无法注册当前用户的默认应用候选项。请检查系统策略或文件权限后重试。";
            return false;
        }

        if (TryLaunchDefaultAppsSettings()) return true;
        error = "无法打开 Windows 设置。请手动打开“设置 > 应用 > 默认应用”，搜索“墨读 Markdown”或“.md”。";
        return false;
    }

    private static bool TryQueryExecutable(string extension, out string executable, out bool noAssociation)
    {
        executable = null;
        noAssociation = false;
        try
        {
            uint length = 0;
            int result = AssocQueryStringW(AssocFNotruncate | AssocFVerify, AssocStrExecutable, extension, null, null, ref length);
            if (result == AssocENoAssociation) { noAssociation = true; return false; }
            if (length == 0 || length > 32768) return false;
            StringBuilder buffer = new StringBuilder((int)length);
            result = AssocQueryStringW(AssocFNotruncate | AssocFVerify, AssocStrExecutable, extension, null, buffer, ref length);
            if (result == AssocENoAssociation) { noAssociation = true; return false; }
            if (result < 0) return false;
            executable = buffer.ToString().Trim();
            return !String.IsNullOrWhiteSpace(executable);
        }
        catch { return false; }
    }

    private static string CurrentExecutablePath()
    {
        try { return Path.GetFullPath(Assembly.GetEntryAssembly().Location); }
        catch { return ""; }
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            string a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool TryLaunchDefaultAppsSettings()
    {
        string[] targets =
        {
            "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(RegisteredAppName),
            "ms-settings:defaultapps"
        };
        foreach (string target in targets)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
                return true;
            }
            catch { }
        }
        return false;
    }
}

internal static class NativeWindowVisuals
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);

    public static void Apply(IntPtr window, bool dark, bool maximized)
    {
        if (window == IntPtr.Zero) return;
        try
        {
            int renderingEnabled = 2;
            DwmSetWindowAttribute(window, 2, ref renderingEnabled, sizeof(int));
            int darkMode = dark ? 1 : 0;
            DwmSetWindowAttribute(window, 20, ref darkMode, sizeof(int));
            int cornerPreference = maximized ? 1 : 3;
            DwmSetWindowAttribute(window, 33, ref cornerPreference, sizeof(int));
            int borderColor = maximized ? unchecked((int)0xFFFFFFFE) : (dark ? 0x004B4139 : 0x00C7BEB8);
            DwmSetWindowAttribute(window, 34, ref borderColor, sizeof(int));
            Margins margins = new Margins();
            int frame = maximized ? 0 : 1;
            margins.Left = frame; margins.Right = frame; margins.Top = frame; margins.Bottom = frame;
            DwmExtendFrameIntoClientArea(window, ref margins);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}

public static class DragDropSupport
{
    public static bool TryGetReadableMarkdownFile(IDataObject data, out string path)
    {
        return TryGetSupportedMarkdownFile(data, true, out path);
    }

    public static bool TryGetSupportedMarkdownFile(IDataObject data, bool requireExistingFile, out string path)
    {
        path = null;
        if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return false;
        string[] files;
        try { files = data.GetData(DataFormats.FileDrop, true) as string[]; }
        catch { return false; }
        if (files == null || files.Length == 0) return false;
        foreach (string candidate in files)
        {
            if (String.IsNullOrWhiteSpace(candidate) || (requireExistingFile && !File.Exists(candidate))) continue;
            string extension;
            try { extension = Path.GetExtension(candidate).ToLowerInvariant(); }
            catch { continue; }
            if (extension == ".md" || extension == ".markdown" || extension == ".mdown" || extension == ".mkd" || extension == ".txt")
            {
                path = Path.GetFullPath(candidate);
                return true;
            }
        }
        return false;
    }
}
}
