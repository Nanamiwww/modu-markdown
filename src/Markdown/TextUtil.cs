using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexMarkdownReader.Markdown
{
    public static class TextUtil
    {
        public const string AsciiPunctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

        public static bool IsAsciiPunctuation(char c)
        {
            return AsciiPunctuation.IndexOf(c) >= 0;
        }

        public static bool IsPunctuation(char c)
        {
            if (IsAsciiPunctuation(c)) return true;
            UnicodeCategory category = Char.GetUnicodeCategory(c);
            switch (category)
            {
                case UnicodeCategory.ConnectorPunctuation:
                case UnicodeCategory.DashPunctuation:
                case UnicodeCategory.OpenPunctuation:
                case UnicodeCategory.ClosePunctuation:
                case UnicodeCategory.InitialQuotePunctuation:
                case UnicodeCategory.FinalQuotePunctuation:
                case UnicodeCategory.OtherPunctuation:
                case UnicodeCategory.MathSymbol:
                case UnicodeCategory.CurrencySymbol:
                case UnicodeCategory.ModifierSymbol:
                case UnicodeCategory.OtherSymbol:
                    return true;
            }
            return false;
        }

        /// <summary>Han, kana, Hangul and full-width / CJK punctuation — text that is not separated by spaces.</summary>
        public static bool IsCjk(char c)
        {
            return (c >= 0x2E80 && c <= 0x2FDF) ||   // radicals
                   (c >= 0x3000 && c <= 0x303F) ||   // CJK symbols and punctuation
                   (c >= 0x3040 && c <= 0x30FF) ||   // kana
                   (c >= 0x3100 && c <= 0x31BF) ||   // bopomofo
                   (c >= 0x31F0 && c <= 0x31FF) ||
                   (c >= 0x3400 && c <= 0x4DBF) ||
                   (c >= 0x4E00 && c <= 0x9FFF) ||
                   (c >= 0xAC00 && c <= 0xD7AF) ||   // Hangul
                   (c >= 0xF900 && c <= 0xFAFF) ||
                   (c >= 0xFE30 && c <= 0xFE4F) ||
                   (c >= 0xFF00 && c <= 0xFFEF);     // full-width forms
        }

        public static bool IsHan(char c)
        {
            return (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF);
        }

        /// <summary>Counts Han characters individually and runs of other letters/digits as words.</summary>
        public static int CountTextUnits(string value)
        {
            if (String.IsNullOrEmpty(value)) return 0;
            int count = 0;
            bool inWord = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (IsHan(c)) { count++; inWord = false; continue; }
                if (Char.IsHighSurrogate(c) && i + 1 < value.Length && Char.IsLowSurrogate(value[i + 1]))
                {
                    int code = Char.ConvertToUtf32(c, value[i + 1]);
                    i++;
                    if (code >= 0x20000 && code <= 0x3134F) { count++; inWord = false; continue; }
                    if (!inWord) { count++; inWord = true; }
                    continue;
                }
                if (Char.IsLetterOrDigit(c))
                {
                    if (!inWord) { count++; inWord = true; }
                }
                else inWord = false;
            }
            return count;
        }

        /// <summary>GitHub-style heading slug. Keeps Unicode letters (including Chinese).</summary>
        public static string Slugify(string text)
        {
            if (text == null) return "";
            StringBuilder builder = new StringBuilder();
            foreach (char raw in text.Trim().ToLowerInvariant())
            {
                char c = raw;
                if (Char.IsLetterOrDigit(c) || c == '_' || c == '-') builder.Append(c);
                else if (c == ' ' || c == '\t') builder.Append('-');
                else if (Char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) builder.Append(c);
            }
            return builder.ToString();
        }

        public static string NormalizeReferenceLabel(string label)
        {
            if (label == null) return "";
            return Regex.Replace(label.Trim(), @"\s+", " ").ToUpperInvariant();
        }

        /// <summary>Expands tabs in the leading indentation to 4-column stops.</summary>
        public static string ExpandLeadingTabs(string line)
        {
            if (line.IndexOf('\t') < 0) return line;
            StringBuilder builder = new StringBuilder();
            int column = 0;
            int i = 0;
            for (; i < line.Length; i++)
            {
                char c = line[i];
                if (c == ' ') { builder.Append(' '); column++; }
                else if (c == '\t') { int spaces = 4 - (column % 4); builder.Append(' ', spaces); column += spaces; }
                else break;
            }
            builder.Append(line, i, line.Length - i);
            return builder.ToString();
        }

        public static int LeadingSpaces(string line)
        {
            int count = 0;
            while (count < line.Length && line[count] == ' ') count++;
            return count;
        }

        public static bool IsBlank(string line)
        {
            for (int i = 0; i < line.Length; i++) if (!Char.IsWhiteSpace(line[i])) return false;
            return true;
        }

        public static string RemoveIndent(string line, int count)
        {
            int n = 0;
            while (n < count && n < line.Length && line[n] == ' ') n++;
            return line.Substring(n);
        }

        public static string HtmlEncode(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            StringBuilder builder = new StringBuilder(value.Length + 16);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '&': builder.Append("&amp;"); break;
                    case '<': builder.Append("&lt;"); break;
                    case '>': builder.Append("&gt;"); break;
                    case '"': builder.Append("&quot;"); break;
                    case '\'': builder.Append("&#39;"); break;
                    default: builder.Append(c); break;
                }
            }
            return builder.ToString();
        }

        private static readonly Dictionary<string, string> Entities = CreateEntities();

        private static Dictionary<string, string> CreateEntities()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            map["amp"] = "&"; map["lt"] = "<"; map["gt"] = ">"; map["quot"] = "\""; map["apos"] = "'"; map["nbsp"] = " ";
            map["copy"] = "©"; map["reg"] = "®"; map["trade"] = "™"; map["hellip"] = "…"; map["mdash"] = "—"; map["ndash"] = "–";
            map["laquo"] = "«"; map["raquo"] = "»"; map["times"] = "×"; map["divide"] = "÷"; map["deg"] = "°"; map["plusmn"] = "±";
            map["middot"] = "·"; map["larr"] = "←"; map["rarr"] = "→"; map["uarr"] = "↑"; map["darr"] = "↓"; map["le"] = "≤"; map["ge"] = "≥";
            map["ne"] = "≠"; map["asymp"] = "≈"; map["micro"] = "µ"; map["sect"] = "§"; map["para"] = "¶"; map["bull"] = "•";
            map["lsquo"] = "‘"; map["rsquo"] = "’"; map["ldquo"] = "“"; map["rdquo"] = "”"; map["ensp"] = " "; map["emsp"] = " "; map["thinsp"] = " ";
            return map;
        }

        /// <summary>Tries to decode an HTML entity starting at <paramref name="start"/> (which must be '&amp;').</summary>
        public static bool TryDecodeEntity(string value, int start, out string decoded, out int length)
        {
            decoded = null; length = 0;
            int semicolon = value.IndexOf(';', start + 1);
            if (semicolon < 0 || semicolon - start > 32) return false;
            string body = value.Substring(start + 1, semicolon - start - 1);
            if (body.Length == 0) return false;
            try
            {
                if (body[0] == '#')
                {
                    int code;
                    bool ok = body.Length > 1 && (body[1] == 'x' || body[1] == 'X')
                        ? Int32.TryParse(body.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)
                        : Int32.TryParse(body.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out code);
                    if (!ok || code <= 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF)) return false;
                    decoded = Char.ConvertFromUtf32(code);
                    length = semicolon - start + 1;
                    return true;
                }
                string named;
                if (Entities.TryGetValue(body, out named))
                {
                    decoded = named;
                    length = semicolon - start + 1;
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
