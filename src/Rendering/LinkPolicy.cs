using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace CodexMarkdownReader.Rendering
{
    public enum LinkKind
    {
        Invalid,
        Missing,
        Blocked,
        Web,
        Mail,
        Anchor,
        MarkdownFile,
        LocalFile,
        LocalDirectory
    }

    public enum FileSafety { Safe, Confirm, Blocked }

    public sealed class LinkTarget
    {
        public LinkKind Kind;
        public string Path;        // local file or directory (full path)
        public string Fragment;    // without '#', already percent-decoded
        public Uri Uri;            // web / mail / local file URI
        public FileSafety Safety;
        public string Original;

        public string Describe()
        {
            switch (Kind)
            {
                case LinkKind.Web:
                case LinkKind.Mail: return Uri.ToString();
                case LinkKind.Anchor: return "跳转到本文：" + Fragment;
                case LinkKind.MarkdownFile: return Path + (String.IsNullOrEmpty(Fragment) ? "" : "#" + Fragment);
                case LinkKind.LocalFile:
                case LinkKind.LocalDirectory: return Path;
                case LinkKind.Missing: return "链接目标不存在：" + Original;
                case LinkKind.Blocked: return "出于安全考虑，不打开此类文件：" + Original;
                default: return Original;
            }
        }
    }

    /// <summary>
    /// Resolves Markdown link destinations and classifies how safe they are to open.
    /// Files that execute code are never opened; unknown types require confirmation.
    /// </summary>
    public static class LinkPolicy
    {
        private static readonly HashSet<string> MarkdownExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".txt"
        };

        private static readonly HashSet<string> BlockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".cmd", ".bat", ".ps1", ".psm1", ".psd1", ".ps1xml", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".wsc", ".ws",
            ".hta", ".scr", ".cpl", ".msi", ".msp", ".mst", ".lnk", ".url", ".scf", ".reg", ".pif", ".application", ".appref-ms",
            ".gadget", ".msc", ".jar", ".py", ".pyw", ".pyc", ".pyo", ".pyz", ".pyzw", ".settingcontent-ms", ".library-ms", ".search-ms",
            ".searchconnector-ms", ".diagcab", ".appx", ".appxbundle", ".msix", ".msixbundle", ".inf", ".chm", ".hlp", ".sct", ".xll",
            ".iqy", ".slk", ".ade", ".adp", ".mde", ".mda", ".accde", ".vb", ".shb", ".shs", ".xbap", ".website", ".rdp", ".sys", ".dll", ".ocx"
        };

        private static readonly HashSet<string> SafeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".txt", ".log", ".csv", ".tsv", ".json", ".jsonl", ".xml", ".yaml", ".yml", ".toml", ".ini",
            ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".svg", ".heic", ".avif",
            ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm",
            // Legacy Office (.doc .xls .ppt), OpenDocument and RTF files can carry macros or embedded objects:
            // they are not listed here, so they are opened only after confirmation.
            ".docx", ".xlsx", ".pptx", ".epub", ".vsdx",
            ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz",
            ".html", ".htm", ".css", ".tex", ".bib", ".ris", ".c", ".h", ".cpp", ".hpp", ".cc", ".cs", ".java", ".kt", ".go", ".rs", ".ts", ".tsx",
            ".jsx", ".vue", ".swift", ".m", ".r", ".jl", ".sql", ".ipynb", ".mat", ".opju", ".opj", ".cif", ".mol", ".sdf", ".xyz", ".pdb", ".dat", ".spe", ".h5", ".nc",
            // Research data: structures, reactions, spectra, quantum-chemistry input/output, sequences, mass spectrometry.
            ".mol2", ".cml", ".rxn", ".smi", ".cdx", ".cdxml", ".jdx", ".dx", ".jcamp", ".mnova", ".spc", ".cube", ".fchk", ".gjf", ".out",
            ".fasta", ".fa", ".gb", ".mzml", ".mzxml", ".pzfx"
        };

        public static FileSafety ClassifyFile(string path)
        {
            string extension;
            try { extension = System.IO.Path.GetExtension(path) ?? ""; }
            catch { return FileSafety.Blocked; }
            if (extension.Length == 0) return FileSafety.Confirm;
            if (BlockedExtensions.Contains(extension)) return FileSafety.Blocked;
            if (SafeExtensions.Contains(extension)) return FileSafety.Safe;
            return FileSafety.Confirm;
        }

        public static bool IsMarkdownPath(string path)
        {
            try { return MarkdownExtensions.Contains(System.IO.Path.GetExtension(path) ?? ""); }
            catch { return false; }
        }

        public static bool IsImagePath(string path)
        {
            string extension;
            try { extension = (System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant(); }
            catch { return false; }
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".gif" || extension == ".bmp" ||
                   extension == ".webp" || extension == ".tif" || extension == ".tiff" || extension == ".ico" || extension == ".jxr" || extension == ".wdp";
        }

        public static LinkTarget Resolve(string href, string baseDirectory)
        {
            LinkTarget target = new LinkTarget();
            target.Original = href ?? "";
            string value = (href ?? "").Trim();
            if (value.StartsWith("<", StringComparison.Ordinal) && value.EndsWith(">", StringComparison.Ordinal)) value = value.Substring(1, value.Length - 2).Trim();
            if (value.Length == 0 || value.Length > 4096 || value.IndexOf('\0') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
            {
                target.Kind = LinkKind.Invalid;
                return target;
            }

            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                target.Kind = LinkKind.Anchor;
                target.Fragment = SafeUnescape(value.Substring(1));
                return target;
            }
            if (value.StartsWith("?", StringComparison.Ordinal)) { target.Kind = LinkKind.Invalid; return target; }

            // Resolve native Windows paths before generic URI parsing. Values such as
            // C:/Documents/file.md can otherwise be mistaken for a URI whose scheme is "c".
            string localPath, fragment;
            bool looksLocal;
            if (TryResolveLocalPath(value, baseDirectory, out localPath, out fragment, out looksLocal))
                return ClassifyLocal(target, localPath, fragment);
            if (looksLocal)
            {
                target.Kind = LinkKind.Missing;
                return target;
            }

            Uri parsed;
            if (Uri.TryCreate(value, UriKind.Absolute, out parsed))
            {
                if (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
                {
                    target.Kind = LinkKind.Web;
                    target.Uri = parsed;
                    return target;
                }
                if (parsed.Scheme == Uri.UriSchemeMailto)
                {
                    target.Kind = LinkKind.Mail;
                    target.Uri = parsed;
                    return target;
                }
                if (parsed.IsFile)
                {
                    try
                    {
                        if (parsed.IsUnc) { target.Kind = LinkKind.Blocked; return target; }
                        // LocalPath is already decoded. Decoding again changes literal
                        // percent sequences in filenames, potentially opening another file.
                        string path = System.IO.Path.GetFullPath(parsed.LocalPath);
                        // Encoded separators can still normalize to a network share; check the final path
                        // before any file-system call.
                        if (IsDevicePath(path)) { target.Kind = LinkKind.Blocked; return target; }
                        string fileFragment = parsed.Fragment.Length > 1 ? SafeUnescape(parsed.Fragment.Substring(1)) : null;
                        if (File.Exists(path) || Directory.Exists(path)) return ClassifyLocal(target, path, fileFragment);
                        target.Kind = LinkKind.Missing;
                        return target;
                    }
                    catch
                    {
                        target.Kind = LinkKind.Invalid;
                        return target;
                    }
                }
                target.Kind = LinkKind.Blocked;
                return target;
            }
            target.Kind = LinkKind.Invalid;
            return target;
        }

        private static LinkTarget ClassifyLocal(LinkTarget target, string path, string fragment)
        {
            target.Path = path;
            target.Fragment = String.IsNullOrEmpty(fragment) ? null : fragment;
            if (IsDevicePath(path)) { target.Kind = LinkKind.Blocked; return target; }
            if (Directory.Exists(path))
            {
                target.Kind = LinkKind.LocalDirectory;
                target.Safety = FileSafety.Safe;
                target.Uri = CreateLocalFileUri(path);
                return target;
            }
            if (!File.Exists(path)) { target.Kind = LinkKind.Missing; return target; }
            target.Safety = ClassifyFile(path);
            if (target.Safety == FileSafety.Blocked) { target.Kind = LinkKind.Blocked; return target; }
            target.Uri = CreateLocalFileUri(path);
            target.Kind = IsMarkdownPath(path) ? LinkKind.MarkdownFile : LinkKind.LocalFile;
            return target;
        }

        /// <summary>Network shares (\\server\share) and device paths (\\?\, \\.\). Never opened, never probed.</summary>
        internal static bool IsDevicePath(string path)
        {
            return String.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);
        }

        private static string SafeUnescape(string value)
        {
            try { return Uri.UnescapeDataString(value ?? ""); }
            catch { return value ?? ""; }
        }

        /// <param name="looksLocal">True when the value is clearly a local path, even if it does not exist.</param>
        private static bool TryResolveLocalPath(string value, string baseDirectory, out string path, out string fragment, out bool looksLocal)
        {
            path = null; fragment = null; looksLocal = false;
            try
            {
                // A literal # or ? denotes a URL fragment/query for a local link.
                // Percent-encoded %23 and %3F remain valid filename characters.
                int suffix = value.IndexOfAny(new char[] { '#', '?' });
                string pathPart = suffix >= 0 ? value.Substring(0, suffix) : value;
                if (suffix >= 0 && value[suffix] == '#') fragment = SafeUnescape(value.Substring(suffix + 1));
                if (Regex.IsMatch(pathPart, @"^[A-Za-z][A-Za-z0-9+.\-]+:") && !Regex.IsMatch(pathPart, @"^[A-Za-z]:[\\/]")) return false;
                string decoded = Uri.UnescapeDataString(pathPart);
                if (decoded.StartsWith(@"\\", StringComparison.Ordinal) || decoded.StartsWith("//", StringComparison.Ordinal)) { looksLocal = true; return false; }
                if (decoded.Length == 0) return false;
                string candidate = null;
                if (Regex.IsMatch(decoded, @"^[A-Za-z]:[\\/]"))
                {
                    looksLocal = true;
                    candidate = decoded.Replace('/', System.IO.Path.DirectorySeparatorChar);
                }
                else if (!String.IsNullOrEmpty(baseDirectory))
                {
                    looksLocal = true;
                    string relative = decoded.Replace('/', System.IO.Path.DirectorySeparatorChar);
                    if (relative.StartsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                    {
                        // "/docs/a.md" means repository root on GitHub; fall back to the document folder.
                        string root = FindRepositoryRoot(baseDirectory) ?? baseDirectory;
                        candidate = System.IO.Path.Combine(root, relative.TrimStart(System.IO.Path.DirectorySeparatorChar));
                    }
                    else candidate = System.IO.Path.Combine(baseDirectory, relative);
                }
                if (String.IsNullOrEmpty(candidate)) return false;
                string full = System.IO.Path.GetFullPath(candidate);
                // ClassifyLocal blocks network and device paths without touching the file system.
                if (IsDevicePath(full)) { path = full; return true; }
                if (!File.Exists(full) && !Directory.Exists(full)) return false;
                path = full;
                return true;
            }
            catch { return false; }
        }

        private static string FindRepositoryRoot(string directory)
        {
            try
            {
                DirectoryInfo current = new DirectoryInfo(directory);
                int guard = 0;
                while (current != null && guard++ < 40)
                {
                    if (Directory.Exists(System.IO.Path.Combine(current.FullName, ".git")) || File.Exists(System.IO.Path.Combine(current.FullName, ".git"))) return current.FullName;
                    current = current.Parent;
                }
            }
            catch { }
            return null;
        }

        public static Uri CreateLocalFileUri(string path)
        {
            string full = System.IO.Path.GetFullPath(path).Replace('\\', '/');
            string[] segments = full.Substring(3).Split('/');
            for (int i = 0; i < segments.Length; i++) segments[i] = Uri.EscapeDataString(segments[i]);
            return new Uri("file:///" + full.Substring(0, 3) + String.Join("/", segments), UriKind.Absolute);
        }

        /// <summary>Re-validates a target immediately before opening it (it may have changed since rendering).</summary>
        public static bool TryGetShellTarget(LinkTarget target, out string shellTarget)
        {
            shellTarget = null;
            if (target == null) return false;
            if (target.Kind == LinkKind.Web || target.Kind == LinkKind.Mail)
            {
                if (target.Uri == null) return false;
                string scheme = target.Uri.Scheme;
                if (scheme != Uri.UriSchemeHttp && scheme != Uri.UriSchemeHttps && scheme != Uri.UriSchemeMailto) return false;
                shellTarget = target.Uri.AbsoluteUri;
                return true;
            }
            if (target.Kind == LinkKind.LocalDirectory || target.Kind == LinkKind.LocalFile || target.Kind == LinkKind.MarkdownFile)
            {
                try
                {
                    string path = System.IO.Path.GetFullPath(target.Path);
                    if (IsDevicePath(path)) return false;
                    if (Directory.Exists(path)) { shellTarget = path; return true; }
                    if (!File.Exists(path) || ClassifyFile(path) == FileSafety.Blocked) return false;
                    shellTarget = path;
                    return true;
                }
                catch { return false; }
            }
            return false;
        }
    }
}
