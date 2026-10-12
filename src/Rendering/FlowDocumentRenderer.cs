using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexMarkdownReader.Markdown;
using CodexMarkdownReader.UI;
using Block = CodexMarkdownReader.Markdown.Block;
using Inline = CodexMarkdownReader.Markdown.Inline;

namespace CodexMarkdownReader.Rendering
{
    public sealed class PreviewRenderOptions
    {
        public Palette Palette;
        public string BaseDirectory;
        public ImageCache Images;
        public bool PrintMode;
        public double FontSize = 15.5;
        /// <summary>Invoked when a link is activated. The flag is true when Ctrl was held (open in a new window).</summary>
        public Action<LinkTarget, bool> LinkHandler;
    }

    public sealed class RenderResult
    {
        public FlowDocument Document;
        public MarkdownDocument Markdown;
        public Dictionary<string, TextElement> Anchors = new Dictionary<string, TextElement>(StringComparer.Ordinal);
        public List<KeyValuePair<HeadingInfo, TextElement>> Headings = new List<KeyValuePair<HeadingInfo, TextElement>>();
        public List<TextBox> CodeBoxes = new List<TextBox>();
    }

    /// <summary>Turns the Markdown AST into a WPF FlowDocument.</summary>
    public sealed class FlowDocumentRenderer
    {
        private readonly PreviewRenderOptions options;
        private readonly Palette palette;
        private RenderResult result;
        private static readonly double[] HeadingSizes = { 1.8, 1.45, 1.22, 1.08, 1.0, 0.95 };

        public FlowDocumentRenderer(PreviewRenderOptions options)
        {
            this.options = options;
            palette = options.Palette ?? Palette.Create(false);
        }

        public RenderResult Render(MarkdownDocument markdown)
        {
            result = new RenderResult();
            result.Markdown = markdown;
            FlowDocument doc = new FlowDocument();
            doc.Language = ReaderTypography.Language;
            doc.FontFamily = ReaderTypography.Body;
            doc.FontSize = options.FontSize;
            doc.Foreground = palette["Text"];
            doc.Background = options.PrintMode ? Brushes.White : Brushes.Transparent;
            doc.LineHeight = Math.Round(options.FontSize * 1.7);
            doc.PagePadding = new Thickness(40, 28, 40, 64);
            doc.PageWidth = Double.NaN;
            doc.ColumnWidth = Double.PositiveInfinity;
            doc.TextAlignment = TextAlignment.Left;
            doc.IsOptimalParagraphEnabled = false;
            doc.IsHyphenationEnabled = false;

            // Formulas share macros (\newcommand ...) in document order and can refer to each other's \tag with \eqref.
            using (MathContext.Use(MathContext.ForDocument(markdown)))
            {
                AddBlocks(doc.Blocks, markdown.Blocks, 0, true);
                AddFootnotes(doc.Blocks, markdown);
            }
            if (doc.Blocks.Count == 0 && !options.PrintMode)
            {
                Paragraph empty = new Paragraph(new Run("预览将在这里显示"));
                empty.Foreground = palette["Faint"];
                empty.Margin = new Thickness(0, 12, 0, 0);
                doc.Blocks.Add(empty);
            }
            TrimTrailingMargin(doc.Blocks);
            result.Document = doc;
            return result;
        }

        // ------------------------------------------------------------------ blocks

        private void AddBlocks(BlockCollection target, List<Block> blocks, int listDepth, bool topLevel)
        {
            for (int index = 0; index < blocks.Count; index++)
            {
                Block block = blocks[index];
                System.Windows.Documents.Block rendered = RenderBlock(block, listDepth, topLevel && index == 0);
                if (rendered != null) target.Add(rendered);
            }
        }

        private System.Windows.Documents.Block RenderBlock(Block block, int listDepth, bool first)
        {
            switch (block.Kind)
            {
                case BlockKind.Paragraph: return RenderParagraph(block);
                case BlockKind.Heading: return RenderHeading(block, first);
                case BlockKind.CodeBlock: return RenderCode(block.RawText, block.Info);
                case BlockKind.MathBlock: return RenderMathBlock(block.RawText);
                case BlockKind.List: return RenderList(block, listDepth);
                case BlockKind.Quote: return RenderQuote(block, listDepth);
                case BlockKind.Table: return RenderTable(block);
                case BlockKind.ThematicBreak: return RenderRule();
            }
            return null;
        }

        private System.Windows.Documents.Block RenderParagraph(Block block)
        {
            List<Inline> inlines = block.Inlines ?? new List<Inline>();
            Inline onlyImage = SingleImage(inlines);
            if (onlyImage != null)
            {
                UIElement image = CreateImageElement(onlyImage, true);
                if (image != null)
                {
                    BlockUIContainer container = new BlockUIContainer(image);
                    container.Margin = new Thickness(0, 4, 0, 16);
                    return container;
                }
            }
            Paragraph paragraph = new Paragraph();
            paragraph.Margin = new Thickness(0, 0, 0, 14);
            AppendInlines(paragraph.Inlines, inlines);
            return paragraph;
        }

        private static Inline SingleImage(List<Inline> inlines)
        {
            Inline image = null;
            foreach (Inline inline in inlines)
            {
                if (inline.Kind == InlineKind.Image && image == null) { image = inline; continue; }
                if (inline.Kind == InlineKind.Text && inline.Text.Trim().Length == 0) continue;
                if (inline.Kind == InlineKind.SoftBreak) continue;
                return null;
            }
            return image;
        }

