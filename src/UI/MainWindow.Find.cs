using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CodexMarkdownReader.Core;

namespace CodexMarkdownReader.UI
{
    public sealed partial class MainWindow
    {
        private sealed class FindMatch
        {
            public TextPointer Start;
            public TextPointer End;
            public TextBox Box;
            public int Index;
            public int Length;
        }

        private sealed class FindSegment
        {
            public Run Run;
            public TextBox Box;
            public int Start;
            public int Length;
        }

        private Border findBar;
        private TextBox findBox;
        private TextBlock findPlaceholder;
        private TextBlock findCount;
        private ToggleButton findCaseButton;
        private Button findScopeButton;
        private bool findInPreview = true;
        private bool findDirty = true;
        private readonly List<FindMatch> findMatches = new List<FindMatch>();
        private int findIndex = -1;
        private DispatcherTimer findTimer;
        private bool lastFocusWasPreview;
        private bool previewHighlighted;
        private FindMatch highlightedCurrent;

        private Border BuildFindBar()
        {
            Border bar = new Border();
            bar.Visibility = Visibility.Collapsed;
            bar.HorizontalAlignment = HorizontalAlignment.Right;
            bar.VerticalAlignment = VerticalAlignment.Top;
            bar.Margin = new Thickness(0, 10, 22, 0);
            bar.Padding = new Thickness(6);
            bar.CornerRadius = new CornerRadius(9);
            bar.BorderThickness = new Thickness(1);
            bar.SetResourceReference(Border.BackgroundProperty, "MenuBg");
            bar.SetResourceReference(Border.BorderBrushProperty, "MenuBorder");
            DropShadowEffect shadow = new DropShadowEffect();
            shadow.BlurRadius = 16; shadow.ShadowDepth = 3; shadow.Direction = 270; shadow.Opacity = 0.18;
            bar.Effect = shadow;

            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;

            Grid inputHost = new Grid();
            findBox = new TextBox();
            findBox.SetResourceReference(StyleProperty, "InputBox");
            findBox.Width = 230;
            AutomationProperties.SetName(findBox, "查找内容");
            AutomationProperties.SetAutomationId(findBox, "FindTextBox");
            findBox.TextChanged += delegate
            {
                findPlaceholder.Visibility = findBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                RecomputeFind(true);
            };
            findBox.PreviewKeyDown += FindBoxKeyDown;
            inputHost.Children.Add(findBox);
            findPlaceholder = new TextBlock();
            findPlaceholder.Text = "查找";
            findPlaceholder.IsHitTestVisible = false;
            findPlaceholder.Margin = new Thickness(10, 0, 0, 0);
            findPlaceholder.VerticalAlignment = VerticalAlignment.Center;
            findPlaceholder.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
            inputHost.Children.Add(findPlaceholder);
            row.Children.Add(inputHost);

            findCount = new TextBlock();
            findCount.MinWidth = 64;
            findCount.TextAlignment = TextAlignment.Center;
            findCount.VerticalAlignment = VerticalAlignment.Center;
            findCount.FontSize = 12;
            findCount.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            AutomationProperties.SetLiveSetting(findCount, AutomationLiveSetting.Polite);
            row.Children.Add(findCount);

            Button previous = MakeIconButton(Icons.ChevronUp, "上一个（Shift+Enter / Shift+F3）", "FindPrevious", delegate { StepFind(-1); });
            previous.Width = 28; previous.Height = 28;
            row.Children.Add(previous);
            Button next = MakeIconButton(Icons.ChevronDown, "下一个（Enter / F3）", "FindNext", delegate { StepFind(1); });
            next.Width = 28; next.Height = 28;
            row.Children.Add(next);

            findCaseButton = new ToggleButton();
            findCaseButton.SetResourceReference(StyleProperty, "ToggleIconButton");
            findCaseButton.Width = 28; findCaseButton.Height = 28;
            findCaseButton.Content = Icons.Create(Icons.Case, 16);
            findCaseButton.ToolTip = "区分大小写";
            AutomationProperties.SetName(findCaseButton, "区分大小写");
            findCaseButton.Click += delegate { RecomputeFind(true); };
            row.Children.Add(findCaseButton);

            findScopeButton = new Button();
            findScopeButton.SetResourceReference(StyleProperty, "GhostButton");
            findScopeButton.Height = 28;
            findScopeButton.FontSize = 12;
            findScopeButton.Padding = new Thickness(8, 0, 8, 0);
            findScopeButton.ToolTip = "切换在预览或源码中查找";
            findScopeButton.Click += delegate
            {
                findInPreview = !findInPreview;
                UpdateFindScope();
                RecomputeFind(true);
            };
            row.Children.Add(findScopeButton);

            Button close = MakeIconButton(Icons.Close, "关闭（Esc）", "FindClose", delegate { CloseFindBar(); });
            close.Width = 28; close.Height = 28;
            row.Children.Add(close);

            bar.Child = row;
            return bar;
        }

