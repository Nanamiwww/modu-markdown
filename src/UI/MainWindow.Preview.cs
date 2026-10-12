using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMarkdownReader.Core;
using CodexMarkdownReader.Markdown;
using CodexMarkdownReader.Rendering;
using LinkTarget = CodexMarkdownReader.Rendering.LinkTarget;

namespace CodexMarkdownReader.UI
{
    public sealed class InfoAction
    {
        public readonly string Label;
        public readonly Action Handler;

        public InfoAction(string label, Action handler)
        {
            Label = label;
            Handler = handler;
        }
    }

    public sealed partial class MainWindow
    {
        private RenderResult lastRender;
        private MarkdownDocument lastMarkdown;
        private string lastMarkdownText;
        private string outlineSignature;

        private sealed class NavEntry
        {
            public string Path;
            public double Offset;
        }

        private readonly List<NavEntry> backStack = new List<NavEntry>();
        private readonly List<NavEntry> forwardStack = new List<NavEntry>();

        // ------------------------------------------------------------------ rendering

        private void ScheduleRender()
        {
            renderTimer.Stop();
            renderTimer.Start();
        }

        private void RenderNow(bool preserveScroll)
        {
            RenderNow(preserveScroll, false);
        }

        private void RenderNow(bool preserveScroll, bool forFind)
        {
            string text = source.Text ?? "";
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                EnsureScrollViewers();
                double offset = preserveScroll && previewScroll != null ? previewScroll.VerticalOffset : 0;
                MarkdownDocument markdown = MarkdownParser.Parse(text);
                PreviewRenderOptions options = new PreviewRenderOptions();
                options.Palette = ReaderApp.Palette ?? Palette.Create(false);
                options.BaseDirectory = String.IsNullOrEmpty(currentPath) ? null : Path.GetDirectoryName(currentPath);
                options.Images = ReaderApp.Images;
                options.LinkHandler = HandleLink;
                RenderResult result = new FlowDocumentRenderer(options).Render(markdown);
                result.Document.AllowDrop = true;
                lastRender = result;
                lastMarkdown = markdown;
                lastMarkdownText = text;

                MarkProgrammaticPreviewScroll();
                preview.Document = result.Document;
                UpdatePreviewPadding();
                if (previewPanel.Visibility == Visibility.Visible)
                {
                    preview.UpdateLayout();
                    EnsureScrollViewers();
                    if (previewScroll != null) previewScroll.ScrollToVerticalOffset(Math.Min(offset, Math.Max(0, previewScroll.ExtentHeight)));
                }
                UpdateOutline();
                previewHighlighted = false;
                if (!forFind && findBar.Visibility == Visibility.Visible && findInPreview) InvalidateFindResults();
            }
            catch (Exception ex)
            {
                SetStatus("预览失败：" + ex.Message, false);
            }
            watch.Stop();
            // Longer documents re-render less eagerly so typing stays smooth.
            double delay = Math.Max(120, Math.Min(900, watch.ElapsedMilliseconds * 1.5 + 60));
            renderTimer.Interval = TimeSpan.FromMilliseconds(delay);
        }

        // ------------------------------------------------------------------ outline

        private void UpdateOutline()
        {
            if (lastRender == null) return;
            StringBuilder signature = new StringBuilder();
            foreach (KeyValuePair<HeadingInfo, TextElement> pair in lastRender.Headings)
                signature.Append(pair.Key.Level).Append('|').Append(pair.Key.Id).Append('|').Append(pair.Key.SourceLine).Append('|').Append(pair.Key.Text).Append('\n');
            string value = signature.ToString();
            if (value == outlineSignature) return;
            outlineSignature = value;

            outlineList.Items.Clear();
            int minLevel = 6;
            foreach (KeyValuePair<HeadingInfo, TextElement> pair in lastRender.Headings) minLevel = Math.Min(minLevel, pair.Key.Level);
            foreach (KeyValuePair<HeadingInfo, TextElement> pair in lastRender.Headings)
            {
                HeadingInfo heading = pair.Key;
                ListBoxItem item = new ListBoxItem();
                item.Tag = heading;
                TextBlock label = new TextBlock();
                label.Text = String.IsNullOrWhiteSpace(heading.Text) ? "（空标题）" : heading.Text;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.FontSize = heading.Level == minLevel ? 13 : 12.5;
                if (heading.Level == minLevel) label.FontWeight = FontWeights.SemiBold;
                label.Margin = new Thickness((heading.Level - minLevel) * 13, 0, 0, 0);
                item.Content = label;
                item.ToolTip = heading.Text;
                AutomationProperties.SetName(item, "第 " + heading.Level.ToString(CultureInfo.InvariantCulture) + " 级标题：" + heading.Text);
                outlineList.Items.Add(item);
            }
            outlineEmpty.Visibility = lastRender.Headings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            scrollSpyTimer.Stop();
            scrollSpyTimer.Start();
        }

