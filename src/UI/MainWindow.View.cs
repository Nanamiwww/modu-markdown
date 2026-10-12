using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMarkdownReader.Core;
using CodexMarkdownReader.Markdown;

namespace CodexMarkdownReader.UI
{
    public sealed partial class MainWindow
    {
        private double pendingPreviewProgress = -1;
        private bool paneLayoutTransition;
        private int layoutGeneration;

        // ------------------------------------------------------------------ view modes

        // Each window keeps its own view. settings.View is only the default for the next window
        // (the last view the user picked), so switching one window never leaves another one half-updated.
        private ViewMode viewMode;

        internal ViewMode CurrentViewMode { get { return viewMode; } }

        public void SetViewMode(ViewMode mode)
        {
            if (viewMode == mode) { UpdateViewButtons(); return; }
            if (viewMode == ViewMode.Split) CaptureSplitRatio();
            viewMode = mode;
            settings.View = mode;
            ApplyViewMode(true);
            settings.Save();
            SetStatus(mode == ViewMode.Preview ? "阅读模式：只显示预览" : mode == ViewMode.Source ? "源码模式：只显示 Markdown" : "分栏模式：源码与预览并排", true);
        }

        private void UpdateViewButtons()
        {
            updatingViewButtons = true;
            viewSourceButton.IsChecked = viewMode == ViewMode.Source;
            viewSplitButton.IsChecked = viewMode == ViewMode.Split;
            viewPreviewButton.IsChecked = viewMode == ViewMode.Preview;
            updatingViewButtons = false;
        }