        private void OpenFindBar()
        {
            if (startPage.Visibility == Visibility.Visible) return;
            if (viewMode == ViewMode.Source) findInPreview = false;
            else if (viewMode == ViewMode.Preview) findInPreview = true;
            else findInPreview = lastFocusWasPreview || !source.IsKeyboardFocusWithin;
            UpdateFindScope();
            string selected = null;
            if (!findInPreview && source.SelectionLength > 0 && source.SelectionLength < 200) selected = source.SelectedText;
            else if (findInPreview && preview.Selection != null && !preview.Selection.IsEmpty)
            {
                string text = preview.Selection.Text;
                if (text.Length < 200 && text.IndexOf('\n') < 0) selected = text.Trim();
            }
            findBar.Visibility = Visibility.Visible;
            if (!String.IsNullOrEmpty(selected) && selected.IndexOf('\n') < 0) findBox.Text = selected;
            else RecomputeFind(true);
            findBox.Focus();
            findBox.SelectAll();
        }

        private void CloseFindBar()
        {
            findBar.Visibility = Visibility.Collapsed;
            findMatches.Clear();
            findIndex = -1;
            ClearPreviewHighlights();
            FocusPrimaryPane();
        }

        /// <summary>Highlights live in the rendered document; re-render to drop them.</summary>
        private void ClearPreviewHighlights()
        {
            highlightedCurrent = null;
            if (!previewHighlighted) return;
            previewHighlighted = false;
            RenderNow(true, true);
        }

        private void UpdateFindScope()
        {
            bool both = viewMode == ViewMode.Split;
            findScopeButton.Visibility = both ? Visibility.Visible : Visibility.Collapsed;
            findScopeButton.Content = findInPreview ? "在预览中" : "在源码中";
        }