        private void ActivateOutlineItem(DependencyObject origin)
        {
            ListBoxItem item = FindAncestor<ListBoxItem>(origin);
            if (item == null) return;
            HeadingInfo heading = item.Tag as HeadingInfo;
            if (heading != null) NavigateToHeading(heading, true);
        }

        private void ActivateSelectedOutline()
        {
            ListBoxItem item = outlineList.SelectedItem as ListBoxItem;
            if (item == null) return;
            HeadingInfo heading = item.Tag as HeadingInfo;
            if (heading != null) NavigateToHeading(heading, true);
        }

        private void NavigateToHeading(HeadingInfo heading, bool pushHistory)
        {
            if (pushHistory) PushHistory();
            suppressScrollSync = true;
            try
            {
                if (viewMode != ViewMode.Preview) ScrollSourceToLine(heading.SourceLine, viewMode == ViewMode.Source);
                TextElement element;
                if (viewMode != ViewMode.Source && lastRender != null && heading.Id != null && lastRender.Anchors.TryGetValue(heading.Id, out element))
                    ScrollPreviewToElement(element);
            }
            finally
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate { suppressScrollSync = false; }));
            }
        }

        private void ScrollSourceToLine(int line, bool focus)
        {
            int index = LogicalLineStart(line);
            if (index < 0) return;
            source.CaretIndex = index;
            try
            {
                source.UpdateLayout();
                EnsureScrollViewers();
                Rect rect = source.GetRectFromCharacterIndex(index);
                if (sourceScroll != null && !rect.IsEmpty)
                    sourceScroll.ScrollToVerticalOffset(Math.Max(0, sourceScroll.VerticalOffset + rect.Top - 24));
            }
            catch { }
            if (focus) source.Focus();
        }

        private double ElementTop(TextElement element)
        {
            try
            {
                Rect rect = element.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                if (rect.IsEmpty) return Double.NaN;
                return rect.Top;
            }
            catch { return Double.NaN; }
        }

        private void ScrollPreviewToElement(TextElement element)
        {
            if (previewPanel.Visibility != Visibility.Visible) return;
            preview.UpdateLayout();
            EnsureScrollViewers();
            if (previewScroll == null) { element.BringIntoView(); return; }
            double margin = 18 * preview.Zoom / 100.0;
            MarkProgrammaticPreviewScroll();
            // Pixel distances are converted to scroll units, which differ when the preview is zoomed.
            PreviewScrolling.ScrollToTop(previewScroll, element, margin);
            // Check again once layout has settled (long documents finish formatting in the background).
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                if (previewScroll == null) return;
                MarkProgrammaticPreviewScroll();
                PreviewScrolling.ScrollToTop(previewScroll, element, margin);
            }));
        }

        private void UpdateActiveHeading()
        {
            if (lastRender == null || lastRender.Headings.Count == 0 || outlineList.Items.Count != lastRender.Headings.Count) return;
            int active = -1;
            if (viewMode == ViewMode.Source)
            {
                int firstLine = 0;
                try
                {
                    int firstIndex = source.GetCharacterIndexFromPoint(new Point(source.Padding.Left + 2, source.Padding.Top + 2), true);
                    if (firstIndex >= 0) firstLine = LogicalLineOf(firstIndex);
                }
                catch { }
                for (int i = 0; i < lastRender.Headings.Count; i++)
                    if (lastRender.Headings[i].Key.SourceLine <= firstLine + 1) active = i;
            }
            else
            {
                double threshold = 48 * preview.Zoom / 100.0;
                for (int i = 0; i < lastRender.Headings.Count; i++)
                {
                    double top = ElementTop(lastRender.Headings[i].Value);
                    if (Double.IsNaN(top)) continue;
                    if (top <= threshold) active = i; else break;
                }
                if (active < 0 && previewScroll != null && previewScroll.VerticalOffset < 4) active = 0;
                if (previewScroll != null && previewScroll.ScrollableHeight > 0 && previewScroll.VerticalOffset >= previewScroll.ScrollableHeight - 2)
                {
                    // At the very bottom the last visible heading counts as current.
                    for (int i = lastRender.Headings.Count - 1; i >= 0; i--)
                    {
                        double top = ElementTop(lastRender.Headings[i].Value);
                        if (!Double.IsNaN(top) && top < PreviewScrolling.ViewportPixels(previewScroll)) { active = Math.Max(active, i); break; }
                    }
                }
            }
            if (active < 0) { outlineList.SelectedIndex = -1; return; }
            if (outlineList.SelectedIndex != active)
            {
                outlineList.SelectedIndex = active;
                outlineList.ScrollIntoView(outlineList.Items[active]);
            }
        }

        // ------------------------------------------------------------------ anchors & links

        private bool ScrollToAnchor(string fragment)
        {
            if (lastRender == null || String.IsNullOrEmpty(fragment)) return false;
            TextElement element = FindAnchor(fragment);
            if (element == null)
            {
                SetStatus("本文中找不到锚点：#" + fragment, true);
                return false;
            }
            if (viewMode == ViewMode.Source) SetViewMode(ViewMode.Split);
            HeadingInfo heading = null;
            foreach (KeyValuePair<HeadingInfo, TextElement> pair in lastRender.Headings) if (Object.ReferenceEquals(pair.Value, element)) { heading = pair.Key; break; }
            if (heading != null) { NavigateToHeading(heading, false); return true; }
            suppressScrollSync = true;
            ScrollPreviewToElement(element);
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate { suppressScrollSync = false; }));
            return true;
        }

        private TextElement FindAnchor(string fragment)
        {
            Dictionary<string, TextElement> anchors = lastRender.Anchors;
            TextElement element;
            if (anchors.TryGetValue(fragment, out element)) return element;
            string decoded = fragment;
            try { decoded = Uri.UnescapeDataString(fragment); } catch { }
            if (anchors.TryGetValue(decoded, out element)) return element;
            string lower = decoded.ToLowerInvariant();
            if (anchors.TryGetValue(lower, out element)) return element;
            string slug = TextUtil.Slugify(decoded);
            if (anchors.TryGetValue(slug, out element)) return element;
            foreach (KeyValuePair<string, TextElement> pair in anchors)
                if (String.Equals(pair.Key, decoded, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            return null;
        }

        private void ScrollToAnchorWhenReady(string fragment)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { ScrollToAnchor(fragment); }));
        }

        private void RestorePreviewOffsetWhenReady(double offset)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                EnsureScrollViewers();
                if (previewScroll == null) return;
                MarkProgrammaticPreviewScroll();
                previewScroll.ScrollToVerticalOffset(offset);
            }));
        }

        private void HandleLink(LinkTarget target, bool newWindow)
        {
            if (target == null) return;
            switch (target.Kind)
            {
                case LinkKind.Anchor:
                    PushHistory();
                    ScrollToAnchor(target.Fragment);
                    return;
                case LinkKind.MarkdownFile:
                    if (newWindow) { ReaderApp.OpenInNewWindow(target.Path, null); return; }
                    if (String.Equals(target.Path, currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!String.IsNullOrEmpty(target.Fragment)) { PushHistory(); ScrollToAnchor(target.Fragment); }
                        return;
                    }
                    if (!ConfirmDiscardOrSave()) return;
                    NavigateToFile(target.Path, target.Fragment, true);
                    return;
                case LinkKind.LocalFile:
                    if (target.Safety == FileSafety.Confirm)
                    {
                        int choice = ThemedDialog.Show(this, "打开这个文件？", "这类文件可能包含宏或会运行程序。请只在确认来源可信时打开。", target.Path,
                            DialogIcon.Warning, new string[] { "打开", "取消" }, 1, 1);
                        if (choice != 0) return;
                    }
                    ShellOpen(target);
                    return;
                case LinkKind.LocalDirectory:
                case LinkKind.Web:
                case LinkKind.Mail:
                    ShellOpen(target);
                    return;
            }
        }

        private void ShellOpen(LinkTarget target)
        {
            string shellTarget;
            if (!LinkPolicy.TryGetShellTarget(target, out shellTarget))
            {
                ThemedDialog.Inform(this, "无法打开链接", "链接指向的本地文件或文件夹不存在、已被更改，或者该类型出于安全考虑不支持打开。", DialogIcon.Warning);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo { FileName = shellTarget, UseShellExecute = true });
                SetStatus("已打开 " + shellTarget, true);
            }
            catch (Exception ex)
            {
                ThemedDialog.Show(this, "无法打开链接", ex.Message, shellTarget, DialogIcon.Warning, new string[] { "确定" }, 0, 0);
            }
        }

        // ------------------------------------------------------------------ history

        private void PushHistory()
        {
            NavEntry entry = new NavEntry();
            entry.Path = currentPath;
            entry.Offset = previewScroll != null ? previewScroll.VerticalOffset : 0;
            backStack.Add(entry);
            if (backStack.Count > 60) backStack.RemoveAt(0);
            forwardStack.Clear();
            UpdateNavigationButtons();
        }

        private NavEntry CurrentEntry()
        {
            NavEntry entry = new NavEntry();
            entry.Path = currentPath;
            entry.Offset = previewScroll != null ? previewScroll.VerticalOffset : 0;
            return entry;
        }

        private void NavigateToFile(string path, string fragment, bool pushHistory)
        {
            NavEntry previous = CurrentEntry();
            if (LoadFile(path, 0, fragment, null))
            {
                if (pushHistory && (previous.Path != null || lastMarkdownText != null))
                {
                    backStack.Add(previous);
                    if (backStack.Count > 60) backStack.RemoveAt(0);
                    forwardStack.Clear();
                }
            }
            UpdateNavigationButtons();
        }

        private void NavigateBack() { NavigateHistory(backStack, forwardStack); }
        private void NavigateForward() { NavigateHistory(forwardStack, backStack); }

        private void NavigateHistory(List<NavEntry> from, List<NavEntry> to)
        {
            if (from.Count == 0) return;
            NavEntry entry = from[from.Count - 1];
            bool samePath = String.Equals(entry.Path, currentPath, StringComparison.OrdinalIgnoreCase);
            if (samePath)
            {
                from.RemoveAt(from.Count - 1);
                to.Add(CurrentEntry());
                if (viewMode == ViewMode.Source) SetViewMode(ViewMode.Split);
                RestorePreviewOffsetWhenReady(entry.Offset);
                UpdateNavigationButtons();
                return;
            }
            if (String.IsNullOrEmpty(entry.Path) || !File.Exists(entry.Path))
            {
                from.RemoveAt(from.Count - 1);
                UpdateNavigationButtons();
                SetStatus("上一个位置的文件已不存在", true);
                return;
            }
            if (!ConfirmDiscardOrSave()) return;
            NavEntry current = CurrentEntry();
            from.RemoveAt(from.Count - 1);
            if (LoadFile(entry.Path, 0, null, entry.Offset)) to.Add(current);
            UpdateNavigationButtons();
        }

        private void UpdateNavigationButtons()
        {
            if (backButton == null) return;
            backButton.IsEnabled = backStack.Count > 0;
            forwardButton.IsEnabled = forwardStack.Count > 0;
        }

        // ------------------------------------------------------------------ info bar

        private void ShowInfoBar(DialogIcon icon, string message, params InfoAction[] actions)
        {
            bool warning = icon == DialogIcon.Warning;
            infoBar.SetResourceReference(Border.BackgroundProperty, warning ? "WarningBg" : "InfoBg");
            infoBar.SetResourceReference(Border.BorderBrushProperty, warning ? "WarningBorder" : "InfoBorder");
            infoText.SetResourceReference(TextBlock.ForegroundProperty, warning ? "WarningText" : "Text");
            FrameworkElement glyph = Icons.Create(warning ? Icons.Warning : Icons.Info, 16, 1.5);
            infoIconHost.Child = glyph;
            infoIconHost.SetResourceReference(TextElement.ForegroundProperty, warning ? "AlertWarning" : "Accent");
            infoText.Text = message;
            infoActions.Children.Clear();
            foreach (InfoAction action in actions)
            {
                InfoAction captured = action;
                Button button = new Button();
                button.SetResourceReference(StyleProperty, "TextButton");
                button.Content = action.Label;
                button.Height = 28;
                button.MinWidth = 0;
                button.Padding = new Thickness(12, 0, 12, 0);
                button.Margin = new Thickness(6, 0, 0, 0);
                button.Click += delegate { captured.Handler(); };
                infoActions.Children.Add(button);
            }
            infoBar.Visibility = Visibility.Visible;
        }

        private void HideInfoBar()
        {
            if (infoBar != null) infoBar.Visibility = Visibility.Collapsed;
        }
    }
}