        private System.Windows.Documents.Block RenderHeading(Block block, bool first)
        {
            Paragraph heading = new Paragraph();
            int level = Math.Max(1, Math.Min(6, block.Level));
            heading.FontSize = Math.Round(options.FontSize * HeadingSizes[level - 1] * 2) / 2;
            heading.FontWeight = FontWeights.SemiBold;
            heading.LineHeight = Double.NaN;
            heading.Foreground = level == 6 ? palette["Muted"] : palette["Text"];
            double top = first ? 0 : (level == 1 ? 30 : level == 2 ? 26 : 20);
            heading.Margin = new Thickness(0, top, 0, level <= 2 ? 14 : 8);
            if (level <= 2)
            {
                heading.BorderBrush = palette["HeadingRule"];
                heading.BorderThickness = new Thickness(0, 0, 0, 1);
                heading.Padding = new Thickness(0, 0, 0, 8);
            }
            AppendInlines(heading.Inlines, block.Inlines ?? new List<Inline>());
            if (!String.IsNullOrEmpty(block.Id))
            {
                if (!result.Anchors.ContainsKey(block.Id)) result.Anchors[block.Id] = heading;
                foreach (HeadingInfo info in result.Markdown.Headings)
                    if (Object.ReferenceEquals(info.Block, block)) { result.Headings.Add(new KeyValuePair<HeadingInfo, TextElement>(info, heading)); break; }
            }
            return heading;
        }

        private System.Windows.Documents.Block RenderRule()
        {
            Border line = new Border();
            line.Height = 1;
            line.Background = palette["Border"];
            line.SnapsToDevicePixels = true;
            BlockUIContainer container = new BlockUIContainer(line);
            container.Margin = new Thickness(0, 22, 0, 22);
            return container;
        }

        private System.Windows.Documents.Block RenderList(Block list, int depth)
        {
            System.Windows.Documents.List wpfList = new System.Windows.Documents.List();
            bool anyTask = false;
            foreach (Block item in list.Children) if (item.IsTask) { anyTask = true; break; }
            if (anyTask) wpfList.MarkerStyle = TextMarkerStyle.None;
            else if (list.Ordered) wpfList.MarkerStyle = TextMarkerStyle.Decimal;
            else wpfList.MarkerStyle = depth == 0 ? TextMarkerStyle.Disc : depth == 1 ? TextMarkerStyle.Circle : TextMarkerStyle.Square;
            if (list.Ordered && list.Start > 0) wpfList.StartIndex = list.Start;
            wpfList.Margin = new Thickness(0, depth == 0 ? 0 : 4, 0, depth == 0 ? 14 : 2);
            wpfList.Padding = new Thickness(anyTask ? 2 : (list.Ordered ? 30 : 26), 0, 0, 0);
            if (list.Ordered && !anyTask && list.Start + list.Children.Count > 99) wpfList.Padding = new Thickness(38, 0, 0, 0);

            foreach (Block item in list.Children)
            {
                ListItem listItem = new ListItem();
                listItem.Margin = new Thickness(0, 0, 0, list.Tight ? 3 : 8);
                AddBlocks(listItem.Blocks, item.Children, depth + 1, false);
                foreach (System.Windows.Documents.Block child in listItem.Blocks)
                {
                    Paragraph paragraph = child as Paragraph;
                    if (paragraph != null) paragraph.Margin = new Thickness(0, 0, 0, list.Tight ? 0 : 10);
                }
                if (anyTask)
                {
                    Paragraph firstParagraph = listItem.Blocks.FirstBlock as Paragraph;
                    if (firstParagraph == null)
                    {
                        firstParagraph = new Paragraph();
                        firstParagraph.Margin = new Thickness(0);
                        if (listItem.Blocks.FirstBlock != null) listItem.Blocks.InsertBefore(listItem.Blocks.FirstBlock, firstParagraph);
                        else listItem.Blocks.Add(firstParagraph);
                    }
                    System.Windows.Documents.Inline glyph = item.IsTask ? (System.Windows.Documents.Inline)CreateTaskBox(item.TaskChecked) : new Run("•  ");
                    if (firstParagraph.Inlines.FirstInline != null) firstParagraph.Inlines.InsertBefore(firstParagraph.Inlines.FirstInline, glyph);
                    else firstParagraph.Inlines.Add(glyph);
                    if (item.IsTask && item.TaskChecked)
                    {
                        firstParagraph.Foreground = palette["Muted"];
                    }
                }
                if (anyTask)
                {
                    // Task lists draw their own markers, so nested lists need explicit indentation.
                    foreach (System.Windows.Documents.Block child in listItem.Blocks)
                    {
                        System.Windows.Documents.List nested = child as System.Windows.Documents.List;
                        if (nested != null) nested.Margin = new Thickness(18, nested.Margin.Top, 0, nested.Margin.Bottom);
                    }
                }
                TrimTrailingMargin(listItem.Blocks);
                wpfList.ListItems.Add(listItem);
            }
            return wpfList;
        }

