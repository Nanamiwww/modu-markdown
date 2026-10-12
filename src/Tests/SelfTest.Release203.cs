using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using CodexMarkdownReader.Core;
using CodexMarkdownReader.Markdown;
using CodexMarkdownReader.Rendering;
using CodexMarkdownReader.UI;
using MdBlock = CodexMarkdownReader.Markdown.Block;
using MdInline = CodexMarkdownReader.Markdown.Inline;

namespace CodexMarkdownReader.Tests
{
    // Regression checks for the v2.0.2 audit (F01–F10). Each check fails on 2.0.2.
    internal static partial class SelfTest
    {
        // ------------------------------------------------------------------ F01: questions before overwriting

        private static void SaveRaceChecks(string temp)
        {
            string folder = Path.Combine(temp, "audit202-save");
            Directory.CreateDirectory(folder);
            ThemedDialog.TestResponder = AuditResponder;
            MainWindow window = null;
            try
            {
                // 1. The "switch to UTF-8" question used to come after the check for changes made by other programs:
                // whatever another program wrote while it was open was overwritten.
                string encodingFile = Path.Combine(folder, "gbk.md");
                File.WriteAllBytes(encodingFile, Encoding.GetEncoding(936).GetBytes("中文内容"));
                window = new MainWindow(encodingFile, null, false);
                window.SetSaveEncodingForTest(936, false);
                window.SourceBoxForTest.AppendText(" \U0001F600");
                ThemedDialog.TestLog.Clear();
                ThemedDialog.TestResponder = delegate(string heading)
                {
                    if (heading.StartsWith("无法用当前编码保存", StringComparison.Ordinal))
                    {
                        File.WriteAllText(encodingFile, "其他程序在提问期间写入", new UTF8Encoding(false));
                        return 0;
                    }
                    return AuditResponder(heading);
                };
                bool saved = window.SaveForTest();
                Check("[F01] 询问是否改用 UTF-8 时其他程序写入了文件：随后仍会发现，取消后保留对方的内容",
                    !saved && ThemedDialog.TestLog.Count == 2 && ThemedDialog.TestLog[0].StartsWith("无法用当前编码保存", StringComparison.Ordinal) &&
                    ThemedDialog.TestLog[1] == "磁盘上的文件已被修改" && File.ReadAllText(encodingFile, Encoding.UTF8) == "其他程序在提问期间写入");
                Check("[F01] 没有保存时保存编码不变（仍是 GBK）", window.FormatForTest.CodePage == 936);
                ThemedDialog.TestResponder = delegate(string heading) { return heading == "磁盘上的文件已被修改" ? 0 : AuditResponder(heading); };
                bool utf8 = window.SaveForTest();
                Check("[F01] 同意改用 UTF-8 并确认覆盖后正常保存，字符完整", utf8 && window.FormatForTest.CodePage == 65001 &&
                    File.ReadAllText(encodingFile, Encoding.UTF8) == window.SourceBoxForTest.Text);
                ThemedDialog.TestResponder = AuditResponder;
                CloseQuietly(window); window = null;

                // 2. The file changes again while "overwrite?" is open: ask again instead of writing.
                string conflictFile = WriteText(folder, "conflict.md", "BASE");
                window = new MainWindow(conflictFile, null, false);
                window.SourceBoxForTest.AppendText(" edit");
                File.WriteAllText(conflictFile, "EXTERNAL A", new UTF8Encoding(false));
                int prompts = 0;
                ThemedDialog.TestResponder = delegate(string heading)
                {
                    if (heading != "磁盘上的文件已被修改") return AuditResponder(heading);
                    prompts++;
                    if (prompts > 1) return 2;
                    File.WriteAllText(conflictFile, "EXTERNAL B", new UTF8Encoding(false));
                    return 0;
                };
                bool overwritten = window.SaveForTest();
                Check("[F01] 确认覆盖的同时文件又被修改：再问一次，取消后保留最新的外部版本",
                    !overwritten && prompts == 2 && File.ReadAllText(conflictFile, Encoding.UTF8) == "EXTERNAL B");
                ThemedDialog.TestResponder = delegate(string heading) { return heading == "磁盘上的文件已被修改" ? 0 : AuditResponder(heading); };
                bool confirmed = window.SaveForTest();
                Check("[F01] 再次确认覆盖、期间没有新修改时正常保存", confirmed && File.ReadAllText(conflictFile, Encoding.UTF8) == "BASE edit");
                ThemedDialog.TestResponder = AuditResponder;
                CloseQuietly(window); window = null;

                // 3. The same while the question about undecodable bytes is open.
                string lossyFile = Path.Combine(folder, "lossy.md");
                File.WriteAllBytes(lossyFile, new byte[] { 0xEF, 0xBB, 0xBF, 0x41, 0xC3, 0x28 });
                window = new MainWindow(lossyFile, null, false);
                window.SourceBoxForTest.AppendText("!");
                ThemedDialog.TestLog.Clear();
                ThemedDialog.TestResponder = delegate(string heading)
                {
                    if (heading != "文件中有无法解码的字节") return AuditResponder(heading);
                    File.WriteAllText(lossyFile, "EXTERNAL DURING QUESTION", new UTF8Encoding(false));
                    return 0;
                };
                bool lossySaved = window.SaveForTest();
                Check("[F01] 询问无法解码的字节时其他程序写入了文件：随后仍会发现，取消后保留对方的内容",
                    !lossySaved && ThemedDialog.TestLog.Contains("磁盘上的文件已被修改") && File.ReadAllText(lossyFile, Encoding.UTF8) == "EXTERNAL DURING QUESTION");
                ThemedDialog.TestResponder = AuditResponder;
                CloseQuietly(window); window = null;

                // 4. The file cannot be read (another program holds it exclusively): an unknown version is not
                // permission to overwrite.
                string lockedFile = WriteText(folder, "locked.md", "LOCKED BASE");
                window = new MainWindow(lockedFile, null, false);
                window.SourceBoxForTest.AppendText(" edit");
                ThemedDialog.TestLog.Clear();
                bool lockedSaved;
                using (FileStream exclusive = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    lockedSaved = window.SaveForTest();
                }
                Check("[F01] 文件被其他程序独占、无法确认版本时先询问，取消后不写入",
                    !lockedSaved && ThemedDialog.TestLog.Contains("无法读取磁盘上的文件") && File.ReadAllText(lockedFile, Encoding.UTF8) == "LOCKED BASE");
            }
            finally
            {
                ThemedDialog.TestResponder = AuditResponder;
                CloseQuietly(window);
                ThemedDialog.TestResponder = null;
            }
        }

