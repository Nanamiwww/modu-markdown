using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexMarkdownReader.Markdown
{
    /// <summary>
    /// Inline parser with CommonMark delimiter-run emphasis rules (so snake_case_names stay plain),
    /// a CJK-friendly flanking tweak, reference links, footnotes, GFM autolinks and a small safe HTML subset.
    /// </summary>
    public sealed class InlineParser
    {
        private const int MaxDepth = 16;
        private const int MaxReferenceLabel = 999;

        /// <summary>
        /// Emphasis, strong and strikethrough never nest deeper than this. Real documents use a few levels; without
        /// a limit a short line of thousands of "*" builds a tree so deep that every consumer of it (preview,
        /// export, plain text) runs out of stack. Delimiters beyond the limit are consumed without adding a level.
        /// </summary>
        public const int MaxNesting = 32;
        private static readonly Regex AutolinkUri = new Regex(@"\G<([A-Za-z][A-Za-z0-9+.\-]{1,31}:[^<>\s]*)>", RegexOptions.Compiled);
        private static readonly Regex AutolinkEmail = new Regex(@"\G<([A-Za-z0-9.!#$%&'*+/=?^_`{|}~\-]+@[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?)*)>", RegexOptions.Compiled);
        private static readonly Regex SafeTag = new Regex(@"\G<(/?)(br|sub|sup|kbd|mark|b|strong|i|em|s|del|strike)(\s*/?)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BareUrl = new Regex(@"\G(?:https?://|www\.)[^\s<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FootnoteReference = new Regex(@"\G\[\^([^\]\s]{1,200})\]", RegexOptions.Compiled);
        private static readonly Regex LooseTitle = new Regex("^(.+?)\\s+[\"'][^\"']*[\"']\\s*$", RegexOptions.Compiled);

        private readonly MarkdownDocument document;

        public InlineParser(MarkdownDocument document)
        {
            this.document = document ?? new MarkdownDocument();
        }

        public List<Inline> Parse(string text)
        {
            string value = text ?? "";
            memos.Clear();
            // Link destinations and titles are the only forward searches without an exact memo. They share
            // a budget proportional to the text: ordinary documents never come close, pathological input
            // (thousands of "[a](" without a closing parenthesis) degrades to plain text instead of hanging.
            scanBudget = 16L * value.Length + 100000;
            return Parse(value, 0, false);
        }

        private List<Inline> Parse(string s, int depth, bool inLink)
        {
            List<Inline> nodes = new List<Inline>();
            if (String.IsNullOrEmpty(s)) return nodes;
            if (depth > MaxDepth) { nodes.Add(Inline.MakeText(s)); return nodes; }

            StringBuilder plain = new StringBuilder();
            int n = s.Length;
            int i = 0;
            while (i < n)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\':
                        {
                            string math; int mathLength;
                            if (i + 1 < n && s[i + 1] == '(' && TryReadInlineMath(s, i, out math, out mathLength))
                            {
                                Flush(nodes, plain);
                                Inline node = new Inline(InlineKind.Math); node.Text = math; nodes.Add(node);
                                i += mathLength; continue;
                            }
                            if (i + 1 < n && s[i + 1] == '\n') { FlushHardBreak(nodes, plain); i += 2; continue; }
                            if (i + 1 < n && TextUtil.IsAsciiPunctuation(s[i + 1])) { plain.Append(s[i + 1]); i += 2; continue; }
                            plain.Append('\\'); i++; continue;
                        }
                    case '`':
                        {
                            int run = CountRun(s, i, '`');
                            int close = FindClosingBackticks(s, i + run, run);
                            if (close < 0) { plain.Append('`', run); i += run; continue; }
                            string code = s.Substring(i + run, close - i - run).Replace('\n', ' ');
                            if (code.Length >= 2 && code[0] == ' ' && code[code.Length - 1] == ' ' && code.Trim().Length > 0) code = code.Substring(1, code.Length - 2);
                            Flush(nodes, plain);
                            Inline node = new Inline(InlineKind.Code); node.Text = code; nodes.Add(node);
                            i = close + run; continue;
                        }
                    case '$':
                        {
                            string math; int mathLength;
                            if (TryReadInlineMath(s, i, out math, out mathLength))
                            {
                                Flush(nodes, plain);
                                Inline node = new Inline(InlineKind.Math); node.Text = math; nodes.Add(node);
                                i += mathLength; continue;
                            }
                            plain.Append(c); i++; continue;
                        }
                    case '<':
                        {
                            int consumed;
                            if (TryParseAngle(s, i, depth, inLink, nodes, plain, out consumed)) { i += consumed; continue; }
                            plain.Append(c); i++; continue;
                        }
                    case '!':
                        {
                            if (i + 1 < n && s[i + 1] == '[')
                            {
                                Inline image; int end;
                                if (TryParseLink(s, i + 1, true, depth, out image, out end))
                                {
                                    Flush(nodes, plain); nodes.Add(image); i = end; continue;
                                }
                            }
                            plain.Append(c); i++; continue;
                        }
                    case '[':
                        {
                            Match footnote = FootnoteReference.Match(s, i);
                            if (footnote.Success)
                            {
                                FootnoteDefinition definition;
                                if (document.Footnotes.TryGetValue(footnote.Groups[1].Value.ToUpperInvariant(), out definition))
                                {
                                    if (definition.Number == 0)
                                    {
                                        definition.Number = document.UsedFootnotes.Count + 1;
                                        document.UsedFootnotes.Add(definition);
                                    }
                                    Flush(nodes, plain);
                                    Inline node = new Inline(InlineKind.FootnoteRef);
                                    node.FootnoteNumber = definition.Number;
                                    node.FootnoteId = definition.Id;
                                    nodes.Add(node);
                                    i += footnote.Length; continue;
                                }
                            }
                            Inline link; int end;
                            if (!inLink && TryParseLink(s, i, false, depth, out link, out end))
                            {
                                Flush(nodes, plain); nodes.Add(link); i = end; continue;
                            }
                            plain.Append(c); i++; continue;
                        }
                    case '*':
                    case '_':
                    case '~':
                        {
                            int run = CountRun(s, i, c);
                            if (c == '~' && run > 2) { plain.Append(c, run); i += run; continue; }
                            // 5~10元: a range in running text, neither a subscript nor strikethrough.
                            if (c == '~' && run == 1 && IsDigitRange(s, i)) { plain.Append(c); i++; continue; }
                            int consumed;
                            if (c == '~' && run == 1 && TryParseScript(s, i, depth, inLink, nodes, plain, out consumed)) { i += consumed; continue; }
                            Flush(nodes, plain);
                            nodes.Add(MakeDelimiter(s, i, run, c));
                            i += run; continue;
                        }
                    case '^':
                        {
                            int consumed;
                            if (TryParseScript(s, i, depth, inLink, nodes, plain, out consumed)) { i += consumed; continue; }
                            plain.Append(c); i++; continue;
                        }
                    case '\n':
                        {
                            int trailing = 0;
                            while (plain.Length - trailing > 0 && plain[plain.Length - 1 - trailing] == ' ') trailing++;
                            if (trailing >= 2) { plain.Length -= trailing; FlushHardBreak(nodes, plain); i++; continue; }
                            plain.Length -= trailing;
                            char before = PreviousVisibleChar(s, i);
                            char after = i + 1 < n ? s[i + 1] : ' ';
                            if (TextUtil.IsCjk(before) && TextUtil.IsCjk(after)) { i++; continue; }
                            Flush(nodes, plain);
                            nodes.Add(new Inline(InlineKind.SoftBreak));
                            i++; continue;
                        }
                    case '&':
                        {
                            string decoded; int length;
                            if (TextUtil.TryDecodeEntity(s, i, out decoded, out length)) { plain.Append(decoded); i += length; continue; }
                            plain.Append(c); i++; continue;
                        }
                    case 'h':
                    case 'H':
                    case 'w':
                    case 'W':
                        {
                            int consumed;
                            if (!inLink && TryParseBareUrl(s, i, nodes, plain, out consumed)) { i += consumed; continue; }
                            plain.Append(c); i++; continue;
                        }
                    default:
                        plain.Append(c); i++; continue;
                }
            }
            Flush(nodes, plain);
            if (UseReferenceEmphasisForTest) ProcessEmphasisReference(nodes); else ProcessEmphasis(nodes);
            return Merge(nodes);
        }

        // ------------------------------------------------------------------ helpers

        private static int CountRun(string s, int start, char c)
        {
            int run = 0;
            while (start + run < s.Length && s[start + run] == c) run++;
            return run;
        }

        private int FindClosingBackticks(string s, int from, int run)
        {
            TextMemo memo = MemoFor(s);
            ForwardSearch search;
            if (!memo.Backticks.TryGetValue(run, out search)) { search = new ForwardSearch(); memo.Backticks[run] = search; }
            int cached;
            if (search.TryReuse(from, out cached)) return cached;
            int i = from;
            while (i < s.Length)
            {
                if (s[i] == '`')
                {
                    int length = CountRun(s, i, '`');
                    if (length == run) return search.Remember(from, i, i);
                    i += length;
                }
                else i++;
            }
            return search.Remember(from, -1, s.Length);
        }

        private static char PreviousVisibleChar(string s, int index)
        {
            for (int k = index - 1; k >= 0; k--) if (s[k] != ' ') return s[k];
            return ' ';
        }

        private static void Flush(List<Inline> nodes, StringBuilder plain)
        {
            if (plain.Length == 0) return;
            nodes.Add(Inline.MakeText(plain.ToString()));
            plain.Clear();
        }

        private static void FlushHardBreak(List<Inline> nodes, StringBuilder plain)
        {
            Flush(nodes, plain);
            nodes.Add(new Inline(InlineKind.HardBreak));
        }

        private static bool IsFlankSpace(char c)
        {
            return c == '\n' || Char.IsWhiteSpace(c);
        }

        private static bool IsFlankPunctuation(char c)
        {
            // CJK characters behave like punctuation so that **“中文”**后文 closes correctly.
            return TextUtil.IsPunctuation(c) || TextUtil.IsCjk(c);
        }

        private static Inline MakeDelimiter(string s, int start, int run, char c)
        {
            char before = start > 0 ? s[start - 1] : '\n';
            char after = start + run < s.Length ? s[start + run] : '\n';
            bool leftFlanking = !IsFlankSpace(after) && (!IsFlankPunctuation(after) || IsFlankSpace(before) || IsFlankPunctuation(before));
            bool rightFlanking = !IsFlankSpace(before) && (!IsFlankPunctuation(before) || IsFlankSpace(after) || IsFlankPunctuation(after));
            Inline node = Inline.MakeText(new string(c, run));
            node.IsDelimiter = true;
            node.DelimiterChar = c;
            node.DelimiterCount = run;
            node.DelimiterOriginal = run;
            if (c == '_')
            {
                node.CanOpen = leftFlanking && (!rightFlanking || IsFlankPunctuation(before));
                node.CanClose = rightFlanking && (!leftFlanking || IsFlankPunctuation(after));
            }
            else
            {
                node.CanOpen = leftFlanking;
                node.CanClose = rightFlanking;
            }
            return node;
        }

        /// <summary>Self-test only: run <see cref="ProcessEmphasisReference"/> instead, to compare both implementations.</summary>
        internal static bool UseReferenceEmphasisForTest;

        /// <summary>
        /// CommonMark "process emphasis". Nodes and delimiters are kept in linked lists of indices, so a match is
        /// wrapped and the used delimiters are dropped in constant time; the list version shifted the whole paragraph
        /// for every match, and a long paragraph full of *pairs* took seconds. The matching rules and the order of the
        /// steps are exactly those of <see cref="ProcessEmphasisReference"/>; the self-test compares both.
        /// </summary>
        private static void ProcessEmphasis(List<Inline> nodes)
        {
            int count = nodes.Count;
            List<int> delimiterNodes = new List<int>();
            for (int k = 0; k < count; k++) if (nodes[k].IsDelimiter) delimiterNodes.Add(k);
            int m = delimiterNodes.Count;
            if (m == 0) return;

            // Node chain (wrappers are appended as they are created) and delimiter chain.
            List<Inline> items = new List<Inline>(nodes);
            List<int> next = new List<int>(count + 64), prev = new List<int>(count + 64);
            for (int k = 0; k < count; k++) { next.Add(k + 1 < count ? k + 1 : -1); prev.Add(k - 1); }
            int head = 0;
            int[] dNext = new int[m], dPrev = new int[m];
            for (int d = 0; d < m; d++) { dPrev[d] = d - 1; dNext[d] = d + 1 < m ? d + 1 : -1; }
            int dHead = 0;

            // CommonMark's openers_bottom: once a closer has found no opener, no later closer of the same kind (character,
            // run length mod 3, whether it can also open) can find one at or below that point, so later searches stop
            // there, and a closer that cannot open either leaves the list. Each delimiter is then passed over a bounded
            // number of times. Without this a paragraph of 250 000 "a*" looked back over every earlier delimiter, and a
            // fixed number of rounds had to cut the work short: in a long paragraph the 5001st **bold** stayed as text.
            int[] bottom = new int[18];
            for (int b = 0; b < bottom.Length; b++) bottom[b] = -1;
            // Each round either moves on to the next delimiter or uses up at least one delimiter character, so this
            // bound is never reached; it only guards against a mistake turning into an endless loop.
            long rounds = m + 16;
            for (int d = 0; d < m; d++) rounds += items[delimiterNodes[d]].DelimiterCount;

            int ci = dHead;
            while (ci >= 0 && rounds-- > 0)
            {
                Inline closer = items[delimiterNodes[ci]];
                if (!closer.CanClose || closer.DelimiterCount == 0) { ci = dNext[ci]; continue; }
                int kind = BottomIndex(closer);
                int oi = dPrev[ci];
                Inline opener = null;
                for (; oi > bottom[kind]; oi = dPrev[oi])
                {
                    Inline candidate = items[delimiterNodes[oi]];
                    if (candidate.DelimiterChar != closer.DelimiterChar || !candidate.CanOpen || candidate.DelimiterCount == 0) continue;
                    if (closer.DelimiterChar == '~')
                    {
                        if (candidate.DelimiterCount == closer.DelimiterCount) { opener = candidate; break; }
                        continue;
                    }
                    bool oddMatch = (candidate.CanClose || closer.CanOpen) &&
                        (candidate.DelimiterOriginal + closer.DelimiterOriginal) % 3 == 0 &&
                        !(candidate.DelimiterOriginal % 3 == 0 && closer.DelimiterOriginal % 3 == 0);
                    if (!oddMatch) { opener = candidate; break; }
                }
                if (opener == null)
                {
                    int after = dNext[ci];
                    bottom[kind] = dPrev[ci];
                    if (!closer.CanOpen) Unlink(dNext, dPrev, ci, ref dHead);
                    ci = after;
                    continue;
                }

                int use = closer.DelimiterChar == '~' ? closer.DelimiterCount : (closer.DelimiterCount >= 2 && opener.DelimiterCount >= 2 ? 2 : 1);
                InlineKind wrap = closer.DelimiterChar == '~' ? InlineKind.Strikethrough : (use == 2 ? InlineKind.Strong : InlineKind.Emphasis);
                opener.DelimiterCount -= use;
                opener.Text = new string(opener.DelimiterChar, opener.DelimiterCount);
                closer.DelimiterCount -= use;
                closer.Text = new string(closer.DelimiterChar, closer.DelimiterCount);

                int openNode = delimiterNodes[oi], closeNode = delimiterNodes[ci];
                int innerHeight = 0;
                for (int k = next[openNode]; k != closeNode; k = next[k]) innerHeight = Math.Max(innerHeight, HeightOf(items[k]));
                if (innerHeight < MaxNesting)
                {
                    List<Inline> inner = new List<Inline>();
                    for (int k = next[openNode]; k != closeNode; k = next[k]) inner.Add(items[k]);
                    Inline wrapper = new Inline(wrap);
                    wrapper.Children = inner;
                    wrapper.CachedHeight = innerHeight + 1;
                    int w = items.Count;
                    items.Add(wrapper);
                    next.Add(closeNode);
                    prev.Add(openNode);
                    next[openNode] = w;
                    prev[closeNode] = w;
                }
                // Otherwise the content is already MaxNesting levels deep: the delimiters are used up, no level is added.

                // Delimiters between opener and closer can no longer match: they stay as plain text.
                dNext[oi] = ci;
                dPrev[ci] = oi;
                if (opener.DelimiterCount == 0)
                {
                    Unlink(next, prev, openNode, ref head);
                    Unlink(dNext, dPrev, oi, ref dHead);
                }
                if (closer.DelimiterCount == 0)
                {
                    int after = dNext[ci];
                    Unlink(next, prev, closeNode, ref head);
                    Unlink(dNext, dPrev, ci, ref dHead);
                    ci = after;
                }
                // Otherwise the closer is looked at again: it may close another opener.
            }
            for (int d = dHead; d >= 0; d = dNext[d]) items[delimiterNodes[d]].IsDelimiter = false;
            nodes.Clear();
            for (int k = head; k >= 0; k = next[k]) nodes.Add(items[k]);
        }

        /// <summary>The openers_bottom slot of a closer: its character, its run length mod 3 and whether it can also open.</summary>
        private static int BottomIndex(Inline closer)
        {
            int character = closer.DelimiterChar == '*' ? 0 : closer.DelimiterChar == '_' ? 1 : 2;
            return (character * 3 + closer.DelimiterOriginal % 3) * 2 + (closer.CanOpen ? 1 : 0);
        }

        private static void Unlink(IList<int> next, IList<int> prev, int k, ref int head)
        {
            if (prev[k] >= 0) next[prev[k]] = next[k]; else head = next[k];
            if (next[k] >= 0) prev[next[k]] = prev[k];
        }

        /// <summary>The straightforward list-based version, kept as the reference the self-test compares against.</summary>
        internal static void ProcessEmphasisReference(List<Inline> nodes)
        {
            List<Inline> delimiters = new List<Inline>();
            foreach (Inline node in nodes) if (node.IsDelimiter) delimiters.Add(node);
            // The same bound as ProcessEmphasis: never reached, so both always finish the whole paragraph.
            long rounds = delimiters.Count + 16;
            foreach (Inline node in delimiters) rounds += node.DelimiterCount;
            int ci = 0;
            while (ci < delimiters.Count && rounds-- > 0)
            {
                Inline closer = delimiters[ci];
                if (!closer.CanClose || closer.DelimiterCount == 0) { ci++; continue; }
                int oi = ci - 1;
                Inline opener = null;
                for (; oi >= 0; oi--)
                {
                    Inline candidate = delimiters[oi];
                    if (candidate.DelimiterChar != closer.DelimiterChar || !candidate.CanOpen || candidate.DelimiterCount == 0) continue;
                    if (closer.DelimiterChar == '~')
                    {
                        if (candidate.DelimiterCount == closer.DelimiterCount) { opener = candidate; break; }
                        continue;
                    }
                    bool oddMatch = (candidate.CanClose || closer.CanOpen) &&
                        (candidate.DelimiterOriginal + closer.DelimiterOriginal) % 3 == 0 &&
                        !(candidate.DelimiterOriginal % 3 == 0 && closer.DelimiterOriginal % 3 == 0);
                    if (!oddMatch) { opener = candidate; break; }
                }
                if (opener == null) { ci++; continue; }

                int use = closer.DelimiterChar == '~' ? closer.DelimiterCount : (closer.DelimiterCount >= 2 && opener.DelimiterCount >= 2 ? 2 : 1);
                InlineKind kind = closer.DelimiterChar == '~' ? InlineKind.Strikethrough : (use == 2 ? InlineKind.Strong : InlineKind.Emphasis);
                opener.DelimiterCount -= use;
                opener.Text = new string(opener.DelimiterChar, opener.DelimiterCount);
                closer.DelimiterCount -= use;
                closer.Text = new string(closer.DelimiterChar, closer.DelimiterCount);

                int openIndex = nodes.IndexOf(opener);
                int closeIndex = nodes.IndexOf(closer);
                int innerHeight = 0;
                for (int k = openIndex + 1; k < closeIndex; k++) innerHeight = Math.Max(innerHeight, HeightOf(nodes[k]));
                if (innerHeight < MaxNesting)
                {
                    List<Inline> inner = nodes.GetRange(openIndex + 1, closeIndex - openIndex - 1);
                    nodes.RemoveRange(openIndex + 1, closeIndex - openIndex - 1);
                    Inline wrapper = new Inline(kind);
                    wrapper.Children = inner;
                    wrapper.CachedHeight = innerHeight + 1;
                    nodes.Insert(openIndex + 1, wrapper);
                }
                // Otherwise the content is already MaxNesting levels deep: the delimiters are used up, no level is added.

                if (ci - oi - 1 > 0) delimiters.RemoveRange(oi + 1, ci - oi - 1);
                ci = oi + 1;
                if (opener.DelimiterCount == 0)
                {
                    nodes.Remove(opener);
                    delimiters.RemoveAt(oi);
                    ci--;
                }
                if (closer.DelimiterCount == 0)
                {
                    nodes.Remove(closer);
                    delimiters.RemoveAt(ci);
                }
            }
            foreach (Inline node in delimiters) node.IsDelimiter = false;
        }

        /// <summary>
        /// Levels of nested containers in a node (0 for text). Cached: a node's children never change depth once built.
        /// Recursion is shallow because every tree produced here is already within MaxNesting and MaxDepth.
        /// </summary>
        private static int HeightOf(Inline node)
        {
            if (node.Children == null || node.Children.Count == 0) return 0;
            if (node.CachedHeight >= 0) return node.CachedHeight;
            int height = 0;
            foreach (Inline child in node.Children) height = Math.Max(height, HeightOf(child));
            node.CachedHeight = height + 1;
            return node.CachedHeight;
        }

        private static List<Inline> Merge(List<Inline> nodes)
        {
            List<Inline> result = new List<Inline>(nodes.Count);
            // Adjacent text is joined in one buffer: appending to a string over and over copied the growing text each
            // time, and a paragraph of many unmatched "*" took seconds.
            StringBuilder joined = null;
            foreach (Inline node in nodes)
            {
                node.IsDelimiter = false;
                if (node.Children != null) node.Children = Merge(node.Children);
                if (node.Kind == InlineKind.Text)
                {
                    if (String.IsNullOrEmpty(node.Text)) continue;
                    if (result.Count > 0 && result[result.Count - 1].Kind == InlineKind.Text)
                    {
                        if (joined == null) joined = new StringBuilder(result[result.Count - 1].Text);
                        joined.Append(node.Text);
                        continue;
                    }
                }
                if (joined != null) { result[result.Count - 1].Text = joined.ToString(); joined = null; }
                result.Add(node);
            }
            if (joined != null) result[result.Count - 1].Text = joined.ToString();
            return result;
        }

        // ------------------------------------------------------------------ angle brackets

        private bool TryParseAngle(string s, int i, int depth, bool inLink, List<Inline> nodes, StringBuilder plain, out int consumed)
        {
            consumed = 0;
            if (String.CompareOrdinal(s, i, "<!--", 0, 4) == 0)
            {
                int end = FindCommentClose(s, i + 4);
                if (end < 0) return false;
                consumed = end + 3 - i;
                return true;
            }
            Match uri = AutolinkUri.Match(s, i);
            if (uri.Success)
            {
                Flush(nodes, plain);
                Inline link = new Inline(InlineKind.Link);
                link.Href = uri.Groups[1].Value;
                link.Children = new List<Inline>();
                link.Children.Add(Inline.MakeText(uri.Groups[1].Value));
                nodes.Add(link);
                consumed = uri.Length;
                return true;
            }
            Match email = AutolinkEmail.Match(s, i);
            if (email.Success)
            {
                Flush(nodes, plain);
                Inline link = new Inline(InlineKind.Link);
                link.Href = "mailto:" + email.Groups[1].Value;
                link.Children = new List<Inline>();
                link.Children.Add(Inline.MakeText(email.Groups[1].Value));
                nodes.Add(link);
                consumed = email.Length;
                return true;
            }
            Match tag = SafeTag.Match(s, i);
            if (!tag.Success) return false;
            string name = tag.Groups[2].Value.ToLowerInvariant();
            bool closing = tag.Groups[1].Value.Length > 0;
            if (name == "br")
            {
                if (closing) return false;
                FlushHardBreak(nodes, plain);
                consumed = tag.Length;
                return true;
            }
            if (closing || tag.Groups[3].Value.Contains("/")) return false;
            int innerStart = i + tag.Length;
            int closeIndex = FindClosingTag(s, innerStart, name);
            if (closeIndex < 0) return false;
            string inner = s.Substring(innerStart, closeIndex - innerStart);
            int closeLength = s.IndexOf('>', closeIndex) - closeIndex + 1;
            InlineKind kind;
            switch (name)
            {
                case "sub": kind = InlineKind.Subscript; break;
                case "sup": kind = InlineKind.Superscript; break;
                case "kbd": kind = InlineKind.Keyboard; break;
                case "mark": kind = InlineKind.Mark; break;
                case "b":
                case "strong": kind = InlineKind.Strong; break;
                case "i":
                case "em": kind = InlineKind.Emphasis; break;
                default: kind = InlineKind.Strikethrough; break;
            }
            Flush(nodes, plain);
            Inline wrapper = new Inline(kind);
            wrapper.Children = Parse(inner, depth + 1, inLink);
            nodes.Add(wrapper);
            consumed = closeIndex + closeLength - i;
            return true;
        }

        /// <summary>Longest text between the marks of H~2~O or Fe^3+^.</summary>
        private const int MaxScriptLength = 32;

        /// <summary>
        /// Subscripts and superscripts written the Pandoc / Typora way, as in chemistry notes: H~2~O, Ca(OH)~2~, Fe^3+^,
        /// 10^-6^. The marks must follow a letter, digit or bracket and enclose a short text without spaces, so that
        /// ~deleted~ between words keeps its GFM meaning and x^2 + y^2 or ^_^ stay plain text. Text with Chinese
        /// punctuation between the marks is running text, not a subscript (a ~ between two digits never gets here:
        /// see <see cref="IsDigitRange"/>).
        /// </summary>
        private bool TryParseScript(string s, int i, int depth, bool inLink, List<Inline> nodes, StringBuilder plain, out int consumed)
        {
            consumed = 0;
            char mark = s[i];
            if (i == 0) return false;
            char before = s[i - 1];
            if (!Char.IsLetterOrDigit(before) && before != ')' && before != ']' && before != '~' && before != '^') return false;
            int limit = Math.Min(s.Length, i + 2 + MaxScriptLength);
            int close = -1;
            bool meaningful = false;
            for (int k = i + 1; k < limit; k++)
            {
                char ch = s[k];
                if (ch == mark) { close = k; break; }
                if (Char.IsWhiteSpace(ch) || IsProsePunctuation(ch)) return false;
                if (Char.IsLetterOrDigit(ch) || "+-−±'′*".IndexOf(ch) >= 0) meaningful = true;
                if (ch == '\\' && k + 1 < limit && TextUtil.IsAsciiPunctuation(s[k + 1])) k++;
            }
            if (close <= i + 1 || !meaningful) return false;
            if (close + 1 < s.Length && s[close + 1] == mark) return false;
            Flush(nodes, plain);
            Inline wrapper = new Inline(mark == '~' ? InlineKind.Subscript : InlineKind.Superscript);
            wrapper.Children = Parse(s.Substring(i + 1, close - i - 1), depth + 1, inLink);
            nodes.Add(wrapper);
            consumed = close + 1 - i;
            return true;
        }

        /// <summary>Chinese and full-width punctuation, dashes and ellipses: a sign of running text, not of a subscript.</summary>
        private static bool IsProsePunctuation(char c)
        {
            return (c >= '\u3000' && c <= '\u303F') || (c >= '\uFF00' && c <= '\uFF0F') || (c >= '\uFF1A' && c <= '\uFF20') ||
                (c >= '\uFF3B' && c <= '\uFF40') || (c >= '\uFF5B' && c <= '\uFF65') || c == '\u2014' || c == '\u2026' ||
                (c >= '\u2018' && c <= '\u201F');
        }

        /// <summary>
        /// A single ~ between two digits: a range as Chinese text writes it (5~10元，3~5个, 1~2天). GFM would let two of
        /// them strike out the text between; here they stay as written.
        /// </summary>
        private static bool IsDigitRange(string s, int i)
        {
            return i > 0 && i + 1 < s.Length && Char.IsDigit(s[i - 1]) && Char.IsDigit(s[i + 1]);
        }

        private static readonly Dictionary<string, Regex> ClosingTagPatterns = new Dictionary<string, Regex>();

        private int FindClosingTag(string s, int from, string name)
        {
            TextMemo memo = MemoFor(s);
            ForwardSearch search;
            if (!memo.ClosingTags.TryGetValue(name, out search)) { search = new ForwardSearch(); memo.ClosingTags[name] = search; }
            int cached;
            if (search.TryReuse(from, out cached)) return cached;
            Regex close;
            lock (ClosingTagPatterns)
            {
                if (!ClosingTagPatterns.TryGetValue(name, out close))
                {
                    close = new Regex(@"</" + Regex.Escape(name) + @"\s*>", RegexOptions.IgnoreCase);
                    ClosingTagPatterns[name] = close;
                }
            }
            Match match = close.Match(s, from);
            return match.Success ? search.Remember(from, match.Index, match.Index) : search.Remember(from, -1, s.Length);
        }

        // ------------------------------------------------------------------ autolinks

        private static bool TryParseBareUrl(string s, int i, List<Inline> nodes, StringBuilder plain, out int consumed)
        {
            consumed = 0;
            if (i > 0)
            {
                char previous = s[i - 1];
                if (Char.IsLetterOrDigit(previous) || previous == '/' || previous == '.' || previous == '@' || previous == '-' || previous == '_') return false;
            }
            Match match = BareUrl.Match(s, i);
            if (!match.Success) return false;
            string url = match.Value;
            // Chinese text often follows a URL without a space; stop at the first non-ASCII character.
            for (int k = 0; k < url.Length; k++) if (url[k] > 0x7E) { url = url.Substring(0, k); break; }
            while (url.Length > 0 && "?!.,:;*_~'\"".IndexOf(url[url.Length - 1]) >= 0) url = url.Substring(0, url.Length - 1);
            while (url.EndsWith(")", StringComparison.Ordinal) && Count(url, '(') < Count(url, ')')) url = url.Substring(0, url.Length - 1);
            bool www = url.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
            int minimum = www ? 7 : (url.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? 9 : 8);
            if (url.Length < minimum || (www && url.IndexOf('.', 4) < 0)) return false;
            Flush(nodes, plain);
            Inline link = new Inline(InlineKind.Link);
            link.Href = www ? "http://" + url : url;
            link.Children = new List<Inline>();
            link.Children.Add(Inline.MakeText(url));
            nodes.Add(link);
            consumed = url.Length;
            return true;
        }

        private static int Count(string value, char c)
        {
            int count = 0;
            foreach (char item in value) if (item == c) count++;
            return count;
        }

        // ------------------------------------------------------------------ links

        private bool TryParseLink(string s, int start, bool image, int depth, out Inline node, out int end)
        {
            node = null; end = start;
            int n = s.Length;
            int k = FindLabelEnd(s, start);
            if (k < 0) return false;
            int labelLength = k - start - 1;
            int after = k + 1;
            string destination = null, title = null;
            int finish = -1;

            if (after < n && s[after] == '(')
            {
                if (scanBudget <= 0) return false;
                int reach;
                bool parsed = TryParseInlineDestination(s, after + 1, out destination, out title, out finish, out reach);
                scanBudget -= reach - after;
                if (!parsed && (scanBudget <= 0 || !TryParseLooseDestination(s, after + 1, out destination, out title, out finish))) return false;
            }
            else
            {
                // CommonMark caps reference labels at 999 characters. The cap also keeps deeply nested
                // brackets cheap: no huge label is copied just to find out that it is not a reference.
                if (labelLength > MaxReferenceLabel) return false;
                string referenceLabel = s.Substring(start + 1, labelLength);
                finish = after;
                if (after < n && s[after] == '[')
                {
                    int close = NextIndexOf(s, ']', after + 1);
                    if (close > after && close - after - 1 <= MaxReferenceLabel)
                    {
                        string explicitLabel = s.Substring(after + 1, close - after - 1);
                        if (explicitLabel.Trim().Length > 0) referenceLabel = explicitLabel;
                        finish = close + 1;
                    }
                }
                LinkReference reference;
                if (!document.References.TryGetValue(TextUtil.NormalizeReferenceLabel(referenceLabel), out reference)) return false;
                destination = reference.Destination;
                title = reference.Title;
            }

            string label = s.Substring(start + 1, labelLength);
            if (image)
            {
                node = new Inline(InlineKind.Image);
                node.Text = Inline.PlainText(Parse(label, depth + 1, true));
            }
            else
            {
                node = new Inline(InlineKind.Link);
                node.Children = Parse(label, depth + 1, true);
            }
            node.Href = destination ?? "";
            node.Title = title;
            end = finish;
            return true;
        }

        /// <param name="reach">How far the scan got, success or not; charged against the scan budget.</param>
        private static bool TryParseInlineDestination(string s, int p, out string destination, out string title, out int end, out int reach)
        {
            destination = null; title = null; end = p; reach = p;
            int n = s.Length;
            while (p < n && (s[p] == ' ' || s[p] == '\n')) p++;
            reach = p;
            if (p >= n) return false;
            if (s[p] == ')') { destination = ""; end = p + 1; return true; }
            StringBuilder dest = new StringBuilder();
            if (s[p] == '<')
            {
                p++;
                while (p < n && s[p] != '>' && s[p] != '\n' && s[p] != '<')
                {
                    if (s[p] == '\\' && p + 1 < n && TextUtil.IsAsciiPunctuation(s[p + 1])) { dest.Append(s[p + 1]); p += 2; continue; }
                    dest.Append(s[p]); p++;
                }
                reach = p;
                if (p >= n || s[p] != '>') return false;
                p++;
            }
            else
            {
                int parens = 0;
                while (p < n)
                {
                    char c = s[p];
                    if (c == '\\' && p + 1 < n && TextUtil.IsAsciiPunctuation(s[p + 1])) { dest.Append(s[p + 1]); p += 2; continue; }
                    if (c == ' ' || c == '\n' || Char.IsControl(c)) break;
                    if (c == '(') parens++;
                    else if (c == ')')
                    {
                        if (parens == 0) break;
                        parens--;
                    }
                    dest.Append(c); p++;
                }
                reach = p;
                if (parens != 0) return false;
            }
            int beforeSpace = p;
            while (p < n && (s[p] == ' ' || s[p] == '\n')) p++;
            reach = p;
            if (p < n && p > beforeSpace && (s[p] == '"' || s[p] == '\'' || s[p] == '('))
            {
                char open = s[p];
                char close = open == '(' ? ')' : open;
                int q = p + 1;
                StringBuilder t = new StringBuilder();
                while (q < n && s[q] != close)
                {
                    if (s[q] == '\\' && q + 1 < n && TextUtil.IsAsciiPunctuation(s[q + 1])) { t.Append(s[q + 1]); q += 2; continue; }
                    t.Append(s[q]); q++;
                }
                reach = q;
                if (q >= n) return false;
                title = t.ToString();
                p = q + 1;
                while (p < n && (s[p] == ' ' || s[p] == '\n')) p++;
                reach = p;
            }
            if (p >= n || s[p] != ')') return false;
            destination = DecodeEntities(dest.ToString());
            end = p + 1;
            return true;
        }

        /// <summary>Compatibility with earlier versions: allow unencoded spaces in local paths, e.g. (中文 空格/报告 1.pdf).</summary>
        private bool TryParseLooseDestination(string s, int p, out string destination, out string title, out int end)
        {
            destination = null; title = null; end = p;
            int close = NextIndexOf(s, ')', p);
            int newline = NextIndexOf(s, '\n', p);
            if (close < 0 || (newline >= 0 && newline < close)) return false;
            scanBudget -= close - p;
            string raw = s.Substring(p, close - p).Trim();
            if (raw.Length == 0) return false;
            // Real destinations are short; skip the title pattern on absurdly long ones (it can backtrack heavily).
            Match withTitle = raw.Length <= 4096 ? LooseTitle.Match(raw) : Match.Empty;
            if (withTitle.Success)
            {
                string full = raw;
                raw = withTitle.Groups[1].Value;
                int quote = full.LastIndexOfAny(new char[] { '"', '\'' });
                int open = full.IndexOf(full[quote], raw.Length);
                if (open >= 0 && quote > open) title = full.Substring(open + 1, quote - open - 1);
            }
            if (raw.StartsWith("<", StringComparison.Ordinal) && raw.EndsWith(">", StringComparison.Ordinal)) raw = raw.Substring(1, raw.Length - 2);
            destination = UnescapeString(raw);
            end = close + 1;
            return true;
        }

        private static string DecodeEntities(string value)
        {
            if (value.IndexOf('&') < 0) return value;
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                string decoded; int length;
                if (value[i] == '&' && TextUtil.TryDecodeEntity(value, i, out decoded, out length)) { builder.Append(decoded); i += length - 1; continue; }
                builder.Append(value[i]);
            }
            return builder.ToString();
        }

        internal static string UnescapeString(string value)
        {
            if (String.IsNullOrEmpty(value)) return value ?? "";
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length && TextUtil.IsAsciiPunctuation(value[i + 1])) { builder.Append(value[i + 1]); i++; continue; }
                builder.Append(value[i]);
            }
            return DecodeEntities(builder.ToString());
        }

        // ------------------------------------------------------------------ math

        private bool TryReadInlineMath(string value, int start, out string expression, out int consumed)
        {
            expression = null; consumed = 0;
            if (start < 0 || start >= value.Length) return false;
            if (value[start] == '\\' && start + 1 < value.Length && value[start + 1] == '(')
            {
                int close = FindParenMathClose(value, start + 2);
                if (close < 0) return false;
                string candidate = value.Substring(start + 2, close - start - 2).Trim();
                if (candidate.Length == 0) return false;
                expression = candidate; consumed = close + 2 - start; return true;
            }
            if (value[start] != '$' || (start > 0 && value[start - 1] == '$') || start + 1 >= value.Length || value[start + 1] == '$' || Char.IsWhiteSpace(value[start + 1])) return false;
            int end = FindDollarClose(value, start + 1);
            if (end < 0) return false;
            string content = value.Substring(start + 1, end - start - 1);
            if (IsObviousCurrency(content)) return false;
            expression = content.Replace('\n', ' '); consumed = end - start + 1; return true;
        }

        /// <summary>
        /// The first '$' after <paramref name="from"/> that can close inline math, or -1 when a blank line or
        /// the end of the text comes first. Backslash escapes are skipped.
        /// </summary>
        private int FindDollarClose(string value, int from)
        {
            TextMemo memo = MemoFor(value);
            int cached;
            if (memo.DollarClose.TryReuse(from, out cached)) return cached;
            int end = from;
            while (end < value.Length)
            {
                if (value[end] == '\\' && end + 1 < value.Length) { end += 2; continue; }
                if (value[end] == '\n' && end + 1 < value.Length && value[end + 1] == '\n') return memo.DollarClose.Remember(from, -1, end);
                if (value[end] == '$' && end > from && !Char.IsWhiteSpace(value[end - 1]) &&
                    (end + 1 >= value.Length || value[end + 1] != '$') && (end + 1 >= value.Length || !Char.IsDigit(value[end + 1])))
                    return memo.DollarClose.Remember(from, end, end);
                end++;
            }
            return memo.DollarClose.Remember(from, -1, value.Length);
        }

        private int FindParenMathClose(string value, int from)
        {
            TextMemo memo = MemoFor(value);
            int cached;
            if (memo.ParenMathClose.TryReuse(from, out cached)) return cached;
            int close = MarkdownParser.FindUnescaped(value, "\\)", from);
            return memo.ParenMathClose.Remember(from, close, close < 0 ? value.Length : close);
        }

        private int FindCommentClose(string value, int from)
        {
            TextMemo memo = MemoFor(value);
            int cached;
            if (memo.CommentClose.TryReuse(from, out cached)) return cached;
            int close = from <= value.Length ? value.IndexOf("-->", from, StringComparison.Ordinal) : -1;
            return memo.CommentClose.Remember(from, close, close < 0 ? value.Length : close);
        }

        private int NextIndexOf(string value, char c, int from)
        {
            TextMemo memo = MemoFor(value);
            ForwardSearch search;
            if (!memo.NextChar.TryGetValue(c, out search)) { search = new ForwardSearch(); memo.NextChar[c] = search; }
            int cached;
            if (search.TryReuse(from, out cached)) return cached;
            int found = from < value.Length ? value.IndexOf(c, from) : -1;
            return search.Remember(from, found, found < 0 ? value.Length : found);
        }

        // ------------------------------------------------------------------ scan memos
        //
        // Links, code spans, inline math, comments and tags search forward for their closing delimiter.
        // Text with thousands of openers and no closer ("[[[[...", "$a $b $c ...") used to repeat that search
        // from every opener, which is quadratic and froze the window for seconds. The memos below return
        // exactly what the original search returned, but each stretch of text is only scanned once.

        private sealed class ReferenceComparer : IEqualityComparer<string>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(string x, string y) { return Object.ReferenceEquals(x, y); }
            public int GetHashCode(string value) { return RuntimeHelpers.GetHashCode(value); }
        }

        /// <summary>
        /// Remembers the last forward search over one text. Openers are visited from left to right, so a search
        /// that starts inside the stretch the previous search already covered has the same answer.
        /// </summary>
        private sealed class ForwardSearch
        {
            private int from = -1, stop = -1, found = -1;

            public bool TryReuse(int query, out int result)
            {
                result = found;
                return from >= 0 && query >= from && query <= stop;
            }

            public int Remember(int query, int result, int stoppedAt)
            {
                from = query; found = result; stop = stoppedAt;
                return result;
            }
        }

        private sealed class TextMemo
        {
            /// <summary>Per '[': 0 = not scanned yet, -1 = never closed, otherwise index of the closing ']' + 1.</summary>
            public int[] LabelEnds;
            public readonly Dictionary<int, ForwardSearch> Backticks = new Dictionary<int, ForwardSearch>();
            public readonly Dictionary<string, ForwardSearch> ClosingTags = new Dictionary<string, ForwardSearch>();
            public readonly Dictionary<char, ForwardSearch> NextChar = new Dictionary<char, ForwardSearch>();
            public readonly ForwardSearch DollarClose = new ForwardSearch();
            public readonly ForwardSearch ParenMathClose = new ForwardSearch();
            public readonly ForwardSearch CommentClose = new ForwardSearch();
        }

        private readonly Dictionary<string, TextMemo> memos = new Dictionary<string, TextMemo>(ReferenceComparer.Instance);
        private long scanBudget;

        private TextMemo MemoFor(string s)
        {
            TextMemo memo;
            if (!memos.TryGetValue(s, out memo)) { memo = new TextMemo(); memos[s] = memo; }
            return memo;
        }

        /// <summary>
        /// Index of the ']' that closes the '[' at <paramref name="start"/>, or -1. Backslash escapes and code spans
        /// are skipped. One pass also records the closing bracket of every '[' it steps on, so later queries are free.
        /// </summary>
        private int FindLabelEnd(string s, int start)
        {
            TextMemo memo = MemoFor(s);
            if (memo.LabelEnds == null) memo.LabelEnds = new int[s.Length];
            int known = memo.LabelEnds[start];
            if (known != 0) return known > 0 ? known - 1 : -1;
            List<int> open = new List<int>();
            open.Add(start);
            int n = s.Length;
            int k = start + 1;
            while (k < n && open.Count > 0)
            {
                char ch = s[k];
                if (ch == '\\') { k += 2; continue; }
                if (ch == '`')
                {
                    int run = CountRun(s, k, '`');
                    int close = FindClosingBackticks(s, k + run, run);
                    k = close < 0 ? k + run : close + run;
                    continue;
                }
                if (ch == '[') open.Add(k);
                else if (ch == ']')
                {
                    int opener = open[open.Count - 1];
                    open.RemoveAt(open.Count - 1);
                    memo.LabelEnds[opener] = k + 1;
                }
                k++;
            }
            foreach (int unclosed in open) memo.LabelEnds[unclosed] = -1;
            known = memo.LabelEnds[start];
            return known > 0 ? known - 1 : -1;
        }

        private static bool IsObviousCurrency(string value)
        {
            return Regex.IsMatch(value.Trim(), @"^[+-]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d{1,2})?$");
        }
    }
}