        private InlineUIContainer CreateTaskBox(bool isChecked)
        {
            Border box = new Border();
            box.Width = 15; box.Height = 15;
            box.CornerRadius = new CornerRadius(4);
            box.BorderThickness = new Thickness(1.3);
            box.BorderBrush = isChecked ? palette["Accent"] : palette["BorderStrong"];
            box.Background = isChecked ? palette["Accent"] : Brushes.Transparent;
            box.Margin = new Thickness(0, 0, 8, -2);
            if (isChecked)
            {
                System.Windows.Shapes.Path check = new System.Windows.Shapes.Path();
                check.Data = Geometry.Parse("M 3,7.5 L 6,10.5 L 11.5,4.5");
                check.Stroke = palette["AccentText"];
                check.StrokeThickness = 1.8;
                check.StrokeStartLineCap = PenLineCap.Round; check.StrokeEndLineCap = PenLineCap.Round; check.StrokeLineJoin = PenLineJoin.Round;
                check.Width = 13; check.Height = 13;
                box.Child = check;
            }
            AutomationProperties.SetName(box, isChecked ? "已完成" : "未完成");
            box.Margin = new Thickness(0, 0, 8, 0);
            InlineUIContainer container = new InlineUIContainer(box);
            AlignInlineElement(container, box);
            return container;
        }

        private System.Windows.Documents.Block RenderQuote(Block quote, int depth)
        {
            Section section = new Section();
            string alert = quote.AlertKind;
            Brush bar = palette["QuoteBar"];
            if (alert != null)
            {
                string key = alert == "NOTE" ? "AlertNote" : alert == "TIP" ? "AlertTip" : alert == "IMPORTANT" ? "AlertImportant" : alert == "WARNING" ? "AlertWarning" : "AlertCaution";
                bar = palette[key];
                Paragraph title = new Paragraph();
                title.Margin = new Thickness(0, 0, 0, 6);
                title.FontWeight = FontWeights.SemiBold;
                title.Foreground = bar;
                string label = alert == "NOTE" ? "说明" : alert == "TIP" ? "提示" : alert == "IMPORTANT" ? "重要" : alert == "WARNING" ? "警告" : "注意";
                title.Inlines.Add(new Run(label));
                section.Blocks.Add(title);
            }
            section.BorderBrush = bar;
            section.BorderThickness = new Thickness(3, 0, 0, 0);
            section.Background = palette["QuoteBg"];
            section.Padding = new Thickness(16, 10, 14, 10);
            section.Margin = new Thickness(0, 2, 0, 16);
            section.Foreground = alert == null ? palette["Muted"] : palette["Text"];
            AddBlocks(section.Blocks, quote.Children, depth, false);
            TrimTrailingMargin(section.Blocks);
            return section;
        }

        private System.Windows.Documents.Block RenderTable(Block block)
        {
            Table table = new Table();
            table.CellSpacing = 0;
            table.Margin = new Thickness(0, 4, 0, 18);
            table.BorderBrush = palette["Border"];
            table.BorderThickness = new Thickness(0.5);
            table.FontSize = Math.Max(12, options.FontSize - 1);
            int columns = block.Aligns.Count;
            for (int c = 0; c < columns; c++) table.Columns.Add(new TableColumn());
            TableRowGroup group = new TableRowGroup();
            table.RowGroups.Add(group);
            AddTableRow(group, block.HeaderCells, block.Aligns, true, false);
            for (int r = 0; r < block.Rows.Count; r++) AddTableRow(group, block.Rows[r], block.Aligns, false, r % 2 == 1);
            return table;
        }

