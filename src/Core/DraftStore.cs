using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CodexMarkdownReader.Core
{
    /// <summary>An unsaved-changes snapshot found on disk that no running window owns.</summary>
    public sealed class DraftInfo : IDisposable
    {
        public string Id;
        public string DraftFile;
        public string DocumentPath;      // may be empty for an untitled document
        public DateTime SavedUtc;
        public string Text;
        public TextFormat Format;        // includes the read encoding and the lossy-decode flag
        /// <summary>Hash of the file version the edits were based on; null when unknown (drafts from v2.0.1 and earlier).</summary>
        public string BaseHash;
        internal FileStream Claim;       // held while the user decides, so another instance cannot take it too
        internal string LockFile;

        public string DisplayName
        {
            get { return String.IsNullOrEmpty(DocumentPath) ? "未命名文档" : Path.GetFileName(DocumentPath); }
        }

        public void Discard()
        {
            try { if (File.Exists(DraftFile)) File.Delete(DraftFile); } catch { }
            Dispose();
        }

        public void Dispose()
        {
            if (Claim != null)
            {
                try { Claim.Dispose(); } catch { }
                Claim = null;
            }
            try { if (LockFile != null && File.Exists(LockFile)) File.Delete(LockFile); } catch { }
        }
    }

    /// <summary>
    /// Each window writes its own draft file and holds a lock file while it runs. After a crash
    /// the lock is released, so the draft is recognised as orphaned and offered for recovery.
    /// </summary>
    public sealed class DraftSession : IDisposable
    {
        private const string Header = "CODEX-MD-DRAFT-2";
        private readonly string id;
        private FileStream lockStream;

        public DraftSession()
        {
            id = Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(DraftDirectory);
                lockStream = new FileStream(LockPath(id), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch { lockStream = null; }
        }

        public string Id { get { return id; } }

        public static string DraftDirectory { get { return Path.Combine(AppSettings.DataDirectory, "drafts"); } }
        public string DraftPath { get { return Path.Combine(DraftDirectory, id + ".md~"); } }
        private static string LockPath(string draftId) { return Path.Combine(DraftDirectory, draftId + ".lock"); }

        public bool Save(string documentPath, string text, TextFormat format)
        {
            return Save(documentPath, text, format, null);
        }

        /// <summary>
        /// Returns false when the draft could not be written (disk full, no permission, file in use).
        /// <paramref name="baseHash"/> is the file version the edits are based on, so that saving after a recovery
        /// still notices changes other programs made in the meantime. Older versions ignore the extra fields.
        /// </summary>
        public bool Save(string documentPath, string text, TextFormat format, string baseHash)
        {
            try
            {
                Directory.CreateDirectory(DraftDirectory);
                StringBuilder builder = new StringBuilder();
                builder.Append(Header).Append('\n');
                builder.Append("path=").Append(AppSettings.Encode(documentPath ?? "")).Append('\n');
                builder.Append("saved=").Append(DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
                TextFormat f = format ?? TextFormat.Default();
                builder.Append("format=").Append(f.CodePage.ToString(CultureInfo.InvariantCulture)).Append('|').Append(f.Bom ? "1" : "0").Append('|').Append(((int)f.LineEnding).ToString(CultureInfo.InvariantCulture)).Append('\n');
                if (f.ReadCodePage != 0) builder.Append("read=").Append(f.ReadCodePage.ToString(CultureInfo.InvariantCulture)).Append('\n');
                if (f.LossyDecode) builder.Append("lossy=1\n");
                if (!String.IsNullOrEmpty(baseHash)) builder.Append("base=").Append(baseHash).Append('\n');
                builder.Append("---\n");
                builder.Append(text ?? "");
                string temporary = DraftPath + ".tmp";
                File.WriteAllText(temporary, builder.ToString(), new UTF8Encoding(false));
                if (File.Exists(DraftPath))
                {
                    try { File.Replace(temporary, DraftPath, null, true); }
                    catch { File.Copy(temporary, DraftPath, true); File.Delete(temporary); }
                }
                else File.Move(temporary, DraftPath);
                return true;
            }
            catch { return false; }
        }

        public void Delete()
        {
            try { if (File.Exists(DraftPath)) File.Delete(DraftPath); } catch { }
        }

        public void Dispose()
        {
            if (lockStream != null)
            {
                try { lockStream.Dispose(); } catch { }
                lockStream = null;
            }
        }

        /// <summary>Returns drafts left behind by windows that are no longer running, claimed for this caller.</summary>
        public static List<DraftInfo> ClaimOrphans(string ownId)
        {
            List<DraftInfo> result = new List<DraftInfo>();
            try
            {
                string legacy = Path.Combine(AppSettings.DataDirectory, "draft.md~");
                if (File.Exists(legacy))
                {
                    DraftInfo old = ReadLegacy(legacy);
                    if (old != null)
                    {
                        FileStream claim = TryClaim(Path.Combine(AppSettings.DataDirectory, "draft.lock"));
                        if (claim != null) { old.Claim = claim; old.LockFile = Path.Combine(AppSettings.DataDirectory, "draft.lock"); result.Add(old); }
                    }
                }
                if (!Directory.Exists(DraftDirectory)) return result;
                foreach (string file in Directory.GetFiles(DraftDirectory, "*.md~"))
                {
                    string draftId = Path.GetFileNameWithoutExtension(file);
                    if (String.Equals(draftId, ownId, StringComparison.OrdinalIgnoreCase)) continue;
                    string lockFile = LockPath(draftId);
                    FileStream claim = TryClaim(lockFile);
                    if (claim == null) continue;   // the owning window is still running
                    DraftInfo info = Read(file);
                    if (info == null) { claim.Dispose(); continue; }
                    info.Claim = claim;
                    info.LockFile = lockFile;
                    result.Add(info);
                }
            }
            catch { }
            result.Sort(delegate(DraftInfo a, DraftInfo b) { return b.SavedUtc.CompareTo(a.SavedUtc); });
            return result;
        }

        private static FileStream TryClaim(string lockFile)
        {
            try { return new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch { return null; }
        }

        private static DraftInfo Read(string file)
        {
            try
            {
                string data = File.ReadAllText(file, Encoding.UTF8);
                int separator = data.IndexOf("\n---\n", StringComparison.Ordinal);
                if (!data.StartsWith(Header + "\n", StringComparison.Ordinal) || separator < 0) return null;
                DraftInfo info = new DraftInfo();
                info.Id = Path.GetFileNameWithoutExtension(file);
                info.DraftFile = file;
                info.Format = TextFormat.Default();
                info.SavedUtc = File.GetLastWriteTimeUtc(file);
                foreach (string line in data.Substring(0, separator).Split('\n'))
                {
                    int p = line.IndexOf('=');
                    if (p <= 0) continue;
                    string key = line.Substring(0, p), value = line.Substring(p + 1);
                    if (key == "path") info.DocumentPath = AppSettings.Decode(value);
                    else if (key == "saved")
                    {
                        long ticks;
                        if (Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) && ticks > 0) info.SavedUtc = new DateTime(ticks, DateTimeKind.Utc);
                    }
                    else if (key == "format")
                    {
                        string[] parts = value.Split('|');
                        int codePage, ending;
                        if (parts.Length == 3 && Int32.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out codePage) &&
                            Int32.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ending))
                        {
                            info.Format.CodePage = codePage;
                            info.Format.Bom = parts[1] == "1";
                            info.Format.LineEnding = (LineEnding)Math.Max(0, Math.Min(2, ending));
                        }
                    }
                    else if (key == "read")
                    {
                        int codePage;
                        if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out codePage) && codePage > 0) info.Format.ReadCodePage = codePage;
                    }
                    else if (key == "lossy") info.Format.LossyDecode = value == "1";
                    else if (key == "base" && value.Length > 0) info.BaseHash = value;
                }
                info.Text = TextFileCodec.NormalizeToCrLf(data.Substring(separator + 5));
                if (info.Text.Length == 0 && String.IsNullOrEmpty(info.DocumentPath)) { try { File.Delete(file); } catch { } return null; }
                return info;
            }
            catch { return null; }
        }

        /// <summary>v1.11 and earlier kept a single shared draft.md~ file.</summary>
        private static DraftInfo ReadLegacy(string file)
        {
            try
            {
                string data = File.ReadAllText(file, Encoding.UTF8);
                int a = data.IndexOf('\n');
                int b = a < 0 ? -1 : data.IndexOf('\n', a + 1);
                if (a < 0 || b < 0 || data.Substring(0, a).Trim() != "CODEX-MD-DRAFT-1") return null;
                DraftInfo info = new DraftInfo();
                info.Id = "legacy";
                info.DraftFile = file;
                info.DocumentPath = AppSettings.Decode(data.Substring(a + 1, b - a - 1).Trim());
                info.Text = TextFileCodec.NormalizeToCrLf(data.Substring(b + 1));
                info.SavedUtc = File.GetLastWriteTimeUtc(file);
                info.Format = TextFormat.Default();
                if (info.Text.Length == 0) return null;
                return info;
            }
            catch { return null; }
        }
    }
}