        // ------------------------------------------------------------------ F02: parser hot spots

        /// <summary>"--render-probe file parse": parses only and reports the time, in its own process.</summary>
        private static int ParseProbe(string file)
        {
            try
            {
                string markdown = File.ReadAllText(file, Encoding.UTF8);
                MarkdownParser.Parse("预热 **文字** $x$\n\n# 标题");   // JIT, not timed
                Stopwatch watch = Stopwatch.StartNew();
                MarkdownDocument document = MarkdownParser.Parse(markdown);
                watch.Stop();
                File.WriteAllLines(file + ".result", new string[]
                {
                    "ms=" + watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                    "blocks=" + document.Blocks.Count.ToString(CultureInfo.InvariantCulture),
                    "headings=" + document.Headings.Count.ToString(CultureInfo.InvariantCulture),
                    "depth=" + InlineDepth(document.Blocks).ToString(CultureInfo.InvariantCulture)
                }, new UTF8Encoding(false));
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(file + ".result", "error=" + ex.GetType().Name + ": " + ex.Message, new UTF8Encoding(false)); } catch { }
                return 3;
            }
        }

        private static void ParseSpeedCheck(string folder, string label, string name, string markdown)
        {
            int exit;
            Dictionary<string, string> fields = RunProbe(folder, name, markdown, "parse", out exit);
            long ms;
            bool timed = Int64.TryParse(Field(fields, "ms"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ms);
            string measured = timed ? ms.ToString(CultureInfo.InvariantCulture) + " ms" : Outcome(exit) + " " + Field(fields, "error");
            Check("[F02] " + label + "在 1.5 秒内解析完（实测 " + measured + "）", exit == 0 && timed && ms < 1500);
        }

        private static void ParserHotspotChecks(string temp)
        {
            string folder = Path.Combine(temp, "audit202-parser");
            Directory.CreateDirectory(folder);
            // The audit's inputs (2.0.2: 12 000 lines of "\[x" took 2.1 s and 40 000 more than 10 s; 12 000 "# a" took 2.9 s;
            // 150 000 "a* " took 11.7 s). Run in their own process, so a regression shows as a slow check, not a hang.
            ParseSpeedCheck(folder, "40000 行未闭合的 \\[x ", "bracket-math", Repeat("\\[x\n", 40000));
            ParseSpeedCheck(folder, "20000 个未闭合的 $$x 块 ", "dollar-math", Repeat("$$x\n\n", 20000));
            ParseSpeedCheck(folder, "30000 个同名标题 ", "headings", Repeat("# a\n", 30000));
            ParseSpeedCheck(folder, "25 万个 a* ", "merge", Repeat("a* ", 250000));
            ParseSpeedCheck(folder, "25000 对 *强调* ", "emphasis", Repeat("*a* ", 25000));
            ParseSpeedCheck(folder, "15000 个未闭合注释与标题交替 ", "comments", Repeat("<!--\n# a\n", 15000));

            // The faster emphasis processing must give exactly the same trees as the straightforward version.
            List<string> samples = new List<string>(new string[]
            {
                "*a **b** c*", "***a***", "**a*b*c**", "*a**b*", "_a_b_", "__a__b", "~~a~~~b~~", "~a~~b~", "**“引号”**后文", "前面*中文*后面",
                "*[链接](a.md)*", "**a `*` b**", "\\*a*", "*a\\*", "a*b*c", "a_b_c", "*a _b* c_", "**a *b** c*", "****a****", "*****a*****"
            });
            Random random = new Random(20261011);
            const string alphabet = "*_~ab 中“”\\`[]()!.";
            for (int n = 0; n < 2000; n++)
            {
                StringBuilder text = new StringBuilder();
                int tokens = 1 + random.Next(16);
                for (int t = 0; t < tokens; t++)
                {
                    char c = alphabet[random.Next(alphabet.Length)];
                    int run = c == '*' || c == '_' || c == '~' ? 1 + random.Next(3) : 1;
                    text.Append(c, run);
                }
                samples.Add(text.ToString());
            }
            int mismatches = 0;
            string first = null;
            foreach (string sample in samples)
            {
                if (DumpParse(sample, false) == DumpParse(sample, true)) continue;
                mismatches++;
                if (first == null) first = sample;
            }
            Check("[F02] 新的强调处理与原算法在 " + samples.Count.ToString(CultureInfo.InvariantCulture) + " 个样例上结果完全相同" +
                (first == null ? "" : "（首个不同：" + first + "）"), mismatches == 0);

            MarkdownDocument headings = MarkdownParser.Parse("# a\n# a\n# a-1\n# a\n# b\n# a-3\n# a");
            List<string> ids = new List<string>();
            foreach (HeadingInfo heading in headings.Headings) ids.Add(heading.Id);
            Check("[F02] 同名标题的锚点编号与原来一致（" + String.Join(" ", ids.ToArray()) + "）",
                String.Join(" ", ids.ToArray()) == "a a-1 a-1-1 a-2 b a-3 a-4");

            List<MdBlock> bracketFirst = Blocks("\\[x\n\n$$\ny\n$$");
            List<MdBlock> dollarFirst = Blocks("$$z\n\n\\[\nw\n\\]");
            List<MdBlock> repeated = Blocks("\\[a\n\n\\[b\n\n$$\nc\n$$");
            Check("[F02] 未闭合的 \\[ 不影响后面的 $$ 块，反之亦然；多个未闭合的开头都按普通文字处理",
                CountBlocks(bracketFirst, BlockKind.MathBlock) == 1 && bracketFirst[0].Kind == BlockKind.Paragraph &&
                CountBlocks(dollarFirst, BlockKind.MathBlock) == 1 && dollarFirst[0].Kind == BlockKind.Paragraph &&
                CountBlocks(repeated, BlockKind.MathBlock) == 1 && CountBlocks(repeated, BlockKind.Paragraph) == 2);
            MarkdownDocument open = MarkdownParser.Parse("<!-- x\n# a\n<!-- y\n# b");
            MarkdownDocument closed = MarkdownParser.Parse("<!--\n隐藏\n-->\n# c");
            Check("[F02] 未闭合的注释照常显示，闭合的注释仍被隐藏", open.Headings.Count == 2 && open.Blocks.Count == 4 &&
                closed.Headings.Count == 1 && closed.Blocks.Count == 1);
            List<MdBlock> nearClose = Blocks("<!--\n" + Repeat("x\n", 50) + "\n" + Repeat("y\n", 50) + "-->\n# 之后");
            List<MdBlock> farClose = Blocks("<!--\n" + Repeat("x\n", 250) + "\n-->\n# 之后");
            Check("[F02] 跨空行的注释规则不变：200 行内闭合则隐藏，超过 200 行后遇到空行则不再当作注释",
                nearClose.Count == 1 && nearClose[0].Kind == BlockKind.Heading && farClose.Count > 1 && farClose[0].Kind == BlockKind.Paragraph);
        }

        /// <summary>The parsed tree as text, with the fast or the reference emphasis processing.</summary>
        private static string DumpParse(string markdown, bool reference)
        {
            InlineParser.UseReferenceEmphasisForTest = reference;
            try
            {
                StringBuilder dump = new StringBuilder();
                DumpBlocks(MarkdownParser.Parse(markdown).Blocks, dump);
                return dump.ToString();
            }
            finally { InlineParser.UseReferenceEmphasisForTest = false; }
        }

        private static void DumpBlocks(List<MdBlock> blocks, StringBuilder dump)
        {
            if (blocks == null) return;
            foreach (MdBlock block in blocks)
            {
                dump.Append('<').Append(block.Kind.ToString()).Append('>');
                DumpInlines(block.Inlines, dump);
                DumpBlocks(block.Children, dump);
                dump.Append("</>");
            }
        }

        private static void DumpInlines(List<MdInline> inlines, StringBuilder dump)
        {
            if (inlines == null) return;
            foreach (MdInline inline in inlines)
            {
                dump.Append(inline.Kind.ToString()).Append('(').Append(inline.Text ?? "").Append('|').Append(inline.Href ?? "").Append(')');
                if (inline.Children != null && inline.Children.Count > 0)
                {
                    dump.Append('[');
                    DumpInlines(inline.Children, dump);
                    dump.Append(']');
                }
            }
        }

        // ------------------------------------------------------------------ F03: printing long code blocks

        private static void PrintPaginationChecks()
        {
            StringBuilder markdown = new StringBuilder("```text\n");
            for (int row = 1; row <= 300; row++) markdown.Append("ROW_").Append(row.ToString("000", CultureInfo.InvariantCulture)).Append('\n');
            markdown.Append("```\n");
            PreviewRenderOptions options = Options(null);
            options.PrintMode = true;
            options.FontSize = 13.5;
            RenderResult printed = new FlowDocumentRenderer(options).Render(MarkdownParser.Parse(markdown.ToString()));
            FlowDocument document = printed.Document;
            // The audit's page: Letter at 96 dpi with the reader's print margins.
            document.PageWidth = 816;
            document.PageHeight = 1056;
            document.PagePadding = new Thickness(72, 64, 72, 64);
            document.ColumnGap = 0;
            document.ColumnWidth = 816;
            DocumentPaginator paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            paginator.PageSize = new Size(816, 1056);
            paginator.ComputePageCount();
            int pages = paginator.PageCount;
            Check("[F03] 打印时 300 行的代码块接着排到后面几页（2.0.2 只有 1 页、其余被截掉；实际 " + pages.ToString(CultureInfo.InvariantCulture) + " 页）", pages >= 5);
            string text = new TextRange(document.ContentStart, document.ContentEnd).Text;
            Check("[F03] 打印的代码保留每一行（ROW_001 到 ROW_300）", text.Contains("ROW_001") && text.Contains("ROW_150") && text.Contains("ROW_300") &&
                FindLogical<TextBox>(document) == null);
            RenderResult screen = Render(markdown.ToString(), null);
            Check("[F03] 屏幕预览中的代码块不变（可选中、可复制的文本框）", screen.CodeBoxes.Count == 1 && screen.CodeBoxes[0].Text.Contains("ROW_300"));
        }

        // ------------------------------------------------------------------ F07–F10

        private static void RefreshDraftLinkChecks(string temp)
        {
            string folder = Path.Combine(temp, "audit202-refresh");
            Directory.CreateDirectory(folder);
            ThemedDialog.TestResponder = AuditResponder;
            int ansiPage = Encoding.Default.CodePage == 65001 ? 54936 : Encoding.Default.CodePage;
            Encoding ansi = Encoding.GetEncoding(ansiPage);
            MainWindow window = null, restored = null;
            try
            {
                // F07. Detected as GBK, then another program saves the file as UTF-8 with a BOM: 2.0.2 kept reading it as GBK.
                string detected = Path.Combine(folder, "detected.md");
                File.WriteAllBytes(detected, ansi.GetBytes("简体中文"));
                window = new MainWindow(detected, null, false);
                bool wasAnsi = window.FormatForTest.CodePage == ansiPage && window.SourceBoxForTest.Text == "简体中文";
                File.WriteAllBytes(detected, Concat(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes("外部程序改存为 UTF-8")));
                window.CheckDiskStateForTest();
                Check("[F07] 自动识别为 GBK 的文件被改存为 UTF-8（BOM）：刷新后文字正确，编码随之变为 UTF-8 BOM",
                    wasAnsi && window.SourceBoxForTest.Text == "外部程序改存为 UTF-8" && window.FormatForTest.CodePage == 65001 && window.FormatForTest.Bom);
                CloseQuietly(window); window = null;

                string ascii = Path.Combine(folder, "ascii.md");
                File.WriteAllBytes(ascii, ansi.GetBytes("简体中文"));
                window = new MainWindow(ascii, null, false);
                File.WriteAllBytes(ascii, Encoding.ASCII.GetBytes("plain ascii"));
                window.CheckDiskStateForTest();
                bool keptAnsi = window.SourceBoxForTest.Text == "plain ascii" && window.FormatForTest.CodePage == ansiPage;
                File.WriteAllBytes(ascii, ansi.GetBytes("又改回中文"));
                window.CheckDiskStateForTest();
                Check("[F07] 外部改成纯英文时仍按 GBK 保存，之后再写入中文也正确显示",
                    keptAnsi && window.SourceBoxForTest.Text == "又改回中文" && window.FormatForTest.CodePage == ansiPage);
                CloseQuietly(window); window = null;

                string lossy = WriteText(folder, "lossy-refresh.md", "正常的 UTF-8");
                window = new MainWindow(lossy, null, false);
                bool barBefore = window.InfoBarVisibleForTest;
                File.WriteAllBytes(lossy, new byte[] { 0xEF, 0xBB, 0xBF, 0x41, 0xC3, 0x28 });
                window.CheckDiskStateForTest();
                Check("[F07] 刷新后出现无法解码的字节：和打开文件时一样显示提示，保存前会确认",
                    !barBefore && window.InfoBarVisibleForTest && window.FormatForTest.LossyDecode);
                CloseQuietly(window); window = null;

                // F08. The file is deleted: the window holds the only copy, so the draft is written at once.
                string removed = WriteText(folder, "removed.md", "只在窗口里的内容");
                window = new MainWindow(removed, null, false);
                bool noDraftYet = !File.Exists(window.DraftPathForTest);
                File.Delete(removed);
                window.CheckDiskStateForTest();
                Check("[F08] 文件被删除后立即写入草稿，不必等到下一次编辑", noDraftYet && File.Exists(window.DraftPathForTest) &&
                    File.ReadAllText(window.DraftPathForTest, Encoding.UTF8).Contains("只在窗口里的内容"));
                CloseQuietly(window); window = null;

                // F09. Changing only the save encoding or line endings is an unsaved change: draft at once, and a recovered
                // draft that only changed the format still counts as unsaved.
                string formatOnly = WriteText(folder, "format-only.md", "只改编码");
                window = new MainWindow(formatOnly, null, false);
                window.SetSaveEncodingForTest(1200, true);
                bool encodingDraft = File.Exists(window.DraftPathForTest);
                string text = window.SourceBoxForTest.Text;
                if (encodingDraft) LeaveOrphanCopy(window);
                CloseQuietly(window); window = null;
                restored = new MainWindow(null, null, false);
                DraftInfo draft = encodingDraft ? ClaimDraft(restored, text) : null;
                bool dirty = false;
                if (draft != null)
                {
                    restored.RestoreDraftForTest(draft);
                    dirty = restored.IsDirtyForTest && restored.FormatForTest.CodePage == 1200 && restored.FormatForTest.Bom;
                }
                Check("[F09] 只改保存编码也立即写草稿；恢复这份草稿后仍算未保存，编码选择不丢", encodingDraft && draft != null && dirty);
                CloseQuietly(restored); restored = null;

                string endings = WriteText(folder, "endings.md", "第一行\r\n第二行");
                window = new MainWindow(endings, null, false);
                window.SetLineEndingForTest(LineEnding.Lf);
                Check("[F09] 只改换行符也立即写草稿", window.IsDirtyForTest && File.Exists(window.DraftPathForTest));
                CloseQuietly(window); window = null;
            }
            finally
            {
                ThemedDialog.TestResponder = AuditResponder;
                CloseQuietly(window);
                CloseQuietly(restored);
                ThemedDialog.TestResponder = null;
            }

            // F10. Legacy Office, RTF and OpenDocument files may carry macros or embedded objects: confirm before opening.
            bool legacyConfirm = true;
            foreach (string extension in new string[] { ".doc", ".xls", ".ppt", ".rtf", ".odt", ".ods", ".odp", ".docm", ".xlsm", ".pptm" })
                if (LinkPolicy.ClassifyFile("资料" + extension) != FileSafety.Confirm) legacyConfirm = false;
            Check("[F10] 旧版 Office、RTF、OpenDocument 与带宏的格式打开前先确认", legacyConfirm);
            bool modernSafe = true;
            foreach (string extension in new string[] { ".docx", ".xlsx", ".pptx", ".pdf", ".png", ".csv", ".cif", ".mol", ".jdx", ".mnova" })
                if (LinkPolicy.ClassifyFile("资料" + extension) != FileSafety.Safe) modernSafe = false;
            Check("[F10] 新版 Office、PDF、图片和常见科研数据文件仍可直接打开", modernSafe);
        }

        /// <summary>
        /// Jumps in a zoomed preview (outline, #anchors, footnotes, find): positions are measured in pixels of the viewer,
        /// but the scroll offsets of a zoomed document are not. Before 2.0.3 a heading clicked at 150% could land at the
        /// wrong place or at the top of the document.
        /// </summary>
        private static void PreviewZoomScrollChecks()
        {
            StringBuilder filler = new StringBuilder();
            for (int k = 0; k < 40; k++) filler.Append("正文内容 text ");
            foreach (int zoom in new int[] { 100, 150, 200 })
            {
                FlowDocument document = new FlowDocument();
                document.PagePadding = new Thickness(40, 26, 40, 72);
                List<Paragraph> headings = new List<Paragraph>();
                for (int i = 0; i < 10; i++)
                {
                    Paragraph heading = new Paragraph(new Run("第 " + i.ToString(CultureInfo.InvariantCulture) + " 节"));
                    heading.FontSize = 22;
                    document.Blocks.Add(heading);
                    headings.Add(heading);
                    for (int j = 0; j < 4; j++) document.Blocks.Add(new Paragraph(new Run(filler.ToString())));
                }
                FlowDocumentScrollViewer viewer = new FlowDocumentScrollViewer();
                viewer.Document = document;
                viewer.Zoom = zoom;
                Size size = new Size(720, 480);
                viewer.Measure(size);
                viewer.Arrange(new Rect(size));
                viewer.UpdateLayout();
                ScrollViewer scroll = MainWindow.FindVisualChild<ScrollViewer>(viewer);
                double margin = 18 * zoom / 100.0;
                StringBuilder detail = new StringBuilder();
                bool ok = scroll != null && scroll.ScrollableHeight > 0;
                if (scroll != null)
                {
                    detail.Append("偏移单位/像素 ").Append(PreviewScrolling.UnitsPerPixel(scroll).ToString("0.00", CultureInfo.InvariantCulture)).Append("；标题→顶部距离");
                    foreach (int target in new int[] { 7, 2, 9, 4, 0 })
                    {
                        PreviewScrolling.ScrollToTop(scroll, headings[target], margin);
                        viewer.UpdateLayout();
                        double top = PreviewScrolling.ViewTop(headings[target]);
                        detail.Append(' ').Append(target).Append(':').Append(Double.IsNaN(top) ? "?" : top.ToString("0", CultureInfo.InvariantCulture));
                        bool atStart = scroll.VerticalOffset <= 0.5, atEnd = scroll.VerticalOffset >= scroll.ScrollableHeight - 0.5;
                        bool placed = !Double.IsNaN(top) && (Math.Abs(top - margin) <= 4 || (atStart && top >= margin - 4) ||
                            (atEnd && top > margin && top < PreviewScrolling.ViewportPixels(scroll)));
                        if (!placed) ok = false;
                    }
                }
                Check("预览缩放 " + zoom.ToString(CultureInfo.InvariantCulture) + "% 时跳到标题，标题停在顶部（" + detail.ToString() + "）", ok);
            }
        }
    }
}