        private void AddTableRow(TableRowGroup group, List<List<Inline>> cells, List<TableAlign> aligns, bool header, bool stripe)
        {
            TableRow row = new TableRow();
            if (header) row.Background = palette["TableHeaderBg"];
            else if (stripe) row.Background = palette["TableStripeBg"];
            for (int c = 0; c < aligns.Count; c++)
            {
                Paragraph paragraph = new Paragraph();
                paragraph.Margin = new Thickness(0);
                paragraph.LineHeight = Double.NaN;
                if (header) paragraph.FontWeight = FontWeights.SemiBold;
                switch (aligns[c])
                {
                    case TableAlign.Center: paragraph.TextAlignment = TextAlignment.Center; break;
                    case TableAlign.Right: paragraph.TextAlignment = TextAlignment.Right; break;
                    default: paragraph.TextAlignment = TextAlignment.Left; break;
                }
                if (c < cells.Count) AppendInlines(paragraph.Inlines, cells[c]);
                TableCell cell = new TableCell(paragraph);
                cell.Padding = new Thickness(12, 7, 12, 7);
                cell.BorderBrush = palette["Border"];
                cell.BorderThickness = new Thickness(0.5);
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }

        private void AddFootnotes(BlockCollection target, MarkdownDocument markdown)
        {
            if (markdown.UsedFootnotes.Count == 0) return;
            target.Add(RenderRule());
            System.Windows.Documents.List list = new System.Windows.Documents.List();
            list.MarkerStyle = TextMarkerStyle.Decimal;
            list.FontSize = Math.Max(12, options.FontSize - 1.5);
            list.Foreground = palette["Muted"];
            list.Padding = new Thickness(28, 0, 0, 0);
            list.Margin = new Thickness(0, 0, 0, 12);
            foreach (FootnoteDefinition footnote in markdown.UsedFootnotes)
            {
                ListItem item = new ListItem();
                item.Margin = new Thickness(0, 0, 0, 6);
                AddBlocks(item.Blocks, footnote.Blocks, 1, false);
                Paragraph last = item.Blocks.LastBlock as Paragraph;
                if (last == null)
                {
                    last = new Paragraph();
                    item.Blocks.Add(last);
                }
                foreach (System.Windows.Documents.Block child in item.Blocks)
                {
                    Paragraph paragraph = child as Paragraph;
                    if (paragraph != null) paragraph.Margin = new Thickness(0, 0, 0, 4);
                }
                LinkTarget back = new LinkTarget();
                back.Kind = LinkKind.Anchor;
                back.Fragment = "fnref-" + footnote.Number.ToString(CultureInfo.InvariantCulture);
                Hyperlink backLink = CreateHyperlink(back, "返回正文");
                backLink.Inlines.Add(new Run(" ↩"));
                last.Inlines.Add(backLink);
                string anchor = "fn-" + footnote.Number.ToString(CultureInfo.InvariantCulture);
                if (item.Blocks.FirstBlock != null && !result.Anchors.ContainsKey(anchor)) result.Anchors[anchor] = item.Blocks.FirstBlock;
                list.ListItems.Add(item);
            }
            target.Add(list);
        }

        private static void TrimTrailingMargin(BlockCollection blocks)
        {
            System.Windows.Documents.Block last = blocks.LastBlock;
            if (last == null) return;
            Thickness margin = last.Margin;
            if (!Double.IsNaN(margin.Bottom)) last.Margin = new Thickness(margin.Left, margin.Top, margin.Right, 0);
        }

        // ------------------------------------------------------------------ code & math

        /// <summary>
        /// Printed code is real flow text (a paragraph of runs and line breaks), so the paginator can continue it on
        /// the next page. A UI element cannot be split: a long code block used to run off the bottom of one page.
        /// </summary>
        private System.Windows.Documents.Block RenderPrintedCode(string code, string language)
        {
            Section section = new Section();
            section.Background = palette["CodeBg"];
            section.BorderBrush = palette["CodeBorder"];
            section.BorderThickness = new Thickness(1);
            section.Padding = new Thickness(14, 10, 14, 10);
            section.Margin = new Thickness(0, 4, 0, 16);
            if (language.Length > 0)
            {
                Paragraph tag = new Paragraph(new Run(language));
                tag.FontSize = 10.5;
                tag.Foreground = palette["Faint"];
                tag.Margin = new Thickness(0, 0, 0, 6);
                section.Blocks.Add(tag);
            }
            Paragraph body = new Paragraph();
            body.FontFamily = ReaderTypography.Code;
            body.Language = ReaderTypography.Language;
            body.FontSize = 12;
            body.LineHeight = 18;
            body.Foreground = palette["Text"];
            body.Margin = new Thickness(0);
            body.TextAlignment = TextAlignment.Left;
            string[] lines = (code ?? "").Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) body.Inlines.Add(new LineBreak());
                if (lines[i].Length > 0) body.Inlines.Add(new Run(lines[i]));
            }
            section.Blocks.Add(body);
            return section;
        }

        private System.Windows.Documents.Block RenderCode(string code, string info)
        {
            string language = (info ?? "").Trim();
            int space = language.IndexOfAny(new char[] { ' ', '\t', '{' });
            if (space > 0) language = language.Substring(0, space);
            if (options.PrintMode) return RenderPrintedCode(code, language);
            Border box = new Border();
            box.SetValue(TextBlock.LineHeightProperty, Double.NaN);
            box.Background = palette["CodeBg"];
            box.BorderBrush = palette["CodeBorder"];
            box.BorderThickness = new Thickness(1);
            box.CornerRadius = new CornerRadius(8);
            box.SnapsToDevicePixels = true;

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            DockPanel header = new DockPanel();
            header.Margin = new Thickness(14, 4, 4, 0);
            header.LastChildFill = true;

            Button copy = new Button();
            Style copyStyle = FindStyle("CodeCopyButton");
            if (copyStyle != null) copy.Style = copyStyle;
            copy.Content = CopyButtonContent(false);
            copy.ToolTip = "复制代码";
            copy.Focusable = true;
            AutomationProperties.SetName(copy, "复制代码");
            DockPanel.SetDock(copy, Dock.Right);
            header.Children.Add(copy);

            TextBlock label = new TextBlock();
            label.Text = language.Length > 0 ? language : "代码";
            label.FontSize = 11;
            label.Foreground = palette["Faint"];
            label.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(label);
            grid.Children.Add(header);

            TextBox content = new TextBox();
            content.Text = code;
            content.IsReadOnly = true;
            content.IsReadOnlyCaretVisible = false;
            content.BorderThickness = new Thickness(0);
            content.Background = Brushes.Transparent;
            content.Foreground = palette["Text"];
            content.FontFamily = ReaderTypography.Code;
            content.Language = ReaderTypography.Language;
            content.FontSize = Math.Max(11, options.FontSize - 2);
            content.SetValue(TextBlock.LineHeightProperty, Math.Round(content.FontSize * 1.6));
            content.TextWrapping = TextWrapping.NoWrap;
            content.AcceptsReturn = true;
            content.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            content.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            content.Padding = new Thickness(10, 2, 10, 12);
            content.SelectionBrush = palette["Selection"];
            content.IsInactiveSelectionHighlightEnabled = true;
            content.Cursor = Cursors.IBeam;
            content.Style = FindStyle("CodeTextBox");
            AutomationProperties.SetName(content, language.Length > 0 ? language + " 代码" : "代码块");
            content.PreviewMouseWheel += ForwardMouseWheel;
            Grid.SetRow(content, 1);
            grid.Children.Add(content);
            result.CodeBoxes.Add(content);

            copy.Click += delegate
            {
                try { Clipboard.SetText(code ?? ""); }
                catch { return; }
                copy.Content = CopyButtonContent(true);
                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(1600);
                timer.Tick += delegate { timer.Stop(); copy.Content = CopyButtonContent(false); };
                timer.Start();
            };

            box.Child = grid;
            BlockUIContainer container = new BlockUIContainer(box);
            container.Margin = new Thickness(0, 4, 0, 16);
            return container;
        }

