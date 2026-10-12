using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexMarkdownReader.Core;
using CodexMarkdownReader.Markdown;
using CodexMarkdownReader.Rendering;
using LinkTarget = CodexMarkdownReader.Rendering.LinkTarget;
using CodexMarkdownReader.UI;
using MdBlock = CodexMarkdownReader.Markdown.Block;
using MdInline = CodexMarkdownReader.Markdown.Inline;

namespace CodexMarkdownReader.Tests
{
    /// <summary>
    /// In-process regression checks, run by build.ps1 through "--self-test [report]".
    /// Every check is named so a failure says exactly what broke.
    /// </summary>
    internal static partial class SelfTest
    {
        private static readonly List<string> Report = new List<string>();
        private static int failures;
        private static int passes;
        private static string reportFile;

        public static int Run(string reportPath)
        {
            reportFile = reportPath ?? Path.Combine(Path.GetTempPath(), "CodexMarkdownReader-selftest.txt");
            string temp = Path.Combine(Path.GetTempPath(), "CodexMarkdownReader-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            AppSettings.TestDataDirectory = Path.Combine(temp, "profile");
            ReaderApp.InitializeForTests(new AppSettings());
            try
            {
                Group("解析：列表", ParserLists);
                Group("解析：行内", ParserInlines);
                Group("解析：块", ParserBlocks);
                Group("解析：公式", ParserMath);
                Group("渲染：预览", delegate { RendererChecks(temp); });
                Group("链接安全", delegate { LinkChecks(temp); });
                Group("编码与换行", CodecChecks);
                Group("草稿恢复", DraftChecks);
                Group("设置", SettingsChecks);
                Group("界面", UiChecks);
                Group("主窗口", delegate { WindowChecks(temp); });
                Group("导出", ExportChecks);
                Group("审计问题：文件与草稿", delegate { AuditDocumentChecks(temp); });
                Group("审计问题：多窗口与大纲", delegate { AuditWindowChecks(temp); });
                Group("审计问题：链接、编码、导出与图片", delegate { AuditResourceChecks(temp); });
                Group("审计问题：解析速度", AuditParserSpeedChecks);
                Group("审计问题：公式嵌套", AuditMathDepthChecks);
                Group("复审问题：强调嵌套过深", delegate { ReviewNestingChecks(temp); });
                Group("复审问题：草稿的文件基线与有损标记", delegate { ReviewDraftChecks(temp); });
                Group("复审问题：选定的读取编码", delegate { ReviewEncodingChecks(temp); });
                Group("复审问题：极窄长图", delegate { ReviewImageChecks(temp); });
                Group("2.0.2 审计：保存前的确认（F01）", delegate { SaveRaceChecks(temp); });
                Group("2.0.2 审计：解析速度与结果（F02）", delegate { ParserHotspotChecks(temp); });
                Group("2.0.2 审计：打印长代码块（F03）", PrintPaginationChecks);
                Group("2.0.2 审计：刷新、草稿与链接（F07–F10）", delegate { RefreshDraftLinkChecks(temp); });
                Group("公式排版：参数、空格与字体（F04–F06）", MathSyntaxChecks);
                Group("公式排版：括号、矩阵、对齐、编号与自定义命令", MathStructureChecks);
                Group("化学式（\\ce）", ChemistryChecks);
                Group("单位与数值（\\pu、siunitx）", UnitChecks);
                Group("公式导出与稳健性", delegate { MathExportChecks(temp); });
                Group("预览缩放后的跳转", PreviewZoomScrollChecks);
                Group("2.0.3 复审：单位中的空白（F11）", delegate { UnitWhitespaceChecks(temp); });
                Group("2.0.3 复审：不确定度与共同指数（F12）", UncertaintyChecks);
                Group("2.0.3 复审：宏参数与 \\let（F13、F14）", MacroSemanticsChecks);
                Group("2.0.3 复审：编号与引用（F15）", delegate { LabelChecks(temp); });
                Group("正文里用 ~ 写的数字范围", RangeTextChecks);
                Group("2.0.4 复审：宏参数、注释与转义（F16、F20、F22）", MacroArgumentChecks);
                Group("2.0.4 复审：数值、单位与化学文字（F17–F19、F21、F24）", ScienceInputChecks);
                Group("2.0.4 复审：长段落的粗体（F23）", delegate { LongEmphasisChecks(temp); });
                Group("2.0.5 复审：乘积、商、范围与角度中的不确定度（F17）", ProductUncertaintyChecks);
                Group("2.0.5 复审：方括号参数里的注释（F20）", BracketCommentChecks);
                Group("2.0.5 复审：单位幂次的精确位数（F18）", PowerPrecisionChecks);
                Group("2.0.5 复审：剩余问题与扩展清单对照", ExtendedAuditChecks);
                Group("2.0.6 复审：除数中的科学计数（F17）", DivisorChecks);
                Group("2.0.6 复审：注释与命令名的边界（F20）", CommentBoundaryChecks);
                Group("2.0.6 复审：\\text 与其他参数里的注释", CommentArgumentChecks);
                Group("2.0.6 复审：剩余问题、扩展清单与 120 组精确幂次", ExtendedAudit206Checks);
                Group("2.0.7 复审：数值参数里的注释（F25）", NumberCommentChecks);
                Group("2.0.7 复审：F25 与扩展清单对照", ExtendedAudit207Checks);
            }
            finally
            {
                ThemedDialog.TestResponder = null;
                try { Directory.Delete(temp, true); } catch { }
            }
            WriteReport(null);
            return Math.Min(failures, 100);
        }

        private static string Summary()
        {
            return "墨读 Markdown " + ReaderApp.Version + " 自检：" + passes.ToString(CultureInfo.InvariantCulture) + " 项通过，" + failures.ToString(CultureInfo.InvariantCulture) + " 项失败";
        }

        /// <summary>Writes the report. While running, a partial report is written after every group, so a crash still leaves a record.</summary>
        private static void WriteReport(string note)
        {
            List<string> lines = new List<string>();
            lines.Add(Summary() + (note ?? ""));
            lines.AddRange(Report);
            try { File.WriteAllLines(reportFile, lines.ToArray(), new UTF8Encoding(true)); } catch { }
        }

        private static void Group(string name, Action body)
        {
            Report.Add("");
            Report.Add("[" + name + "]");
            try { body(); }
            catch (Exception ex)
            {
                failures++;
                Report.Add("  失败  （异常）" + ex.GetType().Name + ": " + ex.Message);
                Report.Add("        " + (ex.StackTrace ?? "").Replace("\n", "\n        "));
            }
            WriteReport("（进行中，最后完成：" + name + "）");
        }

        private static void Check(string name, bool condition)
        {
            if (condition) { passes++; Report.Add("  通过  " + name); }
            else { failures++; Report.Add("  失败  " + name); }
        }

        private static void Check(string name, Func<bool> condition)
        {
            bool ok;
            try { ok = condition(); }
            catch (Exception ex) { failures++; Report.Add("  失败  " + name + "（异常 " + ex.GetType().Name + ": " + ex.Message + "）"); return; }
            Check(name, ok);
        }

        // ------------------------------------------------------------------ helpers

        private static List<MdBlock> Blocks(string markdown) { return MarkdownParser.Parse(markdown).Blocks; }

        private static int CountInlines(IEnumerable<MdInline> inlines, InlineKind kind)
        {
            int count = 0;
            if (inlines == null) return 0;
            foreach (MdInline inline in inlines)
            {
                if (inline.Kind == kind) count++;
                count += CountInlines(inline.Children, kind);
            }
            return count;
        }

        private static int CountInlinesDeep(IEnumerable<MdBlock> blocks, InlineKind kind)
        {
            int count = 0;
            foreach (MdBlock block in blocks)
            {
                count += CountInlines(block.Inlines, kind);
                if (block.HeaderCells != null) foreach (List<MdInline> cell in block.HeaderCells) count += CountInlines(cell, kind);
                if (block.Rows != null) foreach (List<List<MdInline>> row in block.Rows) foreach (List<MdInline> cell in row) count += CountInlines(cell, kind);
                count += CountInlinesDeep(block.Children, kind);
            }
            return count;
        }

        private static int CountBlocks(IEnumerable<MdBlock> blocks, BlockKind kind)
        {
            int count = 0;
            foreach (MdBlock block in blocks)
            {
                if (block.Kind == kind) count++;
                count += CountBlocks(block.Children, kind);
            }
            return count;
        }

        private static string Plain(string markdown)
        {
            List<MdBlock> blocks = Blocks(markdown);
            return blocks.Count == 0 ? "" : MdInline.PlainText(blocks[0].Inlines);
        }

        // ------------------------------------------------------------------ parser

        private static void ParserLists()
        {
            List<MdBlock> loose = Blocks("1. 第一步\n\n2. 第二步\n\n3. 第三步");
            Check("空行分隔的有序列表是同一个列表（不再全部显示为 1.）", loose.Count == 1 && loose[0].Kind == BlockKind.List && loose[0].Children.Count == 3 && !loose[0].Tight);
            List<MdBlock> start = Blocks("3. 三\n4. 四");
            Check("有序列表保留起始编号 3", start.Count == 1 && start[0].Start == 3);
            List<MdBlock> nested = Blocks("- 一\n  - 二\n    - 三");
            Check("嵌套列表保持层级", nested.Count == 1 && nested[0].Children.Count == 1 && CountBlocks(nested, BlockKind.List) == 3);
            List<MdBlock> fourSpace = Blocks("1. 步骤\n\n    - 子项");
            Check("空行后缩进 4 格的子列表不会变成代码块", CountBlocks(fourSpace, BlockKind.List) == 2 && CountBlocks(fourSpace, BlockKind.CodeBlock) == 0);
            List<MdBlock> withCode = Blocks("1. 运行：\n\n   ```bash\n   npm i\n   ```\n2. 下一步");
            Check("列表项中的代码块留在列表内，编号继续", withCode.Count == 1 && withCode[0].Children.Count == 2 && CountBlocks(withCode, BlockKind.CodeBlock) == 1);
            List<MdBlock> continuation = Blocks("- 第一行\n继续同一项");
            Check("列表项的续行留在同一项", continuation.Count == 1 && continuation[0].Children.Count == 1 && Plain("- a\nb").Length >= 0 &&
                MdInline.PlainText(continuation[0].Children[0].Children[0].Inlines).Contains("继续同一项"));
            List<MdBlock> tasks = Blocks("- [x] 完成\n- [ ] 待办");
            Check("任务列表", tasks.Count == 1 && tasks[0].Children[0].IsTask && tasks[0].Children[0].TaskChecked && tasks[0].Children[1].IsTask && !tasks[0].Children[1].TaskChecked);
            Check("“1.5 倍”这类文字不会被当成列表", Blocks("1.5 倍速度")[0].Kind == BlockKind.Paragraph);
        }

        private static void ParserInlines()
        {
            List<MdBlock> snake = Blocks("变量 snake_case_name 和 __init__ 方法");
            Check("snake_case_name 不会被当成斜体", CountInlinesDeep(snake, InlineKind.Emphasis) == 0 && Plain("snake_case_name").Contains("snake_case_name"));
            List<MdBlock> emphasis = Blocks("*斜* **粗** _下划线斜_ ~~删除~~");
            Check("常规强调、加粗与删除线", CountInlinesDeep(emphasis, InlineKind.Emphasis) == 2 && CountInlinesDeep(emphasis, InlineKind.Strong) == 1 && CountInlinesDeep(emphasis, InlineKind.Strikethrough) == 1);
            Check("中文引号紧贴 ** 也能加粗", CountInlinesDeep(Blocks("前面**“引号”**后面"), InlineKind.Strong) == 1);
            Check("中文句中加粗", CountInlinesDeep(Blocks("这是**重点**内容"), InlineKind.Strong) == 1);
            List<MdBlock> reference = Blocks("见 [官网][site] 与 [site]。\n\n[site]: https://example.com \"标题\"");
            Check("引用式链接", CountInlinesDeep(reference, InlineKind.Link) == 2 && reference.Count == 1);
            MarkdownDocument footnoteDoc = MarkdownParser.Parse("正文[^1]。\n\n[^1]: 脚注内容");
            Check("脚注", CountInlinesDeep(footnoteDoc.Blocks, InlineKind.FootnoteRef) == 1 && footnoteDoc.UsedFootnotes.Count == 1 && footnoteDoc.Blocks.Count == 1);
            List<MdBlock> bare = Blocks("见https://example.com/a。后文");
            List<MdBlock> bareSpaced = Blocks("见 https://example.com/a。后文");
            Check("裸网址自动识别且不吞掉中文", CountInlinesDeep(bareSpaced, InlineKind.Link) == 1 && FirstHref(bareSpaced) == "https://example.com/a" && CountInlinesDeep(bare, InlineKind.Link) <= 1);
            List<MdBlock> html = Blocks("a<br>b <sub>2</sub> <kbd>Ctrl</kbd> <script>x</script>");
            Check("安全 HTML 子集（br/sub/kbd），其余标签原样显示", CountInlinesDeep(html, InlineKind.HardBreak) == 1 && CountInlinesDeep(html, InlineKind.Subscript) == 1 &&
                CountInlinesDeep(html, InlineKind.Keyboard) == 1 && Plain("<script>x</script>").Contains("<script>"));
            Check("中文软换行不插入空格", Plain("中文\n继续") == "中文继续");
            Check("英文软换行保留空格", CountInlinesDeep(Blocks("English\nnext"), InlineKind.SoftBreak) == 1);
            List<MdBlock> spaces = Blocks("[报告](中文 空格/报告 1.pdf)");
            Check("兼容旧写法：链接路径中含空格", CountInlinesDeep(spaces, InlineKind.Link) == 1 && FirstHref(spaces) == "中文 空格/报告 1.pdf");
            Check("行内代码中的符号保持原样", Plain("`**不加粗** $x$`") == "**不加粗** $x$");
            Check("HTML 实体", Plain("&copy; &lt;tag&gt; &#x4E2D;") == "© <tag> 中");
        }

        private static string FirstHref(IEnumerable<MdBlock> blocks)
        {
            foreach (MdBlock block in blocks)
            {
                string found = FirstHref(block.Inlines);
                if (found != null) return found;
                found = FirstHref(block.Children);
                if (found != null) return found;
            }
            return null;
        }

        private static string FirstHref(List<MdInline> inlines)
        {
            if (inlines == null) return null;
            foreach (MdInline inline in inlines)
            {
                if (inline.Kind == InlineKind.Link) return inline.Href;
                string found = FirstHref(inline.Children);
                if (found != null) return found;
            }
            return null;
        }

        private static void ParserBlocks()
        {
            List<MdBlock> setext = Blocks("标题一\n===\n\n标题二\n---");
            Check("下划线式标题（=== / ---）", setext.Count == 2 && setext[0].Kind == BlockKind.Heading && setext[0].Level == 1 && setext[1].Level == 2);
            List<MdBlock> table = Blocks("| 左 | 中 | 右 |\n|:--|:-:|--:|\n| 1 | 2 | 3 |");
            Check("表格对齐", table.Count == 1 && table[0].Kind == BlockKind.Table && table[0].Aligns[0] == TableAlign.Left && table[0].Aligns[1] == TableAlign.Center && table[0].Aligns[2] == TableAlign.Right);
            Check("短分隔行 |-|-| 的表格", Blocks("|a|b|\n|-|-|\n|1|2|")[0].Kind == BlockKind.Table);
            List<MdBlock> pipe = Blocks("| 代码 |\n|---|\n| `x|y` |");
            Check("表格单元格中代码里的竖线", pipe[0].Kind == BlockKind.Table && pipe[0].Rows.Count == 1 && pipe[0].Aligns.Count == 1);
            List<MdBlock> quote = Blocks("> # 标题\n> - 列表\n>\n> > 内层");
            Check("引用中的标题、列表和嵌套引用", quote.Count == 1 && quote[0].Kind == BlockKind.Quote && CountBlocks(quote[0].Children, BlockKind.Heading) == 1 &&
                CountBlocks(quote[0].Children, BlockKind.List) == 1 && CountBlocks(quote[0].Children, BlockKind.Quote) == 1);
            Check("GitHub 提示块", Blocks("> [!WARNING]\n> 小心")[0].AlertKind == "WARNING");
            Check("HTML 注释块被隐藏", Blocks("<!-- 隐藏 -->\n\n正文").Count == 1);
            MarkdownDocument ids = MarkdownParser.Parse("# 标题\n## 标题\n## Hello World");
            Check("标题锚点唯一", ids.Headings.Count == 3 && ids.Headings[0].Id == "标题" && ids.Headings[1].Id == "标题-1" && ids.Headings[2].Id == "hello-world");
            Check("段落后紧跟 --- 是二级标题而不是正文", Blocks("文字\n---")[0].Kind == BlockKind.Heading);
            Check("分隔线", Blocks("文字\n\n***")[1].Kind == BlockKind.ThematicBreak);
            Check("围栏代码块保留内容", Blocks("```csharp\nvar x = 1;\n```")[0].RawText == "var x = 1;");
        }

        private static void ParserMath()
        {
            List<MdBlock> math = Blocks("行内 $x_i+y_i$ 与 \\(\\sqrt{a^2+b^2}\\)。\n\n$$ D=\\frac{d\\bar{\\varepsilon}}{dm} $$\n\n\\[\n\\sum_{i=1}^{n} x_i\n\\]");
            Check("行内公式与块级公式", CountInlinesDeep(math, InlineKind.Math) == 2 && CountBlocks(math, BlockKind.MathBlock) == 2);
            List<MdBlock> literal = Blocks("`$x_i$`、\\$5、$19.99$。\n\n```\n$$ x_i $$\n```");
            Check("代码、转义美元符号和金额不被当成公式", CountInlinesDeep(literal, InlineKind.Math) == 0 && CountBlocks(literal, BlockKind.MathBlock) == 0);
            Check("表格中的公式", CountInlinesDeep(Blocks("| 公式 |\n|---|\n| $\\frac{a}{b}$ |"), InlineKind.Math) == 1);
        }

        // ------------------------------------------------------------------ renderer

        private static PreviewRenderOptions Options(string baseDirectory)
        {
            PreviewRenderOptions options = new PreviewRenderOptions();
            options.Palette = Palette.Create(false);
            options.BaseDirectory = baseDirectory;
            options.Images = new ImageCache();
            return options;
        }

        private static RenderResult Render(string markdown, string baseDirectory)
        {
            return new FlowDocumentRenderer(Options(baseDirectory)).Render(MarkdownParser.Parse(markdown));
        }

        private static int CountAutomationId(DependencyObject root, string automationId)
        {
            if (root == null) return 0;
            int count = String.Equals(AutomationProperties.GetAutomationId(root), automationId, StringComparison.Ordinal) ? 1 : 0;
            try
            {
                foreach (object child in LogicalTreeHelper.GetChildren(root))
                {
                    DependencyObject dependency = child as DependencyObject;
                    if (dependency != null) count += CountAutomationId(dependency, automationId);
                }
            }
            catch { }
            return count;
        }

        private static T FindLogical<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            T match = root as T;
            if (match != null) return match;
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                DependencyObject dependency = child as DependencyObject;
                T found = FindLogical<T>(dependency);
                if (found != null) return found;
            }
            return null;
        }