        private void ApplyViewMode(bool moveFocus)
        {
            UpdateViewButtons();
            double previewProgress = paneLayoutTransition ? pendingPreviewProgress : GetScrollProgress(previewScroll);
            double sourceProgress = GetScrollProgress(sourceScroll);
            pendingPreviewProgress = previewProgress;
            paneLayoutTransition = true;
            int generation = ++layoutGeneration;
            ApplyColumns(sourceColumn, splitterColumn, previewColumn, sourcePanel, splitter, previewPanel, viewMode, settings.SplitRatio);
            UpdatePosition();
            if (!IsLoaded) { paneLayoutTransition = false; return; }
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                if (generation != layoutGeneration) return;
                try
                {
                    contentGrid.UpdateLayout();
                    EnsureScrollViewers();
                    UpdatePreviewPadding();
                    if (viewMode == ViewMode.Source) SetScrollProgress(sourceScroll, previewProgress >= 0 ? previewProgress : sourceProgress);
                    else
                    {
                        MarkProgrammaticPreviewScroll();
                        SetScrollProgress(previewScroll, previewProgress >= 0 ? previewProgress : sourceProgress);
                        if (viewMode == ViewMode.Split && settings.SyncScroll)
                        {
                            // The editor only gets its real extent after it has been laid out once.
                            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
                            {
                                EnsureScrollViewers();
                                ignoreSourceScrollUntil = DateTime.UtcNow.AddMilliseconds(250);
                                SetScrollProgress(sourceScroll, GetScrollProgress(previewScroll));
                            }));
                        }
                    }
                    if (moveFocus) FocusPrimaryPane();
                }
                finally
                {
                    if (generation == layoutGeneration) paneLayoutTransition = false;
                }
            }));
        }

        internal static void ApplyColumns(ColumnDefinition sourceColumn, ColumnDefinition splitterColumn, ColumnDefinition previewColumn,
            FrameworkElement sourcePanel, FrameworkElement splitter, FrameworkElement previewPanel, ViewMode mode, double splitRatio)
        {
            double ratio = Double.IsNaN(splitRatio) || Double.IsInfinity(splitRatio) ? 0.5 : Math.Max(0.18, Math.Min(0.82, splitRatio));
            switch (mode)
            {
                case ViewMode.Preview:
                    sourcePanel.Visibility = Visibility.Collapsed;
                    splitter.Visibility = Visibility.Collapsed;
                    previewPanel.Visibility = Visibility.Visible;
                    sourceColumn.MinWidth = 0;
                    sourceColumn.Width = new GridLength(0);
                    splitterColumn.Width = new GridLength(0);
                    previewColumn.MinWidth = 200;
                    previewColumn.Width = new GridLength(1, GridUnitType.Star);
                    break;
                case ViewMode.Source:
                    previewPanel.Visibility = Visibility.Collapsed;
                    splitter.Visibility = Visibility.Collapsed;
                    sourcePanel.Visibility = Visibility.Visible;
                    previewColumn.MinWidth = 0;
                    previewColumn.Width = new GridLength(0);
                    splitterColumn.Width = new GridLength(0);
                    sourceColumn.MinWidth = 200;
                    sourceColumn.Width = new GridLength(1, GridUnitType.Star);
                    break;
                default:
                    sourceColumn.MinWidth = 200;
                    previewColumn.MinWidth = 200;
                    sourceColumn.Width = new GridLength(ratio, GridUnitType.Star);
                    splitterColumn.Width = new GridLength(6);
                    previewColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
                    sourcePanel.Visibility = Visibility.Visible;
                    splitter.Visibility = Visibility.Visible;
                    previewPanel.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void FocusPrimaryPane()
        {
            if (startPage.Visibility == Visibility.Visible) return;
            if (viewMode == ViewMode.Preview) preview.Focus(); else source.Focus();
        }

        private void CycleFocus(bool backwards)
        {
            System.Collections.Generic.List<UIElement> order = new System.Collections.Generic.List<UIElement>();
            if (settings.SidebarVisible) order.Add(settings.Sidebar == SidebarTab.Outline ? (UIElement)outlineList : recentList);
            if (viewMode != ViewMode.Preview) order.Add(source);
            if (viewMode == ViewMode.Split) order.Add(splitter);
            if (viewMode != ViewMode.Source) order.Add(preview);
            if (order.Count == 0) return;
            int current = -1;
            for (int i = 0; i < order.Count; i++) if (order[i].IsKeyboardFocusWithin) { current = i; break; }
            int next = current < 0 ? 0 : (current + (backwards ? order.Count - 1 : 1)) % order.Count;
            order[next].Focus();
        }

        // ------------------------------------------------------------------ sidebar

        private void ApplySidebarVisibility()
        {
            sidebarToggle.IsChecked = settings.SidebarVisible;
            sidebarPanel.Visibility = settings.SidebarVisible ? Visibility.Visible : Visibility.Collapsed;
            sidebarResizer.Visibility = sidebarPanel.Visibility;
            sidebarColumn.Width = settings.SidebarVisible ? new GridLength(settings.SidebarWidth) : new GridLength(0);
            ApplySidebarTab();
            if (IsLoaded) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdatePreviewPadding));
        }

        private void ApplySidebarTab()
        {
            outlineTab.IsChecked = settings.Sidebar == SidebarTab.Outline;
            recentTab.IsChecked = settings.Sidebar == SidebarTab.Recent;
            outlinePage.Visibility = settings.Sidebar == SidebarTab.Outline ? Visibility.Visible : Visibility.Collapsed;
            recentPage.Visibility = settings.Sidebar == SidebarTab.Recent ? Visibility.Visible : Visibility.Collapsed;
            if (settings.Sidebar == SidebarTab.Recent) RefreshRecentLists();
        }

        private void ShowSidebarTab(SidebarTab tab)
        {
            settings.SidebarVisible = true;
            settings.Sidebar = tab;
            ApplySidebarVisibility();
            SharedSettingsChanged();
        }

        // ------------------------------------------------------------------ app-wide preferences

        // Sidebar, zoom and reading width are app-wide preferences: a change in one window applies to all windows.
        private bool applyingSharedSettings;

        internal void ApplySharedSettings()
        {
            applyingSharedSettings = true;
            try
            {
                ApplySidebarVisibility();
                ApplyZoom();
                UpdatePreviewPadding();
            }
            finally { applyingSharedSettings = false; }
        }

        private void SharedSettingsChanged()
        {
            settings.Save();
            if (!applyingSharedSettings) ReaderApp.ApplySharedSettings(this);
        }

        // ------------------------------------------------------------------ zoom & reading width

        private void SetZoom(int zoom)
        {
            int value = Math.Max(50, Math.Min(250, zoom));
            if (value == settings.Zoom) { ApplyZoom(); return; }
            settings.Zoom = value;
            ApplyZoom();
            SharedSettingsChanged();
            SetStatus("缩放 " + value.ToString(CultureInfo.InvariantCulture) + "%", true);
        }

        private void StepZoom(int direction)
        {
            int[] steps = { 50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250 };
            int current = settings.Zoom;
            if (direction > 0)
            {
                foreach (int step in steps) if (step > current) { SetZoom(step); return; }
            }
            else
            {
                for (int i = steps.Length - 1; i >= 0; i--) if (steps[i] < current) { SetZoom(steps[i]); return; }
            }
        }

        private void ApplyZoom()
        {
            double factor = settings.Zoom / 100.0;
            preview.Zoom = settings.Zoom;
            source.FontSize = Math.Round(14 * factor * 2) / 2;
            source.SetValue(TextBlock.LineHeightProperty, Math.Round(23 * factor));
            zoomButton.Content = settings.Zoom.ToString(CultureInfo.InvariantCulture) + "%";
            UpdatePreviewPadding();
        }

        /// <summary>Keeps the text column at a comfortable reading width and centers it.</summary>
        private void UpdatePreviewPadding()
        {
            FlowDocument document = preview == null ? null : preview.Document;
            if (document == null) return;
            double zoom = Math.Max(0.5, preview.Zoom / 100.0);
            double available = (preview.ActualWidth - 14) / zoom;
            if (available <= 0) return;
            double minimum = available < 560 ? 22 : 40;
            double horizontal = minimum;
            if (settings.ReadingWidth > 0) horizontal = Math.Max(minimum, (available - settings.ReadingWidth) / 2);
            Thickness current = document.PagePadding;
            if (Math.Abs(current.Left - horizontal) > 0.5 || Math.Abs(current.Right - horizontal) > 0.5)
                document.PagePadding = new Thickness(horizontal, 26, horizontal, 72);
        }

        private void SetReadingWidth(int width)
        {
            settings.ReadingWidth = width;
            UpdatePreviewPadding();
            SharedSettingsChanged();
            SetStatus(width == 0 ? "阅读宽度：铺满" : "阅读宽度：" + width.ToString(CultureInfo.InvariantCulture) + " 像素", true);
        }

        // ------------------------------------------------------------------ splitter

        private void UpdateSplitterVisualState()
        {
            if (splitter == null) return;
            bool focused = splitter.IsKeyboardFocusWithin;
            bool hovered = splitter.IsMouseOver;
            object brushKey;
            if (SystemParameters.HighContrast) brushKey = focused || hovered ? SystemColors.HighlightBrushKey : SystemColors.WindowTextBrushKey;
            else brushKey = focused || hovered ? (object)"Accent" : (object)"Splitter";
            splitter.SetResourceReference(BackgroundProperty, brushKey);
            splitter.Opacity = focused ? 1.0 : (hovered ? 0.6 : 1.0);
        }

        private void CaptureSplitRatio()
        {
            if (viewMode != ViewMode.Split || paneLayoutTransition || sourcePanel.Visibility != Visibility.Visible) return;
            double ratio;
            if (TryCalculateSplitRatio(sourceColumn.ActualWidth, previewColumn.ActualWidth, out ratio)) settings.SplitRatio = ratio;
        }

        internal static bool TryCalculateSplitRatio(double sourceWidth, double previewWidth, out double ratio)
        {
            ratio = 0;
            if (Double.IsNaN(sourceWidth) || Double.IsInfinity(sourceWidth) || Double.IsNaN(previewWidth) || Double.IsInfinity(previewWidth) || sourceWidth <= 1 || previewWidth <= 1) return false;
            ratio = Math.Max(0.18, Math.Min(0.82, sourceWidth / (sourceWidth + previewWidth)));
            return true;
        }

        private void ResetSplitter()
        {
            settings.SplitRatio = 0.5;
            if (viewMode == ViewMode.Split)
            {
                sourceColumn.Width = new GridLength(1, GridUnitType.Star);
                previewColumn.Width = new GridLength(1, GridUnitType.Star);
            }
            settings.Save();
        }

        // ------------------------------------------------------------------ scroll helpers

        private ScrollViewer sourceScroll;
        private ScrollViewer previewScroll;
        private bool synchronizingScroll;

        private void EnsureScrollViewers()
        {
            source.ApplyTemplate();
            preview.ApplyTemplate();
            ScrollViewer foundSource = FindVisualChild<ScrollViewer>(source);
            ScrollViewer foundPreview = FindVisualChild<ScrollViewer>(preview);
            if (foundSource != null && !Object.ReferenceEquals(foundSource, sourceScroll))
            {
                if (sourceScroll != null) sourceScroll.ScrollChanged -= SourceScrollChanged;
                sourceScroll = foundSource;
                sourceScroll.ScrollChanged += SourceScrollChanged;
            }
            if (foundPreview != null && !Object.ReferenceEquals(foundPreview, previewScroll))
            {
                if (previewScroll != null) previewScroll.ScrollChanged -= PreviewScrollChanged;
                previewScroll = foundPreview;
                previewScroll.ScrollChanged += PreviewScrollChanged;
            }
        }

        private void SourceScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0) return;
            SyncScroll(sourceScroll, previewScroll);
            if (viewMode == ViewMode.Source) { scrollSpyTimer.Stop(); scrollSpyTimer.Start(); }
        }

        private void PreviewScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0) return;
            SyncScroll(previewScroll, sourceScroll);
            scrollSpyTimer.Stop();
            scrollSpyTimer.Start();
        }

        private void SyncScroll(ScrollViewer from, ScrollViewer to)
        {
            if (viewMode != ViewMode.Split || !settings.SyncScroll || paneLayoutTransition || synchronizingScroll || suppressScrollSync || from == null || to == null || from.ScrollableHeight <= 0) return;
            // Ignore the echo of a scroll we caused ourselves, otherwise both panes nudge each other.
            DateTime now = DateTime.UtcNow;
            if (Object.ReferenceEquals(from, sourceScroll) && now < ignoreSourceScrollUntil) return;
            if (Object.ReferenceEquals(from, previewScroll) && now < ignorePreviewScrollUntil) return;
            if (Object.ReferenceEquals(to, sourceScroll)) ignoreSourceScrollUntil = now.AddMilliseconds(160);
            else ignorePreviewScrollUntil = now.AddMilliseconds(160);
            synchronizingScroll = true;
            try { to.ScrollToVerticalOffset((from.VerticalOffset / from.ScrollableHeight) * to.ScrollableHeight); }
            finally { synchronizingScroll = false; }
        }

        private bool suppressScrollSync;
        private DateTime ignoreSourceScrollUntil, ignorePreviewScrollUntil;

        private void MarkProgrammaticPreviewScroll()
        {
            ignorePreviewScrollUntil = DateTime.UtcNow.AddMilliseconds(250);
        }

        private static double GetScrollProgress(ScrollViewer viewer)
        {
            if (viewer == null || viewer.ScrollableHeight <= 0) return 0;
            return Math.Max(0, Math.Min(1, viewer.VerticalOffset / viewer.ScrollableHeight));
        }

        private static void SetScrollProgress(ScrollViewer viewer, double progress)
        {
            if (viewer == null) return;
            double safe = Double.IsNaN(progress) || Double.IsInfinity(progress) ? 0 : Math.Max(0, Math.Min(1, progress));
            viewer.ScrollToVerticalOffset(safe * viewer.ScrollableHeight);
        }

        internal static T FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                T match = child as T;
                if (match != null) return match;
                match = FindVisualChild<T>(child);
                if (match != null) return match;
            }
            return null;
        }

        // ------------------------------------------------------------------ status bar

        private void SetStatus(string message, bool transient)
        {
            normalStatus = String.IsNullOrEmpty(message) ? "就绪" : message;
            RefreshStatusText();
            statusTimer.Stop();
            if (transient) statusTimer.Start();
        }

        private void RefreshStatusText()
        {
            if (statusText == null) return;
            statusText.Text = splitterHintActive ? "分割条已选中 · ← / → 调整宽度，Enter 恢复均分，F6 切换区域" : normalStatus;
        }

        private void RefreshDirtyStatus()
        {
            if (fileMissing) SetStatus("原文件已不存在，保存时会重新创建", false);
            else SetStatus(IsDirty ? "未保存" : (String.IsNullOrEmpty(currentPath) ? "就绪" : "已保存"), false);
        }

        private void UpdatePosition()
        {
            if (positionText == null) return;
            if (viewMode == ViewMode.Preview) { positionText.Visibility = Visibility.Collapsed; return; }
            positionText.Visibility = Visibility.Visible;
            string text = source.Text ?? "";
            int index = Math.Max(0, Math.Min(text.Length, source.SelectionStart));
            int line = 0, lineStart = 0;
            for (int i = 0; i < index; i++)
            {
                if (text[i] == '\n') { line++; lineStart = i + 1; }
            }
            string position = "行 " + (line + 1).ToString(CultureInfo.InvariantCulture) + "，列 " + (index - lineStart + 1).ToString(CultureInfo.InvariantCulture);
            if (source.SelectionLength > 0) position += "（已选 " + TextUtil.CountTextUnits(source.SelectedText).ToString(CultureInfo.InvariantCulture) + " 字）";
            positionText.Text = position;
        }

        /// <summary>Character index where a logical (unwrapped) line starts; -1 when out of range.</summary>
        private int LogicalLineStart(int line)
        {
            string text = source.Text ?? "";
            if (line <= 0) return 0;
            int current = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                current++;
                if (current == line) return i + 1;
            }
            return -1;
        }

        /// <summary>Logical (unwrapped) line containing a character index.</summary>
        private int LogicalLineOf(int index)
        {
            string text = source.Text ?? "";
            int line = 0;
            int limit = Math.Min(index, text.Length);
            for (int i = 0; i < limit; i++) if (text[i] == '\n') line++;
            return line;
        }

        private void UpdateWordCount()
        {
            if (countText == null) return;
            countText.Text = TextUtil.CountTextUnits(source.Text).ToString("N0", CultureInfo.InvariantCulture) + " 字";
        }

        private void UpdateFormatIndicator()
        {
            if (encodingButton == null || format == null) return;
            string text = format.EncodingLabel + " · " + format.LineEndingLabel;
            if (format.MixedLineEndings) text += "（混合）";
            encodingButton.Content = text;
        }
    }
}
