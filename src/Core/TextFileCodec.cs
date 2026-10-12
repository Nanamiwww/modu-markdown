using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CodexMarkdownReader.Core
{
    public enum LineEnding { CrLf, Lf, Cr }

    /// <summary>How a file is stored on disk. Preserved on save so opening and saving never silently converts a file.</summary>
    public sealed class TextFormat
    {
        public int CodePage = 65001;
        public bool Bom;
        public LineEnding LineEnding = LineEnding.CrLf;
        public bool MixedLineEndings;
        public bool DecodedWithFallback;
        /// <summary>Some bytes were not valid in this encoding and were shown as U+FFFD; saving would replace them for good.</summary>
        public bool LossyDecode;
        /// <summary>
        /// Code page for reading this file again (external refresh, reload, draft recovery); 0 = detect it again.
        /// Set when the user picks an encoding, so that choice is not lost the next time the file is read.
        /// </summary>
        public int ReadCodePage;

        public TextFormat Clone()
        {
            TextFormat copy = new TextFormat();
            copy.CodePage = CodePage; copy.Bom = Bom; copy.LineEnding = LineEnding;
            copy.MixedLineEndings = MixedLineEndings; copy.DecodedWithFallback = DecodedWithFallback;
            copy.LossyDecode = LossyDecode;
            copy.ReadCodePage = ReadCodePage;
            return copy;
        }

        /// <summary>UTF-8 and BOM-marked UTF-16 are recognised again when the file is read; other encodings are not.</summary>
        public bool IsSelfIdentifying
        {
            get { return CodePage == 65001 || (Bom && (CodePage == 1200 || CodePage == 1201)); }
        }

        public bool SameStorage(TextFormat other)
        {
            return other != null && other.CodePage == CodePage && other.Bom == Bom && other.LineEnding == LineEnding;
        }

        public string EncodingLabel
        {
            get
            {
                switch (CodePage)
                {
                    case 65001: return Bom ? "UTF-8 BOM" : "UTF-8";
                    case 1200: return "UTF-16 LE";
                    case 1201: return "UTF-16 BE";
                    case 936: return "GBK";
                    case 54936: return "GB18030";
                    case 950: return "Big5";
                    case 932: return "Shift-JIS";
                    case 1252: return "Windows-1252";
                }
                try { return Encoding.GetEncoding(CodePage).WebName.ToUpperInvariant(); }
                catch { return "CP" + CodePage.ToString(); }
            }
        }

        public string LineEndingLabel
        {
            get { return LineEnding == LineEnding.Lf ? "LF" : LineEnding == LineEnding.Cr ? "CR" : "CRLF"; }
        }

        public static TextFormat Default()
        {
            TextFormat format = new TextFormat();
            format.CodePage = 65001;
            format.Bom = false;
            format.LineEnding = LineEnding.CrLf;
            return format;
        }
    }

    public sealed class DecodedText
    {
        public string Text;          // normalized to CRLF for the editor
        public TextFormat Format;
    }

    public sealed class UnencodableTextException : Exception
    {
        public UnencodableTextException(string encoding) : base("部分字符无法用 " + encoding + " 编码保存。") { }
    }

    public static class TextFileCodec
    {
        public static DecodedText Decode(byte[] bytes, int forcedCodePage)
        {
            DecodedText result = new DecodedText();
            TextFormat format = new TextFormat();
            string text;
            bool lossy = false;
            if (forcedCodePage == 0)
            {
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                {
                    format.CodePage = 65001; format.Bom = true;
                    text = DecodeChecked(new UTF8Encoding(false, true), new UTF8Encoding(false, false), bytes, 3, ref lossy);
                }
                else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                {
                    format.CodePage = 1200; format.Bom = true;
                    text = DecodeChecked(new UnicodeEncoding(false, false, true), Encoding.Unicode, bytes, 2, ref lossy);
                }
                else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                {
                    format.CodePage = 1201; format.Bom = true;
                    text = DecodeChecked(new UnicodeEncoding(true, false, true), Encoding.BigEndianUnicode, bytes, 2, ref lossy);
                }
                else
                {
                    try
                    {
                        text = new UTF8Encoding(false, true).GetString(bytes);
                        format.CodePage = 65001;
                    }
                    catch (DecoderFallbackException)
                    {
                        // Not valid UTF-8: most Chinese Windows files are GBK (the ANSI code page).
                        int ansi = Encoding.Default.CodePage;
                        if (ansi == 65001) ansi = 54936;
                        format.CodePage = ansi;
                        format.DecodedWithFallback = true;
                        text = DecodeChecked(StrictEncoding(ansi), Encoding.GetEncoding(ansi), bytes, 0, ref lossy);
                    }
                }
            }
            else
            {
                Encoding encoding = Encoding.GetEncoding(forcedCodePage);
                format.CodePage = forcedCodePage;
                format.ReadCodePage = forcedCodePage;
                int skip = 0;
                byte[] preamble = encoding.GetPreamble();
                if (preamble.Length > 0 && bytes.Length >= preamble.Length)
                {
                    bool match = true;
                    for (int i = 0; i < preamble.Length; i++) if (bytes[i] != preamble[i]) { match = false; break; }
                    if (match) { skip = preamble.Length; format.Bom = true; }
                }
                text = DecodeChecked(StrictEncoding(forcedCodePage), encoding, bytes, skip, ref lossy);
            }
            format.LossyDecode = lossy;

            int crlf = 0, lf = 0, cr = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                    else cr++;
                }
                else if (text[i] == '\n') lf++;
            }
            if (crlf == 0 && lf == 0 && cr == 0) format.LineEnding = LineEnding.CrLf;
            else if (lf >= crlf && lf >= cr) format.LineEnding = LineEnding.Lf;
            else if (cr > crlf) format.LineEnding = LineEnding.Cr;
            else format.LineEnding = LineEnding.CrLf;
            int kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
            format.MixedLineEndings = kinds > 1;

            result.Text = NormalizeToCrLf(text);
            result.Format = format;
            return result;
        }

        /// <summary>
        /// Decodes strictly first. Bytes that are not valid in the encoding are still shown (as U+FFFD),
        /// but the loss is reported so that saving can ask before the original bytes are overwritten.
        /// </summary>
        private static string DecodeChecked(Encoding strict, Encoding lenient, byte[] bytes, int offset, ref bool lossy)
        {
            try { return strict.GetString(bytes, offset, bytes.Length - offset); }
            catch (DecoderFallbackException)
            {
                lossy = true;
                return lenient.GetString(bytes, offset, bytes.Length - offset);
            }
        }

        private static Encoding StrictEncoding(int codePage)
        {
            if (codePage == 65001) return new UTF8Encoding(false, true);
            if (codePage == 1200) return new UnicodeEncoding(false, false, true);
            if (codePage == 1201) return new UnicodeEncoding(true, false, true);
            return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        public static string NormalizeToCrLf(string text)
        {
            if (String.IsNullOrEmpty(text)) return text ?? "";
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
        }

        public static byte[] Encode(string text, TextFormat format)
        {
            string normalized = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            if (format.LineEnding == LineEnding.CrLf) normalized = normalized.Replace("\n", "\r\n");
            else if (format.LineEnding == LineEnding.Cr) normalized = normalized.Replace('\n', '\r');

            Encoding encoding;
            if (format.CodePage == 65001) encoding = new UTF8Encoding(false, true);
            else if (format.CodePage == 1200) encoding = new UnicodeEncoding(false, false, true);
            else if (format.CodePage == 1201) encoding = new UnicodeEncoding(true, false, true);
            else encoding = Encoding.GetEncoding(format.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

            byte[] body;
            try { body = encoding.GetBytes(normalized); }
            catch (EncoderFallbackException) { throw new UnencodableTextException(format.EncodingLabel); }

            byte[] preamble = new byte[0];
            if (format.Bom)
            {
                if (format.CodePage == 65001) preamble = new byte[] { 0xEF, 0xBB, 0xBF };
                else if (format.CodePage == 1200) preamble = new byte[] { 0xFF, 0xFE };
                else if (format.CodePage == 1201) preamble = new byte[] { 0xFE, 0xFF };
            }
            if (preamble.Length == 0) return body;
            byte[] combined = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, combined, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, combined, preamble.Length, body.Length);
            return combined;
        }

        public static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes ?? new byte[0]));
        }

        public static byte[] ReadAllBytesShared(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] buffer = new byte[stream.Length];
                int offset = 0;
                while (offset < buffer.Length)
                {
                    int read = stream.Read(buffer, offset, buffer.Length - offset);
                    if (read <= 0) break;
                    offset += read;
                }
                if (offset == buffer.Length) return buffer;
                byte[] trimmed = new byte[offset];
                Buffer.BlockCopy(buffer, 0, trimmed, 0, offset);
                return trimmed;
            }
        }

        /// <summary>Writes through a temporary file in the same folder, then swaps it in.</summary>
        public static void WriteAtomic(string path, byte[] bytes)
        {
            string full = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(full);
            if (String.IsNullOrEmpty(directory)) directory = Environment.CurrentDirectory;
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(full))
                {
                    try { File.Replace(temporary, full, null, true); }
                    catch (PlatformNotSupportedException) { File.Copy(temporary, full, true); }
                    catch (IOException) { File.Copy(temporary, full, true); }
                    catch (UnauthorizedAccessException) { File.Copy(temporary, full, true); }
                }
                else File.Move(temporary, full);
            }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }
    }
}
