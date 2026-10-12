using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexMarkdownReader.Markdown
{
    /// <summary>
    /// A pragmatic CommonMark / GitHub Flavored Markdown parser. Container blocks
    /// (block quotes, list items, footnotes) collect their lines and are parsed
    /// recursively, which keeps nesting, numbering and continuation lines intact.
    /// </summary>
    public sealed class MarkdownParser
    {
        private const int MaxDepth = 24;
        private static readonly Regex FenceOpen = new Regex(@"^(`{3,}|~{3,})(.*)$", RegexOptions.Compiled);
        private static readonly Regex AtxHeading = new Regex(@"^(#{1,6})(?:[ \t]+(.*?))?[ \t]*$", RegexOptions.Compiled);
        private static readonly Regex AtxClosing = new Regex(@"(?:^|[ \t]+)#+[ \t]*$", RegexOptions.Compiled);
        private static readonly Regex ThematicBreak = new Regex(@"^((\*[ \t]*){3,}|(-[ \t]*){3,}|(_[ \t]*){3,})$", RegexOptions.Compiled);
        private static readonly Regex SetextUnderline = new Regex(@"^ {0,3}(=+|-+)[ \t]*$", RegexOptions.Compiled);
        private static readonly Regex DelimiterCell = new Regex(@"^:?-+:?$", RegexOptions.Compiled);
        private static readonly Regex ReferenceDefinition = new Regex(@"^ {0,3}\[((?:[^\]\\]|\\.){1,999})\]:[ \t]*(<[^<>\n]*>|\S+)(?:[ \t]+(""[^""]*""|'[^']*'|\([^()]*\)))?[ \t]*$", RegexOptions.Compiled);
        private static readonly Regex FootnoteDefinitionStart = new Regex(@"^ {0,3}\[\^([^\]\s]+)\]:[ \t]?(.*)$", RegexOptions.Compiled);
        private static readonly Regex AlertMarker = new Regex(@"^\s*\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TaskMarker = new Regex(@"^\[([ xX])\](?:[ \t]+|$)", RegexOptions.Compiled);

        private readonly MarkdownDocument document;
        private readonly Dictionary<string, int> nextHeadingSuffix = new Dictionary<string, int>(StringComparer.Ordinal);

        private MarkdownParser(MarkdownDocument document)
        {
            this.document = document;
        }

        public static MarkdownDocument Parse(string markdown)
        {
            MarkdownDocument document = new MarkdownDocument();
            string normalized = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            if (normalized.Length > 0 && normalized[0] == '﻿') normalized = normalized.Substring(1);
            string[] raw = normalized.Split('\n');
            List<string> lines = new List<string>(raw.Length);
            List<int> numbers = new List<int>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                lines.Add(TextUtil.ExpandLeadingTabs(raw[i]));
                numbers.Add(i);
            }
            MarkdownParser parser = new MarkdownParser(document);
            document.Blocks = parser.ParseBlocks(lines, numbers, 0);
            parser.ParseInlines();
            return document;
        }

        // ------------------------------------------------------------------ blocks

        private List<Block> ParseBlocks(List<string> lines, List<int> numbers, int depth)
        {
            List<Block> result = new List<Block>();
            int i = 0;
            while (i < lines.Count)
            {
                string line = lines[i];
                if (TextUtil.IsBlank(line)) { i++; continue; }
                if (depth > MaxDepth) { i = AddParagraph(result, lines, numbers, i); continue; }

                int indent = TextUtil.LeadingSpaces(line);
                if (indent >= 4) { i = AddIndentedCode(result, lines, numbers, i); continue; }
                string trimmed = line.Substring(indent);

                Match fence = FenceOpen.Match(trimmed);
                if (fence.Success && (fence.Groups[1].Value[0] == '~' || fence.Groups[2].Value.IndexOf('`') < 0))
                {
                    i = AddFence(result, lines, numbers, i, indent, fence);
                    continue;
                }

                if (IsMathStart(trimmed))
                {
                    int next = AddMathBlock(result, lines, numbers, i);
                    if (next > i) { i = next; continue; }
                }

                if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
                {
                    int end = FindCommentEnd(lines, i);
                    if (end >= 0) { i = end + 1; continue; }
                }

                Match heading = AtxHeading.Match(trimmed);
                if (heading.Success)
                {
                    Block block = new Block(BlockKind.Heading, numbers[i]);
                    block.Level = heading.Groups[1].Value.Length;
                    string content = heading.Groups[2].Success ? heading.Groups[2].Value : "";
                    content = AtxClosing.Replace(content, "").Trim();
                    block.RawText = content;
                    result.Add(block);
                    i++;
                    continue;
                }

                if (ThematicBreak.IsMatch(trimmed))
                {
                    result.Add(new Block(BlockKind.ThematicBreak, numbers[i]));
                    i++;
                    continue;
                }

                if (trimmed.Length > 0 && trimmed[0] == '>')
                {
                    i = AddQuote(result, lines, numbers, i, depth);
                    continue;
                }

                ListMarker marker;
                if (TryParseListMarker(line, out marker))
                {
                    i = AddList(result, lines, numbers, i, marker, depth);
                    continue;
                }

                if (IsTableStart(lines, i))
                {
                    i = AddTable(result, lines, numbers, i);
                    continue;
                }

                Match footnote = FootnoteDefinitionStart.Match(line);
                if (footnote.Success)
                {
                    i = AddFootnoteDefinition(lines, numbers, i, footnote, depth);
                    continue;
                }

                Match reference = ReferenceDefinition.Match(line);
                if (reference.Success)
                {
                    AddReference(reference);
                    i++;
                    continue;
                }

                i = AddParagraph(result, lines, numbers, i);
            }
            return result;
        }

        private int AddParagraph(List<Block> result, List<string> lines, List<int> numbers, int index)
        {
            StringBuilder text = new StringBuilder();
            int i = index;
            while (i < lines.Count)
            {
                string line = lines[i];
                if (TextUtil.IsBlank(line)) break;
                if (i > index)
                {
                    if (SetextUnderline.IsMatch(line) && text.Length > 0)
                    {
                        Block heading = new Block(BlockKind.Heading, numbers[index]);
                        heading.SourceEndLine = numbers[i];
                        heading.Level = line.TrimStart()[0] == '=' ? 1 : 2;
                        heading.RawText = text.ToString().Trim();
                        result.Add(heading);
                        return i + 1;
                    }
                    if (CanInterruptParagraph(lines, i)) break;
                }
                if (text.Length > 0) text.Append('\n');
                text.Append(line.TrimStart());
                i++;
            }
            Block paragraph = new Block(BlockKind.Paragraph, numbers[index]);
            paragraph.SourceEndLine = numbers[Math.Max(index, i - 1)];
            paragraph.RawText = text.ToString().TrimEnd();
            if (paragraph.RawText.Length > 0) result.Add(paragraph);
            return Math.Max(i, index + 1);
        }

        private bool CanInterruptParagraph(List<string> lines, int i)
        {
            string line = lines[i];
            int indent = TextUtil.LeadingSpaces(line);
            if (indent >= 4) return false;
            string trimmed = line.Substring(indent);
            if (trimmed.Length == 0) return true;
            Match fence = FenceOpen.Match(trimmed);
            if (fence.Success && (fence.Groups[1].Value[0] == '~' || fence.Groups[2].Value.IndexOf('`') < 0)) return true;
            if (AtxHeading.IsMatch(trimmed)) return true;
            if (ThematicBreak.IsMatch(trimmed)) return true;
            if (trimmed[0] == '>') return true;
            if (IsMathStart(trimmed)) return true;
            ListMarker marker;
            if (TryParseListMarker(line, out marker) && !marker.Empty && (!marker.Ordered || marker.Number == 1)) return true;
            if (IsTableStart(lines, i)) return true;
            if (FootnoteDefinitionStart.IsMatch(line)) return true;
            return false;
        }

        /// <summary>Used for lazy continuation lines: any recognizable block start ends the lazy text.</summary>
        private bool StartsBlock(List<string> lines, int i)
        {
            if (CanInterruptParagraph(lines, i)) return true;
            ListMarker marker;
            return TryParseListMarker(lines[i], out marker);
        }

        private int AddIndentedCode(List<Block> result, List<string> lines, List<int> numbers, int index)
        {
            List<string> content = new List<string>();
            int i = index;
            int lastContent = index;
            while (i < lines.Count && (TextUtil.IsBlank(lines[i]) || TextUtil.LeadingSpaces(lines[i]) >= 4))
            {
                if (!TextUtil.IsBlank(lines[i])) lastContent = i;
                i++;
            }
            for (int k = index; k <= lastContent; k++) content.Add(TextUtil.IsBlank(lines[k]) ? "" : TextUtil.RemoveIndent(lines[k], 4));
            Block block = new Block(BlockKind.CodeBlock, numbers[index]);
            block.SourceEndLine = numbers[lastContent];
            block.Info = "";
            block.RawText = String.Join("\n", content.ToArray());
            result.Add(block);
            return lastContent + 1;
        }

        private int AddFence(List<Block> result, List<string> lines, List<int> numbers, int index, int indent, Match opener)
        {
            string marker = opener.Groups[1].Value;
            char markerChar = marker[0];
            string info = opener.Groups[2].Value.Trim();
            List<string> content = new List<string>();
            int i = index + 1;
            bool closed = false;
            while (i < lines.Count)
            {
                string line = lines[i];
                int lineIndent = TextUtil.LeadingSpaces(line);
                if (lineIndent <= 3)
                {
                    string trimmed = line.Substring(lineIndent).TrimEnd();
                    int run = 0;
                    while (run < trimmed.Length && trimmed[run] == markerChar) run++;
                    if (run >= marker.Length && run == trimmed.Length) { closed = true; break; }
                }
                content.Add(TextUtil.RemoveIndent(line, indent));
                i++;
            }
            Block block = new Block(BlockKind.CodeBlock, numbers[index]);
            block.SourceEndLine = numbers[Math.Min(i, lines.Count - 1)];
            block.Info = info;
            block.RawText = String.Join("\n", content.ToArray());
            result.Add(block);
            return closed ? i + 1 : i;
        }

        private static bool IsMathStart(string trimmed)
        {
            return trimmed.StartsWith("$$", StringComparison.Ordinal) || trimmed.StartsWith("\\[", StringComparison.Ordinal);
        }

        private int AddMathBlock(List<Block> result, List<string> lines, List<int> numbers, int index)
        {
            string first = lines[index].TrimStart();
            string opener = first.StartsWith("$$", StringComparison.Ordinal) ? "$$" : "\\[";
            string closer = opener == "$$" ? "$$" : "\\]";
            StringBuilder formula = new StringBuilder();
            string remainder = first.Substring(opener.Length);
            int next = index + 1;
            bool closed = TryAppendUntilMathCloser(formula, remainder, closer);
            // A previous search already showed that no line from here on closes this kind of block.
            ScanMemo memo = MemoFor(lines);
            if (!closed && next >= (opener == "$$" ? memo.NoDollarCloser : memo.NoBracketCloser)) return index;
            bool hasContent = remainder.Trim().Length > 0;
            while (!closed && next < lines.Count)
            {
                bool blank = TextUtil.IsBlank(lines[next]);
                if (blank && opener == "$$" && !hasContent) break;
                if (!blank) hasContent = true;
                if (formula.Length > 0) formula.Append('\n');
                closed = TryAppendUntilMathCloser(formula, lines[next], closer);
                next++;
            }
            if (!closed)
            {
                if (next >= lines.Count)
                {
                    if (opener == "$$") memo.NoDollarCloser = Math.Min(memo.NoDollarCloser, index + 1);
                    else memo.NoBracketCloser = Math.Min(memo.NoBracketCloser, index + 1);
                }
                return index;
            }
            string source = formula.ToString().Trim();
            if (source.Length == 0) return index;
            Block block = new Block(BlockKind.MathBlock, numbers[index]);
            block.SourceEndLine = numbers[next - 1];
            block.RawText = source;
            result.Add(block);
            return next;
        }

        private static bool TryAppendUntilMathCloser(StringBuilder target, string value, string closer)
        {
            int close = FindUnescaped(value, closer, 0);
            if (close < 0) { target.Append(value); return false; }
            if (value.Substring(close + closer.Length).Trim().Length != 0) { target.Append(value); return false; }
            target.Append(value.Substring(0, close));
            return true;
        }

        internal static int FindUnescaped(string value, string token, int start)
        {
            int index = Math.Max(0, start);
            while (index <= value.Length - token.Length)
            {
                index = value.IndexOf(token, index, StringComparison.Ordinal);
                if (index < 0) return -1;
                int slashes = 0;
                for (int i = index - 1; i >= 0 && value[i] == '\\'; i--) slashes++;
                if ((slashes & 1) == 0) return index;
                index += token.Length;
            }
            return -1;
        }

        private int FindCommentEnd(List<string> lines, int index)
        {
            int start = lines[index].IndexOf("<!--", StringComparison.Ordinal) + 4;
            int end = lines[index].IndexOf("-->", start, StringComparison.Ordinal);
            // Only hide the block if nothing but whitespace follows the comment.
            if (end >= 0) return lines[index].Substring(end + 3).Trim().Length != 0 ? -1 : index;
            // The comment continues on the first later line containing "-->", unless the search reaches a blank line
            // more than 200 lines down first. Both lines come from tables built once per list of lines: searching
            // forward from every unclosed "<!--" took seconds for a few thousand of them.
            ScanMemo memo = MemoFor(lines);
            memo.PrepareComments(lines);
            int close = memo.NextCommentClose[index + 1];
            if (close < 0) return -1;
            int stop = memo.NextBlank[Math.Min(index + 201, lines.Count)];
            if (stop >= 0 && stop < close) return -1;
            end = lines[close].IndexOf("-->", StringComparison.Ordinal);
            return lines[close].Substring(end + 3).Trim().Length != 0 ? -1 : close;
        }

        /// <summary>
        /// Remembers, per list of lines, what forward searches for closing markers found, so an unclosed opener does
        /// not search to the end again for every later opener.
        /// </summary>
        private sealed class ScanMemo
        {
            /// <summary>From this line on, no line closes a $$ (or \[) block.</summary>
            public int NoDollarCloser = Int32.MaxValue;
            public int NoBracketCloser = Int32.MaxValue;
            /// <summary>For each line: the first line at or after it that contains "-->" / that is blank (-1: none).</summary>
            public int[] NextCommentClose, NextBlank;

            public void PrepareComments(List<string> lines)
            {
                if (NextCommentClose != null) return;
                int count = lines.Count;
                NextCommentClose = new int[count + 1];
                NextBlank = new int[count + 1];
                NextCommentClose[count] = -1;
                NextBlank[count] = -1;
                for (int i = count - 1; i >= 0; i--)
                {
                    NextCommentClose[i] = lines[i].IndexOf("-->", StringComparison.Ordinal) >= 0 ? i : NextCommentClose[i + 1];
                    NextBlank[i] = TextUtil.IsBlank(lines[i]) ? i : NextBlank[i + 1];
                }
            }
        }

        private sealed class SameList : IEqualityComparer<List<string>>
        {
            public bool Equals(List<string> a, List<string> b) { return ReferenceEquals(a, b); }
            public int GetHashCode(List<string> list) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(list); }
        }

        private readonly Dictionary<List<string>, ScanMemo> scanMemos = new Dictionary<List<string>, ScanMemo>(new SameList());

        private ScanMemo MemoFor(List<string> lines)
        {
            ScanMemo memo;
            if (!scanMemos.TryGetValue(lines, out memo)) { memo = new ScanMemo(); scanMemos[lines] = memo; }
            return memo;
        }

        private int AddQuote(List<Block> result, List<string> lines, List<int> numbers, int index, int depth)
        {
            List<string> inner = new List<string>();
            List<int> innerNumbers = new List<int>();
            int i = index;
            while (i < lines.Count)
            {
                string line = lines[i];
                int indent = TextUtil.LeadingSpaces(line);
                if (indent <= 3 && indent < line.Length && line[indent] == '>')
                {
                    string content = line.Substring(indent + 1);
                    if (content.StartsWith(" ", StringComparison.Ordinal)) content = content.Substring(1);
                    inner.Add(TextUtil.ExpandLeadingTabs(content));
                    innerNumbers.Add(numbers[i]);
                    i++;
                    continue;
                }
                if (TextUtil.IsBlank(line)) break;
                // Lazy continuation of a paragraph inside the quote.
                if (inner.Count > 0 && !TextUtil.IsBlank(inner[inner.Count - 1]) && !StartsBlock(lines, i) && !LastLineOpensFence(inner))
                {
                    inner.Add(line.TrimStart());
                    innerNumbers.Add(numbers[i]);
                    i++;
                    continue;
                }
                break;
            }

            Block quote = new Block(BlockKind.Quote, numbers[index]);
            quote.SourceEndLine = numbers[i - 1];
            int firstContent = 0;
            while (firstContent < inner.Count && TextUtil.IsBlank(inner[firstContent])) firstContent++;
            if (firstContent < inner.Count)
            {
                Match alert = AlertMarker.Match(inner[firstContent]);
                if (alert.Success)
                {
                    quote.AlertKind = alert.Groups[1].Value.ToUpperInvariant();
                    inner.RemoveAt(firstContent);
                    innerNumbers.RemoveAt(firstContent);
                }
            }
            quote.Children = ParseBlocks(inner, innerNumbers, depth + 1);
            result.Add(quote);
            return i;
        }

        private static bool LastLineOpensFence(List<string> lines)
        {
            int open = 0;
            foreach (string line in lines)
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)) open ^= 1;
            }
            return open == 1;
        }

        // ------------------------------------------------------------------ lists

        private struct ListMarker
        {
            public bool Ordered;
            public char Bullet;          // '-', '+', '*' or the ordered delimiter '.' / ')'
            public int Number;
            public int ContentIndent;    // columns to strip from continuation lines
            public string Content;       // first line content
            public bool Empty;
        }

        private static bool TryParseListMarker(string line, out ListMarker marker)
        {
            marker = new ListMarker();
            int indent = TextUtil.LeadingSpaces(line);
            if (indent > 3 || indent >= line.Length) return false;
            int p = indent;
            int markerLength;
            char c = line[p];
            if (c == '-' || c == '+' || c == '*')
            {
                marker.Ordered = false;
                marker.Bullet = c;
                markerLength = 1;
            }
            else if (c >= '0' && c <= '9')
            {
                int q = p;
                while (q < line.Length && q - p < 9 && line[q] >= '0' && line[q] <= '9') q++;
                if (q >= line.Length || (line[q] != '.' && line[q] != ')')) return false;
                int number;
                if (!Int32.TryParse(line.Substring(p, q - p), NumberStyles.None, CultureInfo.InvariantCulture, out number)) return false;
                marker.Ordered = true;
                marker.Number = number;
                marker.Bullet = line[q];
                markerLength = q - p + 1;
            }
            else return false;

            int after = p + markerLength;
            if (after < line.Length && line[after] != ' ') return false;
            int spaces = 0;
            while (after + spaces < line.Length && line[after + spaces] == ' ') spaces++;
            if (after + spaces >= line.Length)
            {
                marker.Empty = true;
                marker.Content = "";
                marker.ContentIndent = after + 1;
                return true;
            }
            if (spaces >= 5)
            {
                marker.ContentIndent = after + 1;
                marker.Content = line.Substring(after + 1);
            }
            else
            {
                marker.ContentIndent = after + spaces;
                marker.Content = line.Substring(after + spaces);
            }
            return true;
        }

        private static bool SameListType(ListMarker a, ListMarker b)
        {
            return a.Ordered == b.Ordered && a.Bullet == b.Bullet;
        }

        private int AddList(List<Block> result, List<string> lines, List<int> numbers, int index, ListMarker first, int depth)
        {
            Block list = new Block(BlockKind.List, numbers[index]);
            list.Ordered = first.Ordered;
            list.Start = first.Ordered ? first.Number : 1;
            list.BulletChar = first.Bullet;
            list.Tight = true;

            int i = index;
            ListMarker marker = first;
            while (true)
            {
                List<string> itemLines = new List<string>();
                List<int> itemNumbers = new List<int>();
                itemLines.Add(marker.Content);
                itemNumbers.Add(numbers[i]);
                int j = i + 1;
                bool previousBlank = marker.Empty;
                while (j < lines.Count)
                {
                    string line = lines[j];
                    if (TextUtil.IsBlank(line))
                    {
                        // An empty list item may only be followed by indented content without a gap.
                        if (marker.Empty && itemLines.Count == 1) break;
                        itemLines.Add("");
                        itemNumbers.Add(numbers[j]);
                        previousBlank = true;
                        j++;
                        continue;
                    }
                    int indent = TextUtil.LeadingSpaces(line);
                    if (indent >= marker.ContentIndent)
                    {
                        itemLines.Add(TextUtil.RemoveIndent(line, marker.ContentIndent));
                        itemNumbers.Add(numbers[j]);
                        previousBlank = false;
                        j++;
                        continue;
                    }
                    if (!previousBlank && !StartsBlock(lines, j) && !LastLineOpensFence(itemLines))
                    {
                        itemLines.Add(line.TrimStart());
                        itemNumbers.Add(numbers[j]);
                        j++;
                        continue;
                    }
                    break;
                }

                int trailingBlank = 0;
                while (itemLines.Count > 1 && TextUtil.IsBlank(itemLines[itemLines.Count - 1]))
                {
                    itemLines.RemoveAt(itemLines.Count - 1);
                    itemNumbers.RemoveAt(itemNumbers.Count - 1);
                    trailingBlank++;
                }
                bool internalBlank = false;
                for (int k = 1; k < itemLines.Count; k++) if (TextUtil.IsBlank(itemLines[k])) { internalBlank = true; break; }

                Block item = new Block(BlockKind.ListItem, numbers[i]);
                item.SourceEndLine = itemNumbers[itemNumbers.Count - 1];
                Match task = TaskMarker.Match(itemLines[0]);
                if (task.Success)
                {
                    item.IsTask = true;
                    item.TaskChecked = task.Groups[1].Value != " ";
                    itemLines[0] = itemLines[0].Substring(task.Length);
                }
                item.Children = ParseBlocks(itemLines, itemNumbers, depth + 1);
                if (internalBlank && item.Children.Count > 1 && HasBlankBetweenChildren(item, itemLines, itemNumbers)) list.Tight = false;
                list.Children.Add(item);
                list.SourceEndLine = item.SourceEndLine;

                ListMarker nextMarker;
                if (j < lines.Count && !TextUtil.IsBlank(lines[j]) && !IsThematicBreakLine(lines[j]) &&
                    TryParseListMarker(lines[j], out nextMarker) && SameListType(first, nextMarker))
                {
                    if (trailingBlank > 0) list.Tight = false;
                    i = j;
                    marker = nextMarker;
                    continue;
                }
                i = j;
                break;
            }
            result.Add(list);
            return i;
        }

        private static bool HasBlankBetweenChildren(Block item, List<string> itemLines, List<int> itemNumbers)
        {
            // Loose when a blank line separates two direct children (not a blank inside a code block).
            for (int c = 1; c < item.Children.Count; c++)
            {
                int previousEnd = item.Children[c - 1].SourceEndLine;
                int nextStart = item.Children[c].SourceLine;
                for (int k = 0; k < itemNumbers.Count; k++)
                {
                    int line = itemNumbers[k];
                    if (line > previousEnd && line < nextStart && TextUtil.IsBlank(itemLines[k])) return true;
                }
            }
            return false;
        }

        private static bool IsThematicBreakLine(string line)
        {
            int indent = TextUtil.LeadingSpaces(line);
            return indent <= 3 && ThematicBreak.IsMatch(line.Substring(indent));
        }

        // ------------------------------------------------------------------ tables

        private static bool IsTableStart(List<string> lines, int i)
        {
            if (i + 1 >= lines.Count) return false;
            string header = lines[i];
            if (header.IndexOf('|') < 0 || TextUtil.LeadingSpaces(header) > 3) return false;
            List<string> delimiter;
            if (!TryParseDelimiterRow(lines[i + 1], out delimiter)) return false;
            List<string> cells = SplitTableRow(header);
            return cells.Count == delimiter.Count;
        }

        private static bool TryParseDelimiterRow(string line, out List<string> cells)
        {
            cells = null;
            if (TextUtil.LeadingSpaces(line) > 3) return false;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.IndexOf('-') < 0) return false;
            if (trimmed.IndexOf('|') < 0 && !trimmed.Contains(":")) return false;
            List<string> parts = SplitTableRow(trimmed);
            foreach (string part in parts)
                if (!DelimiterCell.IsMatch(part.Trim())) return false;
            cells = parts;
            return parts.Count > 0;
        }

        internal static List<string> SplitTableRow(string line)
        {
            string value = line.Trim();
            if (value.StartsWith("|", StringComparison.Ordinal)) value = value.Substring(1);
            if (value.EndsWith("|", StringComparison.Ordinal) && !value.EndsWith("\\|", StringComparison.Ordinal)) value = value.Substring(0, value.Length - 1);
            List<string> result = new List<string>();
            StringBuilder cell = new StringBuilder();
            int codeRun = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch == '\\' && i + 1 < value.Length && value[i + 1] == '|') { cell.Append('|'); i++; continue; }
                if (ch == '`')
                {
                    int run = 0;
                    while (i + run < value.Length && value[i + run] == '`') run++;
                    if (codeRun == 0) codeRun = run; else if (codeRun == run) codeRun = 0;
                    cell.Append('`', run);
                    i += run - 1;
                    continue;
                }
                if (ch == '|' && codeRun == 0) { result.Add(cell.ToString().Trim()); cell.Clear(); continue; }
                cell.Append(ch);
            }
            result.Add(cell.ToString().Trim());
            return result;
        }

        private int AddTable(List<Block> result, List<string> lines, List<int> numbers, int index)
        {
            List<string> delimiter;
            TryParseDelimiterRow(lines[index + 1], out delimiter);
            Block table = new Block(BlockKind.Table, numbers[index]);
            table.Aligns = new List<TableAlign>();
            foreach (string cell in delimiter)
            {
                string d = cell.Trim();
                bool left = d.StartsWith(":", StringComparison.Ordinal), right = d.EndsWith(":", StringComparison.Ordinal);
                table.Aligns.Add(left && right ? TableAlign.Center : right ? TableAlign.Right : left ? TableAlign.Left : TableAlign.None);
            }
            table.RawHeader = SplitTableRow(lines[index]);
            table.RawRows = new List<List<string>>();
            int i = index + 2;
            while (i < lines.Count && !TextUtil.IsBlank(lines[i]) && lines[i].IndexOf('|') >= 0 && !CanInterruptParagraphSimple(lines[i]))
            {
                table.RawRows.Add(SplitTableRow(lines[i]));
                i++;
            }
            table.SourceEndLine = numbers[i - 1];
            result.Add(table);
            return i;
        }

        private static bool CanInterruptParagraphSimple(string line)
        {
            int indent = TextUtil.LeadingSpaces(line);
            if (indent >= 4) return false;
            string trimmed = line.Substring(indent);
            return trimmed.StartsWith(">", StringComparison.Ordinal) || trimmed.StartsWith("```", StringComparison.Ordinal) ||
                   trimmed.StartsWith("~~~", StringComparison.Ordinal) || AtxHeading.IsMatch(trimmed);
        }

        // ------------------------------------------------------------------ references & footnotes

        private void AddReference(Match match)
        {
            string label = TextUtil.NormalizeReferenceLabel(match.Groups[1].Value);
            if (label.Length == 0 || document.References.ContainsKey(label)) return;
            string destination = match.Groups[2].Value;
            if (destination.StartsWith("<", StringComparison.Ordinal) && destination.EndsWith(">", StringComparison.Ordinal))
                destination = destination.Substring(1, destination.Length - 2);
            string title = match.Groups[3].Success ? match.Groups[3].Value : null;
            if (!String.IsNullOrEmpty(title) && title.Length >= 2) title = title.Substring(1, title.Length - 2);
            LinkReference reference = new LinkReference();
            reference.Destination = InlineParser.UnescapeString(destination);
            reference.Title = title == null ? null : InlineParser.UnescapeString(title);
            document.References[label] = reference;
        }

        private int AddFootnoteDefinition(List<string> lines, List<int> numbers, int index, Match start, int depth)
        {
            List<string> inner = new List<string>();
            List<int> innerNumbers = new List<int>();
            inner.Add(start.Groups[2].Value);
            innerNumbers.Add(numbers[index]);
            int i = index + 1;
            bool previousBlank = false;
            while (i < lines.Count)
            {
                string line = lines[i];
                if (TextUtil.IsBlank(line)) { inner.Add(""); innerNumbers.Add(numbers[i]); previousBlank = true; i++; continue; }
                int indent = TextUtil.LeadingSpaces(line);
                if (indent >= 2) { inner.Add(TextUtil.RemoveIndent(line, Math.Min(indent, 4))); innerNumbers.Add(numbers[i]); previousBlank = false; i++; continue; }
                if (!previousBlank && !StartsBlock(lines, i)) { inner.Add(line.TrimStart()); innerNumbers.Add(numbers[i]); i++; continue; }
                break;
            }
            while (inner.Count > 1 && TextUtil.IsBlank(inner[inner.Count - 1])) { inner.RemoveAt(inner.Count - 1); innerNumbers.RemoveAt(innerNumbers.Count - 1); }
            string key = start.Groups[1].Value.ToUpperInvariant();
            if (!document.Footnotes.ContainsKey(key))
            {
                FootnoteDefinition definition = new FootnoteDefinition();
                definition.Id = key;
                definition.Label = start.Groups[1].Value;
                definition.Blocks = ParseBlocks(inner, innerNumbers, depth + 1);
                document.Footnotes[key] = definition;
            }
            // Keep consuming trailing blank lines so the caller resumes cleanly.
            return i;
        }

        // ------------------------------------------------------------------ inline pass

        private void ParseInlines()
        {
            InlineParser inline = new InlineParser(document);
            HashSet<string> usedIds = new HashSet<string>(StringComparer.Ordinal);
            ParseInlines(document.Blocks, inline, usedIds, true);
            for (int i = 0; i < document.UsedFootnotes.Count; i++)
                ParseInlines(document.UsedFootnotes[i].Blocks, inline, usedIds, false);
        }

        private void ParseInlines(List<Block> blocks, InlineParser inline, HashSet<string> usedIds, bool collectHeadings)
        {
            foreach (Block block in blocks)
            {
                switch (block.Kind)
                {
                    case BlockKind.Paragraph:
                        block.Inlines = inline.Parse(block.RawText);
                        break;
                    case BlockKind.Heading:
                        block.Inlines = inline.Parse(block.RawText);
                        if (collectHeadings)
                        {
                            string text = Inline.PlainText(block.Inlines).Trim();
                            string baseId = TextUtil.Slugify(text);
                            if (baseId.Length == 0) baseId = "section";
                            string id = baseId;
                            if (usedIds.Contains(id))
                            {
                                // Continue after the last suffix handed out for this name instead of counting from 1 every
                                // time (thousands of identical headings used to take seconds).
                                int suffix;
                                if (!nextHeadingSuffix.TryGetValue(baseId, out suffix)) suffix = 1;
                                do { id = baseId + "-" + (suffix++).ToString(CultureInfo.InvariantCulture); } while (usedIds.Contains(id));
                                nextHeadingSuffix[baseId] = suffix;
                            }
                            usedIds.Add(id);
                            block.Id = id;
                            HeadingInfo info = new HeadingInfo();
                            info.Level = block.Level;
                            info.Text = text;
                            info.Id = id;
                            info.SourceLine = block.SourceLine;
                            info.Block = block;
                            document.Headings.Add(info);
                        }
                        break;
                    case BlockKind.Table:
                        block.HeaderCells = new List<List<Inline>>();
                        foreach (string cell in block.RawHeader) block.HeaderCells.Add(inline.Parse(cell));
                        block.Rows = new List<List<List<Inline>>>();
                        foreach (List<string> row in block.RawRows)
                        {
                            List<List<Inline>> cells = new List<List<Inline>>();
                            for (int c = 0; c < block.Aligns.Count; c++) cells.Add(inline.Parse(c < row.Count ? row[c] : ""));
                            block.Rows.Add(cells);
                        }
                        break;
                    case BlockKind.List:
                    case BlockKind.ListItem:
                    case BlockKind.Quote:
                        ParseInlines(block.Children, inline, usedIds, collectHeadings);
                        break;
                }
            }
        }
    }
}