        private void FindBoxKeyDown(object sender, KeyEventArgs e)
        {
            Key key = RealKey(e);
            if (key == Key.Enter)
            {
                StepFind((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
                e.Handled = true;
            }
            else if (key == Key.Escape)
            {
                CloseFindBar();
                e.Handled = true;
            }
        }

        private void InvalidateFindResults()
        {
            findDirty = true;
            if (findTimer == null)
            {
                findTimer = new DispatcherTimer(DispatcherPriority.Background);
                findTimer.Interval = TimeSpan.FromMilliseconds(250);
                findTimer.Tick += delegate { findTimer.Stop(); if (findBar.Visibility == Visibility.Visible) RecomputeFind(false); };
            }
            findTimer.Stop();
            findTimer.Start();
        }

        private void ClearFindResults()
        {
            findMatches.Clear();
            findIndex = -1;
            findDirty = true;
            if (findCount != null) findCount.Text = "";
        }

        private StringComparison FindComparison
        {
            get { return findCaseButton.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase; }
        }

        private void RecomputeFind(bool jumpToFirst)
        {
            findMatches.Clear();
            findDirty = false;
            string query = findBox.Text;
            ClearPreviewHighlights();
            if (String.IsNullOrEmpty(query))
            {
                findIndex = -1;
                findCount.Text = "";
                return;
            }
            if (findInPreview)
            {
                CollectPreviewMatches(query);
                HighlightPreviewMatches();
            }
            else CollectSourceMatches(query);

            if (findMatches.Count == 0)
            {
                findIndex = -1;
                findCount.Text = "无结果";
                return;
            }
            if (jumpToFirst || findIndex < 0 || findIndex >= findMatches.Count)
            {
                findIndex = FirstMatchNearCurrentPosition();
                SelectFindMatch();
            }
            UpdateFindCount();
        }

        private void UpdateFindCount()
        {
            findCount.Text = findMatches.Count == 0 ? "无结果" : (findIndex + 1).ToString(CultureInfo.InvariantCulture) + " / " + findMatches.Count.ToString(CultureInfo.InvariantCulture);
        }

        private int FirstMatchNearCurrentPosition()
        {
            if (!findInPreview)
            {
                int caret = source.SelectionStart;
                for (int i = 0; i < findMatches.Count; i++) if (findMatches[i].Index >= caret) return i;
                return 0;
            }
            for (int i = 0; i < findMatches.Count; i++)
            {
                FindMatch match = findMatches[i];
                if (match.Start == null) continue;
                try
                {
                    Rect rect = match.Start.GetCharacterRect(LogicalDirection.Forward);
                    if (!rect.IsEmpty && rect.Top >= 0) return i;
                }
                catch { }
            }
            return 0;
        }

        private void StepFind(int direction)
        {
            if (findBar.Visibility != Visibility.Visible) { OpenFindBar(); return; }
            if (findDirty) RecomputeFind(false);
            if (findMatches.Count == 0) return;
            findIndex = (findIndex + direction + findMatches.Count) % findMatches.Count;
            SelectFindMatch();
            UpdateFindCount();
        }

        private void HighlightPreviewMatches()
        {
            Brush brush = ReaderApp.Palette != null ? ReaderApp.Palette["FindMatch"] : Brushes.Yellow;
            int count = 0;
            foreach (FindMatch match in findMatches)
            {
                if (match.Start == null || match.End == null) continue;
                try { new TextRange(match.Start, match.End).ApplyPropertyValue(TextElement.BackgroundProperty, brush); }
                catch { }
                previewHighlighted = true;
                if (++count >= 1000) break;
            }
        }

        private void CollectSourceMatches(string query)
        {
            string text = source.Text;
            StringComparison comparison = FindComparison;
            int index = 0;
            while (index <= text.Length - query.Length)
            {
                int found = text.IndexOf(query, index, comparison);
                if (found < 0) break;
                FindMatch match = new FindMatch();
                match.Index = found;
                match.Length = query.Length;
                findMatches.Add(match);
                index = found + Math.Max(1, query.Length);
                if (findMatches.Count >= 5000) break;
            }
        }

        private void CollectPreviewMatches(string query)
        {
            FlowDocument document = preview.Document;
            if (document == null) return;
            List<FindSegment> segments = new List<FindSegment>();
            StringBuilder text = new StringBuilder();
            foreach (Block block in document.Blocks) CollectBlockText(block, segments, text);
            string all = text.ToString();
            StringComparison comparison = FindComparison;
            int index = 0;
            int segmentCursor = 0;
            while (index <= all.Length - query.Length)
            {
                int found = all.IndexOf(query, index, comparison);
                if (found < 0) break;
                index = found + Math.Max(1, query.Length);
                while (segmentCursor < segments.Count && segments[segmentCursor].Start + segments[segmentCursor].Length <= found) segmentCursor++;
                if (segmentCursor >= segments.Count) break;
                FindSegment first = segments[segmentCursor];
                if (found < first.Start) continue;
                int endPosition = found + query.Length;
                int lastIndex = segmentCursor;
                while (lastIndex < segments.Count && segments[lastIndex].Start + segments[lastIndex].Length < endPosition) lastIndex++;
                if (lastIndex >= segments.Count) continue;
                FindSegment last = segments[lastIndex];
                FindMatch match = new FindMatch();
                if (first.Box != null || last.Box != null)
                {
                    if (!Object.ReferenceEquals(first, last) || first.Box == null) continue;
                    match.Box = first.Box;
                    match.Index = found - first.Start;
                    match.Length = query.Length;
                }
                else
                {
                    match.Start = first.Run.ContentStart.GetPositionAtOffset(found - first.Start);
                    match.End = last.Run.ContentStart.GetPositionAtOffset(endPosition - last.Start);
                    if (match.Start == null || match.End == null) continue;
                }
                findMatches.Add(match);
                if (findMatches.Count >= 5000) break;
            }
        }

        private static void CollectBlockText(Block block, List<FindSegment> segments, StringBuilder text)
        {
            Paragraph paragraph = block as Paragraph;
            if (paragraph != null)
            {
                foreach (Inline inline in paragraph.Inlines) CollectInlineText(inline, segments, text);
                text.Append('\n');
                return;
            }
            List list = block as List;
            if (list != null)
            {
                foreach (ListItem item in list.ListItems) foreach (Block child in item.Blocks) CollectBlockText(child, segments, text);
                return;
            }
            Section section = block as Section;
            if (section != null)
            {
                foreach (Block child in section.Blocks) CollectBlockText(child, segments, text);
                return;
            }
            Table table = block as Table;
            if (table != null)
            {
                foreach (TableRowGroup group in table.RowGroups)
                    foreach (TableRow row in group.Rows)
                        foreach (TableCell cell in row.Cells)
                        {
                            foreach (Block child in cell.Blocks) CollectBlockText(child, segments, text);
                        }
                return;
            }
            BlockUIContainer container = block as BlockUIContainer;
            if (container != null)
            {
                TextBox box = FindVisualOrLogicalTextBox(container.Child);
                if (box != null)
                {
                    FindSegment segment = new FindSegment();
                    segment.Box = box;
                    segment.Start = text.Length;
                    segment.Length = box.Text.Length;
                    segments.Add(segment);
                    text.Append(box.Text);
                    text.Append('\n');
                }
            }
        }

        private static void CollectInlineText(Inline inline, List<FindSegment> segments, StringBuilder text)
        {
            Run run = inline as Run;
            if (run != null)
            {
                FindSegment segment = new FindSegment();
                segment.Run = run;
                segment.Start = text.Length;
                segment.Length = run.Text.Length;
                segments.Add(segment);
                text.Append(run.Text);
                return;
            }
            Span span = inline as Span;
            if (span != null)
            {
                foreach (Inline child in span.Inlines) CollectInlineText(child, segments, text);
                return;
            }
            if (inline is LineBreak) { text.Append('\n'); return; }
            if (inline is InlineUIContainer) text.Append('￼');
        }

        private static TextBox FindVisualOrLogicalTextBox(UIElement element)
        {
            if (element == null) return null;
            TextBox direct = element as TextBox;
            if (direct != null) return direct;
            foreach (object child in LogicalTreeHelper.GetChildren(element))
            {
                UIElement childElement = child as UIElement;
                TextBox found = FindVisualOrLogicalTextBox(childElement);
                if (found != null) return found;
            }
            return null;
        }

        private void SelectFindMatch()
        {
            if (findIndex < 0 || findIndex >= findMatches.Count) return;
            FindMatch match = findMatches[findIndex];
            if (!findInPreview)
            {
                source.Select(match.Index, match.Length);
                try
                {
                    source.UpdateLayout();
                    EnsureScrollViewers();
                    Rect rect = source.GetRectFromCharacterIndex(match.Index);
                    if (sourceScroll != null && !rect.IsEmpty && (rect.Top < 20 || rect.Bottom > sourceScroll.ViewportHeight - 20))
                        sourceScroll.ScrollToVerticalOffset(Math.Max(0, sourceScroll.VerticalOffset + rect.Top - sourceScroll.ViewportHeight / 3));
                }
                catch { }
                return;
            }
            if (match.Box != null)
            {
                try { preview.Selection.Select(preview.Document.ContentStart, preview.Document.ContentStart); } catch { }
                match.Box.Select(match.Index, match.Length);
                match.Box.BringIntoView();
                Rect boxRect = match.Box.GetRectFromCharacterIndex(match.Index);
                if (!boxRect.IsEmpty) match.Box.ScrollToHorizontalOffset(Math.Max(0, match.Box.HorizontalOffset + boxRect.Left - 40));
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { CenterPreviewOn(match.Box.TranslatePoint(new Point(0, 0), preview).Y); }));
                return;
            }
            try
            {
                Palette palette = ReaderApp.Palette ?? Palette.Create(false);
                if (highlightedCurrent != null && highlightedCurrent.Start != null)
                    new TextRange(highlightedCurrent.Start, highlightedCurrent.End).ApplyPropertyValue(TextElement.BackgroundProperty, palette["FindMatch"]);
                new TextRange(match.Start, match.End).ApplyPropertyValue(TextElement.BackgroundProperty, palette["FindCurrent"]);
                highlightedCurrent = match;
                previewHighlighted = true;
                Rect rect = match.Start.GetCharacterRect(LogicalDirection.Forward);
                if (!rect.IsEmpty) CenterPreviewOn(rect.Top);
            }
            catch { }
        }

        private void CenterPreviewOn(double top)
        {
            EnsureScrollViewers();
            if (previewScroll == null || Double.IsNaN(top)) return;
            // top is in pixels of the viewer; the scroll offsets of a zoomed preview are not.
            double visible = PreviewScrolling.ViewportPixels(previewScroll);
            if (top >= 30 && top <= visible - 60) return;
            PreviewScrolling.ScrollByPixels(previewScroll, top - visible / 3);
        }
    }
}