        private static Style FindStyle(string key)
        {
            Application app = Application.Current;
            return app == null ? null : app.TryFindResource(key) as Style;
        }

        private object CopyButtonContent(bool copied)
        {
            StackPanel panel = new StackPanel();
            panel.Orientation = Orientation.Horizontal;
            panel.Children.Add(Icons.Create(copied ? Icons.Check : Icons.Copy, 13));
            TextBlock text = new TextBlock();
            text.Text = copied ? "已复制" : "复制";
            text.FontSize = 11.5;
            text.Margin = new Thickness(5, 0, 0, 0);
            text.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(text);
            return panel;
        }

        internal static void ForwardMouseWheel(object sender, MouseWheelEventArgs e)
        {
            UIElement element = sender as UIElement;
            if (element == null || e.Handled) return;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                TextBox box = sender as TextBox;
                ScrollViewer viewer = sender as ScrollViewer;
                if (box != null) box.ScrollToHorizontalOffset(Math.Max(0, box.HorizontalOffset - e.Delta));
                else if (viewer != null) viewer.ScrollToHorizontalOffset(Math.Max(0, viewer.HorizontalOffset - e.Delta));
                e.Handled = true;
                return;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
            e.Handled = true;
            DependencyObject parent = VisualTreeHelper.GetParent(element);
            UIElement target = parent as UIElement;
            while (target == null && parent != null)
            {
                parent = VisualTreeHelper.GetParent(parent);
                target = parent as UIElement;
            }
            if (target == null) return;
            MouseWheelEventArgs forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta);
            forwarded.RoutedEvent = UIElement.MouseWheelEvent;
            forwarded.Source = sender;
            target.RaiseEvent(forwarded);
        }

        private System.Windows.Documents.Block RenderMathBlock(string source)
        {
            string tag;
            FrameworkElement visual = CreateMathVisual(source, true, palette["Text"], options.FontSize, out tag);
            FrameworkElement content = tag == null ? visual : WithTag(visual, tag, palette["Text"], MathFontSize(true, options.FontSize));
            Grid centered = new Grid();
            centered.HorizontalAlignment = HorizontalAlignment.Stretch;
            content.HorizontalAlignment = tag == null ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            centered.Children.Add(content);
            AutomationProperties.SetName(centered, "块级" + GetAccessibleFormulaName(source) + (tag == null ? "" : "，编号 " + tag));
            AutomationProperties.SetHelpText(centered, "LaTeX：" + source);
            if (options.PrintMode)
            {
                BlockUIContainer printContainer = new BlockUIContainer(centered);
                printContainer.Margin = new Thickness(0, 8, 0, 16);
                return printContainer;
            }
            ScrollViewer scroller = new ScrollViewer();
            scroller.Content = centered;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroller.Focusable = false;
            scroller.PreviewMouseWheel += ForwardMouseWheel;
            BlockUIContainer container = new BlockUIContainer(scroller);
            container.Margin = new Thickness(0, 8, 0, 16);
            return container;
        }

        /// <summary>A numbered formula: centered, with its tag at the right edge (never overlapping the formula).</summary>
        internal static FrameworkElement WithTag(FrameworkElement formula, string tag, Brush foreground, double size)
        {
            TextBlock label = new TextBlock();
            label.Text = tag;
            label.Language = ReaderTypography.Language;
            label.FontFamily = ReaderTypography.Math;
            label.FontSize = size;
            label.Foreground = foreground;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.Margin = new Thickness(size, 0, 4, 0);
            AutomationProperties.SetAutomationId(label, "FormulaTag");
            label.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            double width = label.DesiredSize.Width;
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = width });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = width });
            formula.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(formula, 1);
            Grid.SetColumn(label, 2);
            grid.Children.Add(formula);
            grid.Children.Add(label);
            return grid;
        }

        internal FrameworkElement CreateMathVisual(string source, bool block)
        {
            return CreateMathVisual(source, block, palette["Text"], options.FontSize);
        }

        /// <summary>Font size of formulas: a little larger than the text, as Cambria Math runs small next to it.</summary>
        internal static double MathFontSize(bool block, double baseSize)
        {
            return block ? baseSize * 1.2 : baseSize * 1.08;
        }

        internal static FrameworkElement CreateMathVisual(string source, bool block, Brush foreground, double baseSize)
        {
            string tag;
            FrameworkElement host = CreateMathVisual(source, block, foreground, baseSize, out tag);
            return tag == null ? host : WithTag(host, tag, foreground, MathFontSize(block, baseSize));
        }

        /// <summary>
        /// The typeset formula in a host border. A displayed formula's \tag is returned separately (inline formulas keep
        /// theirs). Formulas that cannot be typeset show their source, with the reason in the tooltip.
        /// </summary>
        internal static FrameworkElement CreateMathVisual(string source, bool block, Brush foreground, double baseSize, out string tag)
        {
            tag = null;
            string text = (source ?? "").Trim();
            string flat = Regex.Replace(text, @"\s+", " ");
            double size = MathFontSize(block, baseSize);
            FrameworkElement expression;
            try
            {
                LatexMathLayout layout = new LatexMathLayout(text, foreground, block);
                expression = layout.Build(size);
                tag = layout.EquationTag;
            }
            catch (Exception ex)
            {
                // Too long, nested too deeply, or a command the engine does not know: show the source instead.
                TextBlock fallback = new TextBlock();
                fallback.Text = flat.Length > 300 ? flat.Substring(0, 300) + "…" : flat;
                fallback.ToolTip = "无法排版这个公式：" + FailureReason(ex) + "。显示的是公式原文";
                fallback.Language = ReaderTypography.Language;
                fallback.FontFamily = ReaderTypography.Math;
                fallback.FontSize = size;
                fallback.Foreground = foreground;
                expression = fallback;
                tag = null;
            }
            Border host = new Border();
            // Elements hosted in a FlowDocument inherit its tall line height; formulas need their natural metrics.
            host.SetValue(TextBlock.LineHeightProperty, Double.NaN);
            host.Child = expression;
            host.Padding = block ? new Thickness(10, 5, 10, 5) : new Thickness(2, 1, 3, 1);
            host.Background = Brushes.Transparent;
            host.VerticalAlignment = VerticalAlignment.Center;
            host.SnapsToDevicePixels = true;
            AutomationProperties.SetName(host, GetAccessibleFormulaName(flat));
            AutomationProperties.SetHelpText(host, "LaTeX：" + flat);
            AutomationProperties.SetAutomationId(host, block ? "BlockMathFormula" : "InlineMathFormula");
            return host;
        }

        private static string FailureReason(Exception ex)
        {
            if (ex is NotSupportedException || ex is InvalidOperationException) return ex.Message;
            return "排版时出错（" + ex.GetType().Name + "）";
        }

        /// <summary>True when the formula could not be typeset and its source is shown instead.</summary>
        internal static bool IsMathFallback(FrameworkElement host)
        {
            Border border = host as Border;
            return border == null || !(border.Child is MathBox);
        }

        /// <summary>How far a typeset formula (with its host padding) reaches below its baseline; -1 for the source fallback.</summary>
        internal static double MathDepth(FrameworkElement host)
        {
            Border border = host as Border;
            MathBox box = border == null ? null : border.Child as MathBox;
            if (box == null) return -1;
            host.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            return Math.Max(0, box.Descent) + border.Padding.Bottom;
        }

        internal static string GetAccessibleFormulaName(string source)
        {
            string value = source ?? "";
            value = Regex.Replace(value, @"\\(?:dfrac|tfrac|frac)\s*", " 分数 ");
            value = Regex.Replace(value, @"\\(?:bar|overline)\s*", " 上横线 ");
            value = Regex.Replace(value, @"\\sqrt\s*", " 平方根 ");
            value = value.Replace("\\varepsilon", "ε").Replace("\\epsilon", "ε").Replace("\\alpha", "α").Replace("\\beta", "β");
            value = value.Replace("\\gamma", "γ").Replace("\\delta", "δ").Replace("\\theta", "θ").Replace("\\lambda", "λ");
            value = value.Replace("\\mu", "μ").Replace("\\pi", "π").Replace("\\sigma", "σ").Replace("\\phi", "φ").Replace("\\omega", "ω");
            value = value.Replace("{", " ").Replace("}", " ").Replace("_", " 下标 ").Replace("^", " 上标 ");
            return "公式：" + Regex.Replace(value, @"\s+", " ").Trim();
        }

        // ------------------------------------------------------------------ inlines

        private void AppendInlines(InlineCollection target, List<Inline> inlines)
        {
            if (inlines == null) return;
            foreach (Inline inline in inlines)
            {
                System.Windows.Documents.Inline rendered = RenderInline(inline);
                if (rendered != null) target.Add(rendered);
            }
        }

        private System.Windows.Documents.Inline RenderInline(Inline inline)
        {
            switch (inline.Kind)
            {
                case InlineKind.Text:
                    return new Run(inline.Text);
                case InlineKind.SoftBreak:
                    return new Run(" ");
                case InlineKind.HardBreak:
                    return new LineBreak();
                case InlineKind.Code:
                    {
                        Run run = new Run(inline.Text);
                        run.FontFamily = ReaderTypography.Code;
                        run.Language = ReaderTypography.Language;
                        run.FontSize = Math.Max(11, options.FontSize - 1.5);
                        run.Background = palette["InlineCodeBg"];
                        return run;
                    }
                case InlineKind.Emphasis:
                    {
                        Italic italic = new Italic();
                        AppendInlines(italic.Inlines, inline.Children);
                        return italic;
                    }
                case InlineKind.Strong:
                    {
                        Bold bold = new Bold();
                        AppendInlines(bold.Inlines, inline.Children);
                        return bold;
                    }
                case InlineKind.Strikethrough:
                    {
                        Span span = new Span();
                        span.TextDecorations = TextDecorations.Strikethrough;
                        AppendInlines(span.Inlines, inline.Children);
                        return span;
                    }
                case InlineKind.Subscript:
                case InlineKind.Superscript:
                    {
                        Span span = new Span();
                        // WPF's Superscript position sits above the capitals (Fe³⁺ looked detached); TextTop lines the
                        // smaller text up with the top of the line's letters instead.
                        span.BaselineAlignment = inline.Kind == InlineKind.Subscript ? BaselineAlignment.Subscript : BaselineAlignment.TextTop;
                        span.FontSize = Math.Round(options.FontSize * 0.75);
                        AppendInlines(span.Inlines, inline.Children);
                        return span;
                    }
                case InlineKind.Mark:
                    {
                        Span span = new Span();
                        span.Background = palette["MarkBg"];
                        AppendInlines(span.Inlines, inline.Children);
                        return span;
                    }
                case InlineKind.Keyboard:
                    return RenderKeyboard(Inline.PlainText(inline.Children));
                case InlineKind.Math:
                    {
                        FrameworkElement visual = CreateMathVisual(inline.Text, false);
                        InlineUIContainer formula = new InlineUIContainer(visual);
                        AlignInlineMath(formula, visual);
                        AutomationProperties.SetName(formula, GetAccessibleFormulaName(inline.Text));
                        AutomationProperties.SetHelpText(formula, "LaTeX：" + inline.Text);
                        return formula;
                    }
                case InlineKind.Link:
                    return RenderLink(inline);
                case InlineKind.Image:
                    return RenderInlineImage(inline);
                case InlineKind.FootnoteRef:
                    return RenderFootnoteReference(inline);
            }
            return null;
        }

        private void AlignInlineMath(InlineUIContainer container, FrameworkElement host)
        {
            AlignInlineFormula(container, host, options.FontSize);
        }

        /// <summary>
        /// Puts an inline formula's baseline on the text baseline: an embedded element sits with its bottom on the
        /// baseline, so it is lowered by its depth. The source fallback is centered on the x-height instead.
        /// </summary>
        internal static void AlignInlineFormula(InlineUIContainer container, FrameworkElement host, double fontSize)
        {
            host.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            double depth = MathDepth(host);
            if (depth < 0) depth = Math.Max(0, host.DesiredSize.Height / 2 - fontSize * 0.34);
            Thickness margin = host.Margin;
            host.Margin = new Thickness(margin.Left, margin.Top, margin.Right, margin.Bottom - depth);
            container.BaselineAlignment = BaselineAlignment.Baseline;
        }

        /// <summary>Centers an inline element on the x-height of the surrounding text instead of sitting on the baseline.</summary>
        private void AlignInlineElement(InlineUIContainer container, FrameworkElement element)
        {
            element.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            double height = element.DesiredSize.Height;
            double drop = Math.Max(0, height / 2 - options.FontSize * 0.34);
            Thickness margin = element.Margin;
            element.Margin = new Thickness(margin.Left, margin.Top, margin.Right, margin.Bottom - drop);
            container.BaselineAlignment = BaselineAlignment.Baseline;
        }

        private System.Windows.Documents.Inline RenderKeyboard(string text)
        {
            Border key = new Border();
            key.SetValue(TextBlock.LineHeightProperty, Double.NaN);
            key.Background = palette["KbdBg"];
            key.BorderBrush = palette["KbdBorder"];
            key.BorderThickness = new Thickness(1, 1, 1, 2);
            key.CornerRadius = new CornerRadius(4);
            key.Padding = new Thickness(5, 0, 5, 0);
            key.Margin = new Thickness(1, 0, 1, 0);
            TextBlock label = new TextBlock();
            label.Text = text;
            label.FontFamily = ReaderTypography.Code;
            label.FontSize = Math.Max(10.5, options.FontSize - 3);
            label.Foreground = palette["Text"];
            key.Child = label;
            InlineUIContainer container = new InlineUIContainer(key);
            AlignInlineElement(container, key);
            return container;
        }

        private System.Windows.Documents.Inline RenderLink(Inline inline)
        {
            LinkTarget target = LinkPolicy.Resolve(inline.Href, options.BaseDirectory);
            string label = Inline.PlainText(inline.Children);
            if (target.Kind == LinkKind.Invalid || target.Kind == LinkKind.Missing || target.Kind == LinkKind.Blocked)
            {
                Span span = new Span();
                AppendInlines(span.Inlines, inline.Children);
                if (target.Kind != LinkKind.Invalid)
                {
                    span.ToolTip = target.Describe();
                    span.TextDecorations = null;
                    span.Foreground = palette["Muted"];
                }
                return span;
            }
            Hyperlink link = CreateHyperlink(target, label);
            AppendInlines(link.Inlines, inline.Children);
            if (!String.IsNullOrEmpty(inline.Title)) link.ToolTip = inline.Title + "\n" + link.ToolTip;
            return link;
        }

        private Hyperlink CreateHyperlink(LinkTarget target, string label)
        {
            Hyperlink link = new Hyperlink();
            link.Foreground = palette["Link"];
            link.TextDecorations = null;
            link.Tag = target;
            link.Cursor = Cursors.Hand;
            string tip = target.Describe();
            if (target.Kind == LinkKind.MarkdownFile) tip += "\nCtrl + 单击：在新窗口打开";
            if (target.Kind == LinkKind.LocalFile && target.Safety == FileSafety.Confirm) tip += "\n此类文件打开前会先确认";
            link.ToolTip = tip;
            AutomationProperties.SetName(link, String.IsNullOrEmpty(label) ? tip : label);
            AutomationProperties.SetHelpText(link, tip);
            link.MouseEnter += delegate { link.TextDecorations = TextDecorations.Underline; };
            link.MouseLeave += delegate { link.TextDecorations = null; };
            link.Click += delegate
            {
                if (options.LinkHandler != null) options.LinkHandler(target, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            };
            if (options.PrintMode) link.Foreground = palette["Link"];
            return link;
        }

        private System.Windows.Documents.Inline RenderFootnoteReference(Inline inline)
        {
            LinkTarget target = new LinkTarget();
            target.Kind = LinkKind.Anchor;
            target.Fragment = "fn-" + inline.FootnoteNumber.ToString(CultureInfo.InvariantCulture);
            Hyperlink link = CreateHyperlink(target, "脚注 " + inline.FootnoteNumber.ToString(CultureInfo.InvariantCulture));
            link.ToolTip = "跳转到脚注 " + inline.FootnoteNumber.ToString(CultureInfo.InvariantCulture);
            link.BaselineAlignment = BaselineAlignment.Superscript;
            link.FontSize = Math.Round(options.FontSize * 0.72);
            link.Inlines.Add(new Run("[" + inline.FootnoteNumber.ToString(CultureInfo.InvariantCulture) + "]"));
            string anchor = "fnref-" + inline.FootnoteNumber.ToString(CultureInfo.InvariantCulture);
            if (!result.Anchors.ContainsKey(anchor)) result.Anchors[anchor] = link;
            return link;
        }

        // ------------------------------------------------------------------ images

        private System.Windows.Documents.Inline RenderInlineImage(Inline inline)
        {
            UIElement element = CreateImageElement(inline, false);
            if (element != null)
            {
                InlineUIContainer container = new InlineUIContainer(element);
                container.BaselineAlignment = BaselineAlignment.TextBottom;
                return container;
            }
            return ImagePlaceholder(inline);
        }

        private System.Windows.Documents.Inline ImagePlaceholder(Inline inline)
        {
            string alt = String.IsNullOrEmpty(inline.Text) ? "图片" : inline.Text;
            LinkTarget target = LinkPolicy.Resolve(inline.Href, options.BaseDirectory);
            string prefix;
            if (target.Kind == LinkKind.Web) prefix = "远程图片（未自动加载）";
            else if (target.Kind == LinkKind.LocalFile && (target.Path ?? "").EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) prefix = "SVG 图片";
            else if (target.Kind == LinkKind.Missing || target.Kind == LinkKind.Invalid) prefix = "图片未找到";
            else prefix = "图片无法显示";
            string text = "[" + prefix + "：" + alt + "]";
            if (target.Kind == LinkKind.Web || target.Kind == LinkKind.LocalFile)
            {
                Hyperlink link = CreateHyperlink(target, text);
                link.Inlines.Add(new Run(text));
                link.FontStyle = FontStyles.Italic;
                return link;
            }
            Run fallback = new Run(text);
            fallback.Foreground = palette["Faint"];
            fallback.FontStyle = FontStyles.Italic;
            return fallback;
        }

        private UIElement CreateImageElement(Inline inline, bool block)
        {
            LinkTarget target = LinkPolicy.Resolve(inline.Href, options.BaseDirectory);
            if (target.Kind != LinkKind.LocalFile || !LinkPolicy.IsImagePath(target.Path) || options.Images == null) return null;
            BitmapSource bitmap;
            int width, height;
            if (!options.Images.TryGet(target.Path, out bitmap, out width, out height)) return null;
            System.Windows.Controls.Image image = new System.Windows.Controls.Image();
            image.Source = bitmap;
            image.Stretch = Stretch.Uniform;
            image.StretchDirection = StretchDirection.DownOnly;
            // Natural pixel size, scaled down only when the column is narrower.
            image.Width = width;
            image.MaxWidth = width;
            image.HorizontalAlignment = HorizontalAlignment.Left;
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            string tip = String.IsNullOrEmpty(inline.Title) ? inline.Text : inline.Title;
            if (!String.IsNullOrEmpty(tip)) image.ToolTip = tip;
            AutomationProperties.SetName(image, String.IsNullOrEmpty(inline.Text) ? "图片" : inline.Text);
            if (!options.PrintMode)
            {
                image.Cursor = Cursors.Hand;
                image.ToolTip = (String.IsNullOrEmpty(tip) ? "" : tip + "\n") + "单击用默认程序打开";
                image.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (options.LinkHandler != null) options.LinkHandler(target, false);
                };
            }
            // A Viewbox keeps the pixel size of small images and only shrinks large ones to the column width.
            Viewbox box = new Viewbox();
            box.Stretch = Stretch.Uniform;
            box.StretchDirection = StretchDirection.DownOnly;
            box.MaxWidth = width;
            box.HorizontalAlignment = HorizontalAlignment.Left;
            if (!block) box.Margin = new Thickness(0, 2, 0, 2);
            image.Width = width;
            image.Height = height;
            image.MaxWidth = Double.PositiveInfinity;
            box.Child = image;
            return box;
        }
    }
}