        private static void WritePng(string path, int width, int height)
        {
            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 60; pixels[i + 1] = 120; pixels[i + 2] = 0; pixels[i + 3] = 255; }
            BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }

        private static void RendererChecks(string temp)
        {
            RenderResult sample = Render("# 标题\n\n普通 **粗体**、*斜体*、~~删除~~ 和 `代码`。\n\n- [x] 任务\n- 列表\n\n> 引用\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n```csharp\nvar x = 1;\n```", null);
            bool hasList = false, hasTable = false, hasCode = false, hasBold = false, hasSection = false;
            foreach (System.Windows.Documents.Block block in sample.Document.Blocks)
            {
                if (block is List) hasList = true;
                if (block is Table) hasTable = true;
                if (block is BlockUIContainer) hasCode = true;
                if (block is Section) hasSection = true;
                Paragraph paragraph = block as Paragraph;
                if (paragraph != null) foreach (System.Windows.Documents.Inline inline in paragraph.Inlines) if (inline is Bold) hasBold = true;
            }
            Check("基本元素渲染（列表/表格/代码/加粗/引用）", hasList && hasTable && hasCode && hasBold && hasSection);
            Check("代码块可复制、可搜索（只读文本框）", sample.CodeBoxes.Count == 1 && sample.CodeBoxes[0].Text == "var x = 1;" && sample.CodeBoxes[0].IsReadOnly);
            Check("标题注册为锚点", sample.Anchors.ContainsKey("标题") && sample.Headings.Count == 1);

            RenderResult ordered = Render("3. 三\n\n4. 四", null);
            List orderedList = ordered.Document.Blocks.FirstBlock as List;
            Check("预览中的有序列表从 3 开始且只有一个列表", orderedList != null && orderedList.StartIndex == 3 && orderedList.ListItems.Count == 2 && ordered.Document.Blocks.Count == 1);

            RenderResult hostile = Render("<script>alert(1)</script>\n\n![remote](https://example.invalid/a.png)", null);
            Check("远程图片不自动加载，脚本不执行", FindLogical<System.Windows.Controls.Image>(hostile.Document) == null);

            RenderResult math = Render("行内 $x_i+y_i$ 与 \\(\\sqrt{a^2+b^2}\\)。\n\n$$ D=\\frac{d\\bar{\\varepsilon}}{dm} $$\n\n\\[\n\\sum_{i=1}^{n} x_i\n\\]", null);
            Check("公式渲染（2 个行内、2 个块级）", CountAutomationId(math.Document, "InlineMathFormula") == 2 && CountAutomationId(math.Document, "BlockMathFormula") == 2);

            string imageDirectory = Path.Combine(temp, "图片 目录");
            Directory.CreateDirectory(imageDirectory);
            WritePng(Path.Combine(imageDirectory, "icon.png"), 32, 16);
            WritePng(Path.Combine(imageDirectory, "wide.png"), 2600, 10);
            RenderResult icon = Render("![小图标](icon.png)", imageDirectory);
            Viewbox iconBox = FindLogical<Viewbox>(icon.Document);
            System.Windows.Controls.Image iconImage = FindLogical<System.Windows.Controls.Image>(icon.Document);
            Check("小图片保持原始像素大小（不再被放大到整栏宽）", iconBox != null && iconImage != null && Math.Abs(iconBox.MaxWidth - 32) < 0.01 && Math.Abs(iconImage.Width - 32) < 0.01);
            BitmapSource decoded; int width, height;
            ImageCache cache = new ImageCache();
            bool wideOk = cache.TryGet(Path.Combine(imageDirectory, "wide.png"), out decoded, out width, out height);
            Check("大图片按上限解码但保留原始尺寸信息", wideOk && width == 2600 && decoded.PixelWidth <= ImageCache.MaxDecodeWidth);
            BitmapSource again; int w2, h2;
            Check("图片缓存命中同一对象", cache.TryGet(Path.Combine(imageDirectory, "wide.png"), out again, out w2, out h2) && Object.ReferenceEquals(decoded, again));

            RenderResult footnote = Render("正文[^a]\n\n[^a]: 说明", null);
            Check("脚注锚点双向可跳转", footnote.Anchors.ContainsKey("fn-1") && footnote.Anchors.ContainsKey("fnref-1"));
        }

        // ------------------------------------------------------------------ links

        private static void LinkChecks(string temp)
        {
            string directory = Path.Combine(temp, "links");
            string linkedDirectory = Path.Combine(directory, "中文 空格");
            Directory.CreateDirectory(linkedDirectory);
            string pdf = Path.Combine(linkedDirectory, "报告 1.pdf");
            File.WriteAllText(pdf, "test");
            File.WriteAllText(Path.Combine(linkedDirectory, "运行.cmd"), "echo");
            File.WriteAllText(Path.Combine(linkedDirectory, "脚本.py"), "print(1)");
            File.WriteAllText(Path.Combine(linkedDirectory, "宏.docm"), "x");
            File.WriteAllText(Path.Combine(directory, "门控%20报告.md"), "# literal");
            File.WriteAllText(Path.Combine(directory, "门控 报告.md"), "# space");

            Check("绝对目录链接（file://）", LinkPolicy.Resolve(new Uri(linkedDirectory).AbsoluteUri, directory).Kind == LinkKind.LocalDirectory);
            Check("正斜杠绝对文件路径", LinkPolicy.Resolve(pdf.Replace('\\', '/'), directory).Kind == LinkKind.LocalFile);
            Check("相对目录链接", LinkPolicy.Resolve("中文%20空格/", directory).Kind == LinkKind.LocalDirectory);
            Check("相对文件链接（%20 编码）", LinkPolicy.Resolve("中文%20空格/报告%201.pdf", directory).Kind == LinkKind.LocalFile);
            LinkTarget fragment = LinkPolicy.Resolve(new Uri(pdf).AbsoluteUri + "#page=3", directory);
            Check("带片段的本地链接指向正确文件", fragment.Kind == LinkKind.LocalFile && String.Equals(fragment.Path, pdf, StringComparison.OrdinalIgnoreCase));
            Check("拒绝 .cmd", LinkPolicy.Resolve("中文%20空格/运行.cmd", directory).Kind == LinkKind.Blocked);
            Check("拒绝 .py（双击会执行）", LinkPolicy.Resolve("中文%20空格/脚本.py", directory).Kind == LinkKind.Blocked);
            LinkTarget macro = LinkPolicy.Resolve("中文%20空格/宏.docm", directory);
            Check("带宏文档需要确认", macro.Kind == LinkKind.LocalFile && macro.Safety == FileSafety.Confirm);
            Check("PDF 可直接打开", LinkPolicy.Resolve("中文%20空格/报告%201.pdf", directory).Safety == FileSafety.Safe);
            LinkKind unc = LinkPolicy.Resolve(@"\\server\share\file.md", directory).Kind;
            LinkKind uncUri = LinkPolicy.Resolve("file://server/share/file.md", directory).Kind;
            LinkKind uncEncoded = LinkPolicy.Resolve("%5C%5Cserver%5Cshare%5Cfile.md", directory).Kind;
            Check("拒绝网络共享路径", IsNotOpenable(unc) && IsNotOpenable(uncUri) && IsNotOpenable(uncEncoded));
            Check("拒绝 javascript:", IsNotOpenable(LinkPolicy.Resolve("javascript:alert(1)", directory).Kind));
            Check("网页链接", LinkPolicy.Resolve("https://example.com/docs", directory).Kind == LinkKind.Web);
            Check("文档内锚点", LinkPolicy.Resolve("#安装步骤", directory).Kind == LinkKind.Anchor && LinkPolicy.Resolve("#%E5%AE%89", directory).Fragment == "安");
            LinkTarget literalPercent = LinkPolicy.Resolve("门控%2520报告.md", directory);
            LinkTarget encodedSpace = LinkPolicy.Resolve("门控%20报告.md", directory);
            Check("%2520 指向文件名含字面 %20 的文件", literalPercent.Kind == LinkKind.MarkdownFile && literalPercent.Path.EndsWith("门控%20报告.md", StringComparison.Ordinal));
            Check("%20 指向文件名含空格的文件", encodedSpace.Kind == LinkKind.MarkdownFile && encodedSpace.Path.EndsWith("门控 报告.md", StringComparison.Ordinal));
            LinkTarget withAnchor = LinkPolicy.Resolve("门控%20报告.md#第二节", directory);
            Check("Markdown 链接带锚点", withAnchor.Kind == LinkKind.MarkdownFile && withAnchor.Fragment == "第二节");
            string shell;
            Check("打开时使用 Windows 路径而不是 file: 地址", LinkPolicy.TryGetShellTarget(LinkPolicy.Resolve(new Uri(linkedDirectory).AbsoluteUri, directory), out shell) &&
                !shell.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && String.Equals(Path.GetFullPath(shell), Path.GetFullPath(linkedDirectory), StringComparison.OrdinalIgnoreCase));
            Check("缺失目标不生成可点击链接", LinkPolicy.Resolve("不存在.md", directory).Kind == LinkKind.Missing);
        }

        private static bool IsNotOpenable(LinkKind kind)
        {
            return kind == LinkKind.Blocked || kind == LinkKind.Missing || kind == LinkKind.Invalid;
        }

        // ------------------------------------------------------------------ codec

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static void CodecChecks()
        {
            byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF, 0x61, 0x0D, 0x0A, 0x62 };
            DecodedText withBom = TextFileCodec.Decode(bom, 0);
            Check("UTF-8 BOM 读写往返不变", withBom.Format.Bom && SameBytes(TextFileCodec.Encode(withBom.Text, withBom.Format), bom));

            byte[] lf = Encoding.UTF8.GetBytes("第一行\n第二行\n");
            DecodedText lfText = TextFileCodec.Decode(lf, 0);
            Check("LF 文件保存后仍是 LF", lfText.Format.LineEnding == LineEnding.Lf && lfText.Text == "第一行\r\n第二行\r\n" && SameBytes(TextFileCodec.Encode(lfText.Text + "", lfText.Format), lf));

            Encoding gbk = Encoding.GetEncoding(936);
            byte[] gbkBytes = gbk.GetBytes("中文说明：门控");
            DecodedText gbkText = TextFileCodec.Decode(gbkBytes, 936);
            Check("GBK 文件保存后仍是 GBK", gbkText.Text == "中文说明：门控" && SameBytes(TextFileCodec.Encode(gbkText.Text, gbkText.Format), gbkBytes));
            DecodedText fallback = TextFileCodec.Decode(gbkBytes, 0);
            Check("非 UTF-8 文件自动按系统编码解码并标记", fallback.Format.DecodedWithFallback && fallback.Format.CodePage != 65001);

            bool threw = false;
            try
            {
                TextFormat gbkFormat = TextFormat.Default();
                gbkFormat.CodePage = 936;
                TextFileCodec.Encode("表情 \U0001F600", gbkFormat);
            }
            catch (UnencodableTextException) { threw = true; }
            Check("GBK 无法表示的字符会提示而不是变成问号", threw);

            byte[] mixed = Encoding.UTF8.GetBytes("a\r\nb\nc\n");
            DecodedText mixedText = TextFileCodec.Decode(mixed, 0);
            Check("识别混合换行", mixedText.Format.MixedLineEndings && mixedText.Format.LineEnding == LineEnding.Lf);
        }

        // ------------------------------------------------------------------ drafts

        private static void DraftChecks()
        {
            DraftSession crashed = new DraftSession();
            crashed.Save(@"C:\docs\a.md", "未保存的内容", TextFormat.Default());
            crashed.Dispose();   // simulates a crash: lock released, draft left behind

            DraftSession alive = new DraftSession();
            alive.Save(null, "另一个窗口正在编辑", TextFormat.Default());

            DraftSession me = new DraftSession();
            List<DraftInfo> orphans = DraftSession.ClaimOrphans(me.Id);
            Check("异常退出留下的草稿会被发现", orphans.Count == 1 && orphans[0].Text == "未保存的内容" && orphans[0].DocumentPath == @"C:\docs\a.md");
            Check("仍在运行的窗口的草稿不会被抢走", orphans.Count == 1);

            DraftSession other = new DraftSession();
            List<DraftInfo> second = DraftSession.ClaimOrphans(other.Id);
            Check("同一份草稿不会同时被两个实例认领", second.Count == 0);
            foreach (DraftInfo draft in orphans) draft.Discard();
            List<DraftInfo> afterDiscard = DraftSession.ClaimOrphans(other.Id);
            Check("丢弃后草稿文件被删除", afterDiscard.Count == 0);

            alive.Delete();
            alive.Dispose();
            me.Dispose();
            other.Dispose();

            Directory.CreateDirectory(AppSettings.DataDirectory);
            File.WriteAllText(Path.Combine(AppSettings.DataDirectory, "draft.md~"), "CODEX-MD-DRAFT-1\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes(@"C:\x.md")) + "\n旧版草稿", new UTF8Encoding(false));
            DraftSession reader = new DraftSession();
            List<DraftInfo> legacy = DraftSession.ClaimOrphans(reader.Id);
            Check("识别 v1.11 旧版草稿", legacy.Count == 1 && legacy[0].Text == "旧版草稿");
            foreach (DraftInfo draft in legacy) draft.Discard();
            reader.Dispose();
        }

        // ------------------------------------------------------------------ settings & ui

        private static void SettingsChecks()
        {
            Directory.CreateDirectory(AppSettings.DataDirectory);
            File.WriteAllLines(AppSettings.SettingsPath, new string[] { "HideSource=True", "Theme=Dark", "SplitRatio=0.31" });
            AppSettings legacy = AppSettings.Load();
            Check("旧设置“隐藏源码”迁移为预览模式", legacy.View == ViewMode.Preview && legacy.Theme == ThemeMode.Dark && Math.Abs(legacy.SplitRatio - 0.31) < 0.0001);
            legacy.AddRecent(@"C:\a.md");
            legacy.AddRecent(@"C:\b.md");
            legacy.AddRecent(@"C:\A.md");
            legacy.Save();
            AppSettings reloaded = AppSettings.Load();
            Check("最近文件去重并置顶", reloaded.RecentFiles.Count == 2 && reloaded.RecentFiles[0] == @"C:\A.md");
        }

        private static void UiChecks()
        {
            ResourceDictionary styles = StyleResources.Load();
            Check("界面样式可以加载", styles.Contains("IconButton") && styles.Contains("SegmentButton") && styles.Contains("SidebarList"));
            Palette light = Palette.Create(false), dark = Palette.Create(true);
            List<string> missing = new List<string>();
            foreach (Match match in Regex.Matches(StyleResources.Xaml, @"DynamicResource (\w+)"))
            {
                string key = match.Groups[1].Value;
                if (light.ColorOf(key) == Colors.Magenta || dark.ColorOf(key) == Colors.Magenta) missing.Add(key);
            }
            Check("样式用到的颜色在浅色和深色主题中都有定义" + (missing.Count > 0 ? "（缺少 " + String.Join(",", missing.ToArray()) + "）" : ""), missing.Count == 0);
            Check("起始页“最近打开”按钮的内容靠左对齐", ButtonContentOffset(styles, "GhostButton", true) < 40);
            Check("其他文字按钮的内容仍然居中", ButtonContentOffset(styles, "TextButton", false) > 100);

            Grid grid = new Grid();
            ColumnDefinition sourceColumn = new ColumnDefinition(), splitterColumn = new ColumnDefinition(), previewColumn = new ColumnDefinition();
            Grid sourcePanel = new Grid(), previewPanel = new Grid();
            GridSplitter splitter = new GridSplitter();
            MainWindow.ApplyColumns(sourceColumn, splitterColumn, previewColumn, sourcePanel, splitter, previewPanel, ViewMode.Preview, 0.31);
            Check("预览模式隐藏源码与分割条", sourcePanel.Visibility == Visibility.Collapsed && splitter.Visibility == Visibility.Collapsed && sourceColumn.Width.Value == 0 && previewColumn.Width.IsStar);
            MainWindow.ApplyColumns(sourceColumn, splitterColumn, previewColumn, sourcePanel, splitter, previewPanel, ViewMode.Source, 0.31);
            Check("源码模式隐藏预览", previewPanel.Visibility == Visibility.Collapsed && previewColumn.Width.Value == 0 && sourceColumn.Width.IsStar);
            MainWindow.ApplyColumns(sourceColumn, splitterColumn, previewColumn, sourcePanel, splitter, previewPanel, ViewMode.Split, 0.31);
            Check("分栏模式恢复比例", sourcePanel.Visibility == Visibility.Visible && previewPanel.Visibility == Visibility.Visible && Math.Abs(sourceColumn.Width.Value - 0.31) < 0.0001 && splitterColumn.Width.Value == 6);
            double ratio;
            Check("分栏比例计算", MainWindow.TryCalculateSplitRatio(310, 690, out ratio) && Math.Abs(ratio - 0.31) < 0.0001 && !MainWindow.TryCalculateSplitRatio(0, 690, out ratio) && !MainWindow.TryCalculateSplitRatio(Double.NaN, 690, out ratio));
            Check("中英文混排字数统计", TextUtil.CountTextUnits("门控光谱") == 4 && TextUtil.CountTextUnits("UV后的门控光谱与") == 8 && TextUtil.CountTextUnits("Latin ABC 123") == 3);
        }

        /// <summary>Left edge of a 40-px content element inside a 400-px-wide button that uses the given style.</summary>
        private static double ButtonContentOffset(ResourceDictionary styles, string styleKey, bool alignLeft)
        {
            Button button = new Button();
            button.Style = (Style)styles[styleKey];
            if (alignLeft) button.HorizontalContentAlignment = HorizontalAlignment.Left;
            Border content = new Border();
            content.Width = 40;
            content.Height = 10;
            button.Content = content;
            button.Measure(new Size(400, 40));
            button.Arrange(new Rect(0, 0, 400, 40));
            return content.TranslatePoint(new Point(0, 0), button).X;
        }

        private static void WindowChecks(string temp)
        {
            string file = Path.Combine(temp, "窗口测试.md");
            File.WriteAllText(file, "# 标题\n\n1. 一\n\n2. 二\n\n| a |\n|---|\n| 1 |\n\n$$ \\sum_{i=1}^{n} x_i $$\n\n行内 $x$ 与 <kbd>Ctrl</kbd>", new UTF8Encoding(false));
            ViewMode original = ReaderApp.Settings.View;
            foreach (ViewMode mode in new ViewMode[] { ViewMode.Split, ViewMode.Source, ViewMode.Preview })
            {
                ReaderApp.Settings.View = mode;
                MainWindow window = null;
                try
                {
                    window = new MainWindow(file, null, false);
                    Check("主窗口可在“" + mode.ToString() + "”模式下打开文件", String.Equals(window.CurrentPath, file, StringComparison.OrdinalIgnoreCase));
                }
                finally
                {
                    if (window != null) window.Close();
                }
            }
            ReaderApp.Settings.View = ViewMode.Split;
            MainWindow empty = null;
            try
            {
                empty = new MainWindow(null, null, false);
                Check("新建空白窗口（起始页）", empty.CurrentPath == null);
            }
            finally
            {
                if (empty != null) empty.Close();
                ReaderApp.Settings.View = original;
            }
        }

        private static void ExportChecks()
        {
            MarkdownDocument document = MarkdownParser.Parse("# 标题\n\n3. 三\n4. 四\n\n| a |\n|---|\n| 1 |\n\n<script>alert(1)</script>\n\n$$ \\frac{a}{b} $$");
            HtmlExporter exporter = new HtmlExporter(null, MathImageRenderer.Render);
            string html = exporter.Export(document, "测试");
            Check("HTML 导出包含标题锚点、编号和表格", html.Contains("<h1 id=\"标题\">") && html.Contains("<ol start=\"3\">") && html.Contains("<table>"));
            Check("HTML 导出转义原始脚本", !html.Contains("<script>") && html.Contains("&lt;script&gt;"));
            Check("HTML 导出内嵌公式图片", html.Contains("class=\"math math-display\" src=\"data:image/png;base64,"));
        }

        // ------------------------------------------------------------------ audit 2026-10-10 regressions
        //
        // One check per finding of the v2.0 source audit. Each one fails on the 2.0 code.

        /// <summary>Answers dialogs during the audit checks: "don't save" for the save prompt, cancel for warnings, OK otherwise.</summary>
        private static int AuditResponder(string heading)
        {
            if (heading.StartsWith("是否保存", StringComparison.Ordinal)) return 1;
            if (heading.StartsWith("磁盘上的文件已被修改", StringComparison.Ordinal)) return 2;
            if (heading.StartsWith("无法确认磁盘上的文件", StringComparison.Ordinal)) return 2;
            if (heading.StartsWith("文件中有无法解码的字节", StringComparison.Ordinal)) return 2;
            if (heading.StartsWith("无法读取磁盘上的文件", StringComparison.Ordinal)) return 2;
            return 0;
        }

        private static string WriteText(string directory, string name, string text)
        {
            string path = Path.Combine(directory, name);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

        private static void CloseQuietly(MainWindow window)
        {
            if (window == null) return;
            try { window.Close(); } catch { }
        }

        private static void AuditDocumentChecks(string temp)
        {
            string folder = Path.Combine(temp, "audit-documents");
            Directory.CreateDirectory(folder);
            ThemedDialog.TestResponder = AuditResponder;
            ThemedDialog.TestLog.Clear();
            try
            {
                // 1. Undo must stop at a document switch.
                string first = WriteText(folder, "undo-a.md", "FIRST DOCUMENT");
                string second = WriteText(folder, "undo-b.md", "SECOND DOCUMENT");
                MainWindow window = null;
                try
                {
                    window = new MainWindow(first, null, false);
                    TextBox box = window.SourceBoxForTest;
                    box.AppendText(" modified");
                    bool editUndoable = box.CanUndo;
                    window.LoadFileForTest(second);
                    bool emptyHistory = !box.CanUndo && box.Text == "SECOND DOCUMENT";
                    box.Undo();
                    bool stillSecond = box.Text == "SECOND DOCUMENT" && String.Equals(window.CurrentPath, second, StringComparison.OrdinalIgnoreCase);
                    Check("[1] 换文档后撤销不会退回上一份文档", editUndoable && emptyHistory && stillSecond);
                    box.AppendText(" typed");
                    bool undone = box.CanUndo && box.Undo() && box.Text == "SECOND DOCUMENT";
                    bool redone = box.CanRedo && box.Redo() && box.Text == "SECOND DOCUMENT typed";
                    Check("[1] 新文档里的输入仍可撤销与重做", undone && redone);
                    window.NewDocumentForTest();
                    Check("[1] 新建文档后同样不能撤销回之前的内容", !box.CanUndo && box.Text.Length == 0);
                }
                finally { CloseQuietly(window); }

                // 4. A failed save keeps the conflict baseline.
                string conflict = WriteText(folder, "conflict.md", "BASE");
                window = null;
                try
                {
                    window = new MainWindow(conflict, null, false);
                    window.SourceBoxForTest.AppendText(" + my pending changes");
                    File.SetAttributes(conflict, FileAttributes.ReadOnly);
                    ThemedDialog.TestLog.Clear();
                    bool firstSave = window.SaveForTest();
                    File.SetAttributes(conflict, FileAttributes.Normal);
                    bool reported = ThemedDialog.TestLog.Contains("无法保存文件");
                    window.CheckDiskStateForTest();
                    Check("[4] 保存失败后不会误报“已被其他程序修改”", !firstSave && reported && !window.InfoBarVisibleForTest);
                    File.WriteAllText(conflict, "CHANGED BY ANOTHER PROGRAM", new UTF8Encoding(false));
                    ThemedDialog.TestLog.Clear();
                    bool secondSave = window.SaveForTest();
                    string disk = File.ReadAllText(conflict, Encoding.UTF8);
                    Check("[4] 保存失败后再保存，外部修改仍会先确认；取消则不覆盖", !secondSave && ThemedDialog.TestLog.Contains("磁盘上的文件已被修改") && disk == "CHANGED BY ANOTHER PROGRAM");
                }
                finally
                {
                    try { File.SetAttributes(conflict, FileAttributes.Normal); } catch { }
                    CloseQuietly(window);
                }

                // 5. Restoring a crash draft keeps the old copy until the new one is on disk.
                DraftSession crashed = new DraftSession();
                crashed.Save(null, "RECOVERY CONTENT", TextFormat.Default());
                string orphanFile = crashed.DraftPath;
                crashed.Dispose();
                window = null;
                try
                {
                    window = new MainWindow(null, null, false);
                    string ownId = Path.GetFileNameWithoutExtension(window.DraftPathForTest);
                    DraftInfo orphan = null;
                    foreach (DraftInfo info in DraftSession.ClaimOrphans(ownId))
                    {
                        if (orphan == null && info.Text == "RECOVERY CONTENT") orphan = info; else info.Dispose();
                    }
                    bool found = orphan != null;
                    bool oldKept = false, newMissing = false;
                    if (found)
                    {
                        using (new FileStream(window.DraftPathForTest + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                        {
                            window.RestoreDraftForTest(orphan);
                            oldKept = File.Exists(orphanFile);
                            newMissing = !File.Exists(window.DraftPathForTest);
                        }
                    }
                    Check("[5] 新草稿写入失败时保留原草稿，内容仍在编辑器中", found && oldKept && newMissing && window.SourceBoxForTest.Text == "RECOVERY CONTENT" && window.InfoBarVisibleForTest);
                    window.SaveDraftForTest();
                    Check("[5] 新草稿写入成功后才删除原草稿", File.Exists(window.DraftPathForTest) && !File.Exists(orphanFile));
                }
                finally { CloseQuietly(window); }

                // 6. "Don't save" followed by a failed open must not switch off draft protection.
                string current = WriteText(folder, "navigate-a.md", "A");
                window = null;
                try
                {
                    window = new MainWindow(current, null, false);
                    window.SourceBoxForTest.AppendText(" unsaved");
                    window.SaveDraftForTest();
                    bool draftBefore = File.Exists(window.DraftPathForTest);
                    window.OpenAfterConfirmForTest(Path.Combine(folder, "does-not-exist.md"));
                    bool stayed = String.Equals(window.CurrentPath, current, StringComparison.OrdinalIgnoreCase) && window.SourceBoxForTest.Text == "A unsaved";
                    bool draftKept = File.Exists(window.DraftPathForTest);
                    window.SourceBoxForTest.AppendText(" and more");
                    window.SaveDraftForTest();
                    Check("[6] 选择“不保存”但新文件打开失败时，当前内容和新输入仍有草稿保护", draftBefore && stayed && draftKept && File.Exists(window.DraftPathForTest));
                }
                finally { CloseQuietly(window); }

                // 10. Saving over undecodable bytes asks first.
                string lossy = Path.Combine(folder, "lossy.md");
                byte[] original = new byte[] { 0xEF, 0xBB, 0xBF, 0x41, 0xC3, 0x28 };
                File.WriteAllBytes(lossy, original);
                window = null;
                try
                {
                    window = new MainWindow(lossy, null, false);
                    bool warned = window.InfoBarVisibleForTest;
                    window.SourceBoxForTest.AppendText("!");
                    ThemedDialog.TestLog.Clear();
                    bool saved = window.SaveForTest();
                    byte[] after = File.ReadAllBytes(lossy);
                    bool unchanged = after.Length == original.Length;
                    for (int i = 0; unchanged && i < after.Length; i++) unchanged = after[i] == original[i];
                    Check("[10] 打开有损解码的文件会提示，保存前确认，取消则原始字节不变", warned && !saved && ThemedDialog.TestLog.Contains("文件中有无法解码的字节") && unchanged);
                }
                finally { CloseQuietly(window); }
            }
            finally
            {
                ThemedDialog.TestResponder = null;
            }
        }

        private static void AuditWindowChecks(string temp)
        {
            string folder = Path.Combine(temp, "audit-windows");
            Directory.CreateDirectory(folder);
            string file = WriteText(folder, "views.md", "# Alpha\n\n正文");
            ViewMode original = ReaderApp.Settings.View;
            MainWindow first = null, second = null;
            try
            {
                // 8. Two windows in split view; both switch to preview.
                ReaderApp.Settings.View = ViewMode.Split;
                first = new MainWindow(file, null, false);
                second = new MainWindow(file, null, false);
                first.SetViewMode(ViewMode.Preview);
                second.SetViewMode(ViewMode.Preview);
                Check("[8] 两个窗口先后切换到预览，各自的布局都会更新", first.SourcePanelForTest.Visibility == Visibility.Collapsed && second.SourcePanelForTest.Visibility == Visibility.Collapsed && second.CurrentViewMode == ViewMode.Preview);
                first.SetViewMode(ViewMode.Split);
                Check("[8] 一个窗口切换视图不会改变另一个窗口", second.CurrentViewMode == ViewMode.Preview && second.SourcePanelForTest.Visibility == Visibility.Collapsed && first.SourcePanelForTest.Visibility == Visibility.Visible);
                Check("[8] 新窗口使用最近一次选择的视图", ReaderApp.Settings.View == ViewMode.Split);

                // 12. The outline follows heading text even when the anchor stays the same.
                string before = OutlineLabel(first, 0);
                first.SourceBoxForTest.Text = "# ALPHA\n\n正文";
                first.RenderForTest();
                Check("[12] 标题只改大小写时，大纲文字同步更新", before == "Alpha" && OutlineLabel(first, 0) == "ALPHA");
            }
            finally
            {
                ThemedDialog.TestResponder = AuditResponder;
                CloseQuietly(first);
                CloseQuietly(second);
                ThemedDialog.TestResponder = null;
                ReaderApp.Settings.View = original;
            }
        }

        private static string OutlineLabel(MainWindow window, int index)
        {
            ListBox outline = window.OutlineListForTest;
            if (outline == null || outline.Items.Count <= index) return null;
            ListBoxItem item = outline.Items[index] as ListBoxItem;
            TextBlock label = item == null ? null : item.Content as TextBlock;
            return label == null ? null : label.Text;
        }

        private static void AuditResourceChecks(string temp)
        {
            string folder = Path.Combine(temp, "audit-resources");
            Directory.CreateDirectory(folder);

            // 3. Network share paths are rejected before any file-system check.
            Check("[3] 编码过的网络共享链接被直接拦截", LinkPolicy.Resolve("file:///%5c%5cserver%5cshare%5cfile.md", folder).Kind == LinkKind.Blocked);
            Check("[3] 编码过的网络共享图片同样被拦截", LinkPolicy.Resolve("file:///%5C%5Cserver%5Cshare%5Cimage.png", folder).Kind == LinkKind.Blocked);
            Check("[3] 普通本地 file:// 链接不受影响", LinkPolicy.Resolve(new Uri(WriteText(folder, "local.md", "x")).AbsoluteUri, folder).Kind == LinkKind.MarkdownFile);
            BitmapSource none; int noneWidth, noneHeight;
            Check("[3] 图片缓存不读取网络共享路径", !new ImageCache().TryGet(@"\\server\share\image.png", out none, out noneWidth, out noneHeight));

            // 10. Undecodable bytes are reported.
            DecodedText broken = TextFileCodec.Decode(new byte[] { 0xEF, 0xBB, 0xBF, 0x41, 0xC3, 0x28 }, 0);
            DecodedText brokenUtf16 = TextFileCodec.Decode(new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x42 }, 0);
            DecodedText valid = TextFileCodec.Decode(Encoding.UTF8.GetPreamble().Length == 3 ? Concat(Encoding.UTF8.GetPreamble(), Encoding.UTF8.GetBytes("正常内容")) : Encoding.UTF8.GetBytes("正常内容"), 0);
            Check("[10] 带 BOM 的损坏 UTF-8 / UTF-16 会被标记为有损解码", broken.Format.LossyDecode && broken.Text.IndexOf('\uFFFD') >= 0 && brokenUtf16.Format.LossyDecode);
            Check("[10] 正常文件不会被误标", !valid.Format.LossyDecode && valid.Text == "正常内容");

            // 9. HTML export keeps remote images as links and says so.
            HtmlExporter exporter = new HtmlExporter(folder, MathImageRenderer.Render);
            string html = exporter.Export(MarkdownParser.Parse("![远程](https://example.invalid/a.png)"), "导出");
            Check("[9] 导出的 HTML 不会自动加载网络图片", html.IndexOf("<img src=\"http", StringComparison.OrdinalIgnoreCase) < 0 && html.Contains("https://example.invalid/a.png") && exporter.RemoteImageCount == 1);
            Check("[9] 导出提示如实说明未内嵌的图片", MainWindow.ExportSummary("a.html", exporter).Contains("网络图片未内嵌") && !MainWindow.ExportSummary("a.html", exporter).Contains("可单独发送"));

            // 11. Tall images are decoded within the height and pixel limits.
            string tall = Path.Combine(folder, "tall.png");
            BitmapSource tallSource = BitmapSource.Create(1000, 12000, 96, 96, PixelFormats.Gray8, null, new byte[1000 * 12000], 1000);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(tallSource));
            using (FileStream stream = File.Create(tall)) encoder.Save(stream);
            ImageCache cache = new ImageCache();
            BitmapSource decoded; int width, height;
            bool loaded = cache.TryGet(tall, out decoded, out width, out height);
            Check("[11] 1000×12000 的长图按高度上限缩小解码，并保留原始尺寸", loaded && width == 1000 && height == 12000 &&
                decoded.PixelHeight <= ImageCache.MaxDecodeHeight && (long)decoded.PixelWidth * decoded.PixelHeight <= ImageCache.MaxDecodePixels);
            Check("[11] 解码比例同时受宽、高和总像素限制，过大的图片直接不解码", ImageCache.DecodeScale(800, 600) == 1.0 &&
                Math.Abs(ImageCache.DecodeScale(1000, 100000) - 0.1) < 0.0001 && ImageCache.DecodeScale(20000, 20000) == 0);
            Check("[11] 图片缓存的总内存有上限", cache.CachedBytes > 0 && cache.CachedBytes <= ImageCache.MaxCacheBytes);
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            byte[] result = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, result, 0, a.Length);
            Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
            return result;
        }

        private static long ParseMilliseconds(string markdown)
        {
            Stopwatch watch = Stopwatch.StartNew();
            MarkdownParser.Parse(markdown);
            watch.Stop();
            return watch.ElapsedMilliseconds;
        }

        private static string Repeat(string value, int count)
        {
            StringBuilder builder = new StringBuilder(value.Length * count);
            for (int i = 0; i < count; i++) builder.Append(value);
            return builder.ToString();
        }

        private static void AuditParserSpeedChecks()
        {
            // 7. Text full of openers without closers used to take seconds (64 000 "[" ≈ 0.9 s, 256 000 ≈ 22 s).
            long brackets = ParseMilliseconds(new string('[', 256000));
            Check("[7] 25.6 万个未闭合的 [ 在 1.5 秒内解析完（实测 " + brackets.ToString(CultureInfo.InvariantCulture) + " ms）", brackets < 1500);
            long mixed = ParseMilliseconds(Repeat("[a](b $c `d <b> <!-- \\(e ", 20000));
            Check("[7] 各种未闭合的开头符号混在一起也很快（实测 " + mixed.ToString(CultureInfo.InvariantCulture) + " ms）", mixed < 1500);
            long dollars = ParseMilliseconds(Repeat("$5 和 ", 60000));
            Check("[7] 大量金额里的 $ 不会拖慢解析（实测 " + dollars.ToString(CultureInfo.InvariantCulture) + " ms）", dollars < 1500);
            long nested = ParseMilliseconds(new string('[', 30000) + new string(']', 30000));
            Check("[7] 深层嵌套的方括号也很快（实测 " + nested.ToString(CultureInfo.InvariantCulture) + " ms）", nested < 1500);

            // The memoised scans must give the same answers as before.
            List<MdInline> links = MarkdownParser.Parse("[外[内]层](a.md) 和 [二](b.md)，`[不是链接](x)`，\\[也不是](y)").Blocks[0].Inlines;
            Check("[7] 嵌套方括号、代码和转义中的链接识别结果不变", CountInlines(links, InlineKind.Link) == 2 && CountInlines(links, InlineKind.Code) == 1);
            List<MdInline> math = MarkdownParser.Parse("$a$ 与 $5 和 $6 元，\\(b\\)").Blocks[0].Inlines;
            Check("[7] 公式与金额的区分结果不变", CountInlines(math, InlineKind.Math) == 2);
        }

        private static void AuditMathDepthChecks()
        {
            // 2. Deeply nested formulas used to overflow the stack and end the process.
            // The audit's input (2500 nested braces) is over the length limit; the shorter ones below hit the depth limit.
            string audited = "$" + new string('{', 2500) + "x" + new string('}', 2500) + "$";
            RenderResult rendered = Render(audited, null);
            Check("[2] 审计用的 2500 层嵌套公式不会让程序崩溃（预览）", rendered != null && rendered.Document != null);
            string html = new HtmlExporter(null, MathImageRenderer.Render).Export(MarkdownParser.Parse(audited), "公式");
            Check("[2] 同一公式导出 HTML 也不会崩溃", html.Length > 0);
            string braces = new string('{', 1500) + "x" + new string('}', 1500);
            string roots = Repeat("\\sqrt", 700) + "x";
            string indexes = Repeat("\\sqrt[", 500) + "x" + new string(']', 500) + "y";
            Check("[2] 长度限制内的深层嵌套（花括号、\\sqrt\\sqrt…、\\sqrt[\\sqrt[…]]）改为显示公式原文",
                ShowsSource(braces) && ShowsSource(roots) && ShowsSource(indexes));
            Check("[2] 这些公式放进文档里预览也不会崩溃",
                Render("$" + braces + "$", null) != null && Render("$" + roots + "$", null) != null && Render("$" + indexes + "$", null) != null);
            Check("[2] 普通公式不受限制影响", !ShowsSource("\\frac{-b \\pm \\sqrt{b^2 - 4ac}}{2a}") && !ShowsSource(Repeat("{", 12) + "x" + Repeat("}", 12)));
        }

        /// <summary>True when the formula could not be typeset and its source text is shown instead.</summary>
        private static bool ShowsSource(string latex)
        {
            Border host = FlowDocumentRenderer.CreateMathVisual(latex, false, Brushes.Black, 16) as Border;
            return host != null && host.Child is TextBlock;
        }

        // ------------------------------------------------------------------ review 2026-10-11 regressions
        //
        // One group per finding of the v2.0.1 source review, using its reproduction inputs. Each check fails on 2.0.1.

        [DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint mode);

        /// <summary>
        /// "--render-probe file": parses, previews and exports one document in its own process and writes the result to
        /// file + ".result". Input that could end the process is tested this way, so the self-test keeps running and
        /// reports the failure instead of disappearing with it.
        /// </summary>
        internal static int RenderProbe(string file, string mode)
        {
            try { SetErrorMode(0x0002); } catch { }   // no Windows error dialog if the probe does crash
            if (String.Equals(mode, "parse", StringComparison.OrdinalIgnoreCase)) return ParseProbe(file);
            if (String.Equals(mode, "math-fuzz", StringComparison.OrdinalIgnoreCase)) return MathFuzzProbe(file);
            if (String.Equals(mode, "gallery", StringComparison.OrdinalIgnoreCase)) return GalleryProbe(file);
            string temp = Path.Combine(Path.GetTempPath(), "CodexMarkdownReader-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                AppSettings.TestDataDirectory = Path.Combine(temp, "profile");
                ReaderApp.InitializeForTests(new AppSettings());
                string markdown = File.ReadAllText(file, Encoding.UTF8);
                Stopwatch watch = Stopwatch.StartNew();
                MarkdownDocument document = MarkdownParser.Parse(markdown);
                int depth = InlineDepth(document.Blocks);
                RenderResult rendered = new FlowDocumentRenderer(Options(null)).Render(document);
                string html = new HtmlExporter(null, MathImageRenderer.Render).Export(document, "probe");
                string plain = document.Blocks.Count == 0 ? "" : MdInline.PlainText(document.Blocks[0].Inlines).Replace('\r', ' ').Replace('\n', ' ');
                watch.Stop();
                File.WriteAllLines(file + ".result", new string[]
                {
                    "depth=" + depth.ToString(CultureInfo.InvariantCulture),
                    "plain=" + (plain.Length > 200 ? plain.Substring(0, 200) : plain),
                    "rendered=" + (rendered != null && rendered.Document != null ? "1" : "0"),
                    "html=" + html.Length.ToString(CultureInfo.InvariantCulture),
                    "ms=" + watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)
                }, new UTF8Encoding(false));
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(file + ".result", "error=" + ex.GetType().Name + ": " + ex.Message, new UTF8Encoding(false)); } catch { }
                return 3;
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        /// <summary>Runs the render probe on <paramref name="markdown"/> in a separate process and returns its result fields.</summary>
        private static Dictionary<string, string> RunRenderProbe(string folder, string name, string markdown, out int exitCode)
        {
            return RunProbe(folder, name, markdown, null, out exitCode);
        }

        /// <summary>Runs the probe in <paramref name="mode"/> (null: parse, preview and export; "parse": parse only).</summary>
        private static Dictionary<string, string> RunProbe(string folder, string name, string markdown, string mode, out int exitCode)
        {
            string file = WriteText(folder, name + ".md", markdown);
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.Ordinal);
            ProcessStartInfo start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--render-probe \"" + file + "\"" + (mode == null ? "" : " " + mode));
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            using (Process process = Process.Start(start))
            {
                if (!process.WaitForExit(60000))
                {
                    try { process.Kill(); } catch { }
                    exitCode = -1;
                    return fields;
                }
                exitCode = process.ExitCode;
            }
            string resultFile = file + ".result";
            if (File.Exists(resultFile))
            {
                foreach (string line in File.ReadAllLines(resultFile, Encoding.UTF8))
                {
                    int p = line.IndexOf('=');
                    if (p > 0) fields[line.Substring(0, p)] = line.Substring(p + 1);
                }
            }
            return fields;
        }

        private static string Field(Dictionary<string, string> fields, string key)
        {
            string value;
            return fields.TryGetValue(key, out value) ? value : "";
        }

        private static bool DepthWithin(Dictionary<string, string> fields, int limit)
        {
            int depth;
            return Int32.TryParse(Field(fields, "depth"), NumberStyles.Integer, CultureInfo.InvariantCulture, out depth) && depth > 0 && depth <= limit;
        }

        private static string Outcome(int exitCode)
        {
            if (exitCode == 0) return "正常结束";
            if (exitCode == -1) return "超时";
            if (exitCode == unchecked((int)0xC00000FD)) return "栈溢出退出";
            return "退出码 " + exitCode.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Deepest nesting of inline nodes (a top-level node counts 1). Measured with an explicit stack, so the
        /// measurement itself cannot overflow even if the parser's limit were broken.
        /// </summary>
        private static int InlineDepth(IEnumerable<MdBlock> blocks)
        {
            int deepest = 0;
            Stack<KeyValuePair<MdInline, int>> pending = new Stack<KeyValuePair<MdInline, int>>();
            Stack<MdBlock> blockStack = new Stack<MdBlock>(blocks);
            while (blockStack.Count > 0)
            {
                MdBlock block = blockStack.Pop();
                PushInlines(pending, block.Inlines);
                if (block.HeaderCells != null) foreach (List<MdInline> cell in block.HeaderCells) PushInlines(pending, cell);
                if (block.Rows != null) foreach (List<List<MdInline>> row in block.Rows) foreach (List<MdInline> cell in row) PushInlines(pending, cell);
                if (block.Children != null) foreach (MdBlock child in block.Children) blockStack.Push(child);
            }
            while (pending.Count > 0)
            {
                KeyValuePair<MdInline, int> entry = pending.Pop();
                if (entry.Value > deepest) deepest = entry.Value;
                if (entry.Key.Children != null)
                    foreach (MdInline child in entry.Key.Children) pending.Push(new KeyValuePair<MdInline, int>(child, entry.Value + 1));
            }
            return deepest;
        }

        private static void PushInlines(Stack<KeyValuePair<MdInline, int>> pending, List<MdInline> inlines)
        {
            if (inlines == null) return;
            foreach (MdInline inline in inlines) pending.Push(new KeyValuePair<MdInline, int>(inline, 1));
        }

        private static void ReviewNestingChecks(string temp)
        {
            string folder = Path.Combine(temp, "review-nesting");
            Directory.CreateDirectory(folder);
            int limit = InlineParser.MaxNesting;

            // The review's input: 18 000 "*", a letter, 18 000 "*" (36 001 bytes). 2.0.1 built 9 000 levels and the
            // preview ended the process with a stack overflow. Run in a separate process for that reason.
            int exit;
            Dictionary<string, string> stars = RunRenderProbe(folder, "stars", new string('*', 18000) + "x" + new string('*', 18000), out exit);
            Check("[复审1] 18000 个 * 包住的文字：预览和导出在独立进程中完成（" + Outcome(exit) + "）", exit == 0 && Field(stars, "rendered") == "1");
            Check("[复审1] 嵌套不超过 " + limit.ToString(CultureInfo.InvariantCulture) + " 层，文字保留（实际 " + Field(stars, "depth") + " 层）",
                DepthWithin(stars, limit + 1) && Field(stars, "plain") == "x");
            Dictionary<string, string> underscores = RunRenderProbe(folder, "underscores", new string('_', 18000) + "x" + new string('_', 18000), out exit);
            Check("[复审1] 换成 _ 同样完成（" + Outcome(exit) + "）", exit == 0 && DepthWithin(underscores, limit + 1) && Field(underscores, "plain") == "x");
            string deep = new string('*', 9000);
            string elsewhere = "# " + deep + "标题" + deep + "\n\n[" + deep + "链接" + deep + "](a.md)\n\n| 列 |\n|---|\n| " + deep + "单元格" + deep + " |";
            Dictionary<string, string> places = RunRenderProbe(folder, "elsewhere", elsewhere, out exit);
            Check("[复审1] 标题、链接文字和表格里的深层嵌套也能完成（" + Outcome(exit) + "）", exit == 0 && DepthWithin(places, limit + 2));

            // In process (shallow enough for any version): ordinary nesting keeps every level, the limit drops no text.
            MarkdownDocument twenty = MarkdownParser.Parse(new string('*', 40) + "x" + new string('*', 40));
            Check("[复审1] 20 层的嵌套照常加粗，没有多余的 *", InlineDepth(twenty.Blocks) == 21 && twenty.Blocks[0].Inlines[0].Kind == InlineKind.Strong &&
                MdInline.PlainText(twenty.Blocks[0].Inlines) == "x");
            MarkdownDocument fifty = MarkdownParser.Parse(new string('*', 100) + "x" + new string('*', 100));
            Check("[复审1] 50 层的嵌套只保留 " + limit.ToString(CultureInfo.InvariantCulture) + " 层，文字不丢", InlineDepth(fifty.Blocks) == limit + 1 &&
                MdInline.PlainText(fifty.Blocks[0].Inlines) == "x");
        }

        /// <summary>Copies a window's own draft under a new id, as if that window had crashed right after writing it.</summary>
        private static void LeaveOrphanCopy(MainWindow window)
        {
            File.Copy(window.DraftPathForTest, Path.Combine(DraftSession.DraftDirectory, Guid.NewGuid().ToString("N") + ".md~"));
        }

        /// <summary>Claims the orphaned draft with this text and releases the others untouched.</summary>
        private static DraftInfo ClaimDraft(MainWindow window, string text)
        {
            DraftInfo found = null;
            foreach (DraftInfo info in DraftSession.ClaimOrphans(Path.GetFileNameWithoutExtension(window.DraftPathForTest)))
            {
                if (found == null && info.Text == text) found = info; else info.Dispose();
            }
            return found;
        }

        private static void ReviewDraftChecks(string temp)
        {
            string folder = Path.Combine(temp, "review-drafts");
            Directory.CreateDirectory(folder);
            ThemedDialog.TestResponder = AuditResponder;
            MainWindow window = null, restored = null;
            try
            {
                // 2. Edited against ORIGINAL; another program then writes a new version; after that the draft is
                // written and the program ends abnormally. The file's time is older than the draft.
                string file = WriteText(folder, "conflict.md", "ORIGINAL");
                window = new MainWindow(file, null, false);
                window.SourceBoxForTest.AppendText(" + unsaved edit");
                File.WriteAllText(file, "EXTERNAL NEW VERSION", new UTF8Encoding(false));
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-5));
                window.SaveDraftForTest();
                LeaveOrphanCopy(window);
                CloseQuietly(window); window = null;
                restored = new MainWindow(null, null, false);
                DraftInfo draft = ClaimDraft(restored, "ORIGINAL + unsaved edit");
                string note = draft == null ? null : MainWindow.RecoveryNote(draft);
                Check("[复审2] 恢复列表标出原文件在编辑期间被修改过（修改早于最后一次存草稿也能发现）", note != null && note.Contains("被其他程序修改"));
                bool saved = true;
                if (draft != null)
                {
                    restored.RestoreDraftForTest(draft);
                    ThemedDialog.TestLog.Clear();
                    saved = restored.SaveForTest();
                }
                Check("[复审2] 恢复草稿后保存会先确认；取消则保留其他程序的修改",
                    draft != null && !saved && ThemedDialog.TestLog.Contains("磁盘上的文件已被修改") && File.ReadAllText(file, Encoding.UTF8) == "EXTERNAL NEW VERSION");
                CloseQuietly(restored); restored = null;

                // Control: when the file did not change, a recovered draft saves without extra questions.
                string calm = WriteText(folder, "calm.md", "BASE");
                window = new MainWindow(calm, null, false);
                window.SourceBoxForTest.AppendText(" edited");
                window.SaveDraftForTest();
                LeaveOrphanCopy(window);
                CloseQuietly(window); window = null;
                restored = new MainWindow(null, null, false);
                DraftInfo calmDraft = ClaimDraft(restored, "BASE edited");
                bool calmQuiet = calmDraft != null && MainWindow.RecoveryNote(calmDraft) == null;
                bool calmSaved = false;
                if (calmDraft != null)
                {
                    restored.RestoreDraftForTest(calmDraft);
                    ThemedDialog.TestLog.Clear();
                    calmSaved = restored.SaveForTest();
                }
                Check("[复审2] 原文件没变时不误报，恢复后直接保存，不多问", calmQuiet && calmSaved &&
                    ThemedDialog.TestLog.Count == 0 && File.ReadAllText(calm, Encoding.UTF8) == "BASE edited");
                CloseQuietly(restored); restored = null;

                // A draft written by 2.0.1 or earlier has no base version: saving over the file asks first.
                string legacyFile = WriteText(folder, "legacy.md", "ON DISK");
                DraftSession old = new DraftSession();
                old.Save(legacyFile, "OLD DRAFT TEXT", TextFormat.Default());
                old.Dispose();
                restored = new MainWindow(null, null, false);
                DraftInfo legacy = ClaimDraft(restored, "OLD DRAFT TEXT");
                bool legacySaved = true;
                if (legacy != null)
                {
                    restored.RestoreDraftForTest(legacy);
                    restored.CheckDiskStateForTest();
                    ThemedDialog.TestLog.Clear();
                    legacySaved = restored.SaveForTest();
                }
                Check("[复审2] 旧版本留下的草稿（无法确认文件版本）保存前也会先确认",
                    legacy != null && !legacySaved && ThemedDialog.TestLog.Contains("无法确认磁盘上的文件是否被修改过") && File.ReadAllText(legacyFile, Encoding.UTF8) == "ON DISK");
                CloseQuietly(restored); restored = null;

                // 3. The lossy-decode flag survives the draft: open, edit, draft, recover, save.
                string lossy = Path.Combine(folder, "lossy.md");
                byte[] original = new byte[] { 0xEF, 0xBB, 0xBF, 0x41, 0xC3, 0x28 };
                File.WriteAllBytes(lossy, original);
                window = new MainWindow(lossy, null, false);
                window.SourceBoxForTest.AppendText("!");
                string edited = window.SourceBoxForTest.Text;
                window.SaveDraftForTest();
                LeaveOrphanCopy(window);
                CloseQuietly(window); window = null;
                restored = new MainWindow(null, null, false);
                DraftInfo lossyDraft = ClaimDraft(restored, edited);
                Check("[复审3] 草稿记住“有无法解码的字节”", lossyDraft != null && lossyDraft.Format.LossyDecode);
                bool lossySaved = true;
                if (lossyDraft != null)
                {
                    restored.RestoreDraftForTest(lossyDraft);
                    ThemedDialog.TestLog.Clear();
                    lossySaved = restored.SaveForTest();
                }
                Check("[复审3] 恢复草稿后保存到原文件前仍会确认；取消则原始字节不变",
                    lossyDraft != null && !lossySaved && ThemedDialog.TestLog.Contains("文件中有无法解码的字节") && SameBytes(File.ReadAllBytes(lossy), original));
            }
            finally
            {
                ThemedDialog.TestResponder = AuditResponder;
                CloseQuietly(window);
                CloseQuietly(restored);
                ThemedDialog.TestResponder = null;
            }
        }

        private static void ReviewEncodingChecks(string temp)
        {
            string folder = Path.Combine(temp, "review-encoding");
            Directory.CreateDirectory(folder);
            ThemedDialog.TestResponder = AuditResponder;
            Encoding big5 = Encoding.GetEncoding(950);
            Encoding ansi = Encoding.GetEncoding(Encoding.Default.CodePage == 65001 ? 54936 : Encoding.Default.CodePage);
            MainWindow window = null, restored = null;
            try
            {
                // 4. Reopened as Big5, then updated by another program in Big5.
                string file = Path.Combine(folder, "big5.md");
                File.WriteAllBytes(file, big5.GetBytes("中文標題"));
                window = new MainWindow(file, null, false);
                window.ReopenWithEncodingForTest(950);
                bool reopened = window.SourceBoxForTest.Text == "中文標題";
                File.WriteAllBytes(file, big5.GetBytes("中文更新"));
                window.CheckDiskStateForTest();
                Check("[复审4] 选定的 Big5 在外部修改自动刷新后仍然有效", reopened && window.SourceBoxForTest.Text == "中文更新" && window.FormatForTest.CodePage == 950);
                File.WriteAllBytes(file, big5.GetBytes("中文再次更新"));
                window.ReloadForTest();
                Check("[复审4] 重新载入（F5）也沿用选定的编码", window.SourceBoxForTest.Text == "中文再次更新" && window.FormatForTest.CodePage == 950);

                // The choice travels with the draft: recovered, read and saved as Big5 without questions.
                window.SourceBoxForTest.AppendText(" 草稿");
                string edited = window.SourceBoxForTest.Text;
                window.SaveDraftForTest();
                LeaveOrphanCopy(window);
                CloseQuietly(window); window = null;
                restored = new MainWindow(null, null, false);
                DraftInfo draft = ClaimDraft(restored, edited);
                bool remembered = draft != null && draft.Format.ReadCodePage == 950;
                bool saved = false;
                if (draft != null)
                {
                    restored.RestoreDraftForTest(draft);
                    ThemedDialog.TestLog.Clear();
                    saved = restored.SaveForTest();
                }
                Check("[复审4] 草稿记住读取编码，恢复后按 Big5 读取和保存", remembered && saved && ThemedDialog.TestLog.Count == 0 &&
                    SameBytes(File.ReadAllBytes(file), big5.GetBytes("中文再次更新 草稿")));
                File.WriteAllBytes(file, big5.GetBytes("保存後再更新"));
                restored.CheckDiskStateForTest();
                Check("[复审4] 按 Big5 保存后，外部修改仍按 Big5 刷新", restored.SourceBoxForTest.Text == "保存後再更新");

                // Saving in UTF-8 instead hands reading back to detection.
                restored.SetSaveEncodingForTest(65001, false);
                bool utf8Saved = restored.SaveForTest();
                File.WriteAllText(file, "改用 UTF-8 之后", new UTF8Encoding(false));
                restored.CheckDiskStateForTest();
                Check("[复审4] 改用 UTF-8 保存后，外部修改按 UTF-8 读取", utf8Saved && restored.SourceBoxForTest.Text == "改用 UTF-8 之后" && restored.FormatForTest.CodePage == 65001);
                CloseQuietly(restored); restored = null;

                // Unchanged: a file detected as the ANSI code page keeps it when it is refreshed.
                string detected = Path.Combine(folder, "ansi.md");
                File.WriteAllBytes(detected, ansi.GetBytes("简体中文"));
                window = new MainWindow(detected, null, false);
                File.WriteAllBytes(detected, ansi.GetBytes("简体中文更新"));
                window.CheckDiskStateForTest();
                Check("[复审4] 自动识别为 ANSI（GBK）的文件刷新后仍正确显示", window.SourceBoxForTest.Text == "简体中文更新");
            }
            finally
            {
                ThemedDialog.TestResponder = AuditResponder;
                CloseQuietly(window);
                CloseQuietly(restored);
                ThemedDialog.TestResponder = null;
            }
        }

        private static string WriteGrayPng(string folder, int width, int height)
        {
            string path = Path.Combine(folder, width.ToString(CultureInfo.InvariantCulture) + "x" + height.ToString(CultureInfo.InvariantCulture) + ".png");
            int stride = (width + 3) & ~3;
            BitmapSource source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, new byte[stride * height], stride);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
            return path;
        }

        private static void ReviewImageChecks(string temp)
        {
            string folder = Path.Combine(temp, "review-images");
            Directory.CreateDirectory(folder);
            ImageCache cache = new ImageCache();
            // 5. The review's 1×12000 and 1×60000 images kept their full height in 2.0.1.
            foreach (int[] size in new int[][] { new int[] { 1, 12000 }, new int[] { 1, 60000 }, new int[] { 2, 30000 } })
            {
                BitmapSource decoded; int width, height;
                bool loaded = cache.TryGet(WriteGrayPng(folder, size[0], size[1]), out decoded, out width, out height);
                string label = size[0].ToString(CultureInfo.InvariantCulture) + "×" + size[1].ToString(CultureInfo.InvariantCulture);
                string actual = loaded ? decoded.PixelWidth.ToString(CultureInfo.InvariantCulture) + "×" + decoded.PixelHeight.ToString(CultureInfo.InvariantCulture) : "未载入";
                Check("[复审5] " + label + " 的极窄长图按高度上限解码（实际 " + actual + "），显示尺寸仍按原图",
                    loaded && decoded.PixelHeight <= ImageCache.MaxDecodeHeight && (long)decoded.PixelWidth * decoded.PixelHeight <= ImageCache.MaxDecodePixels &&
                    width == size[0] && height == size[1]);
            }
            BitmapSource icon; int iconWidth, iconHeight;
            bool iconLoaded = cache.TryGet(WriteGrayPng(folder, 48, 48), out icon, out iconWidth, out iconHeight);
            Check("[复审5] 小图仍按原始像素解码，不放大也不缩小", iconLoaded && icon.PixelWidth == 48 && icon.PixelHeight == 48);
        }
    }
}
