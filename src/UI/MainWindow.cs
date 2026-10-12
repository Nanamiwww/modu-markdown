using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using CodexMarkdownReader.Core;
using CodexMarkdownReader.Markdown;
using CodexMarkdownReader.Platform;
using CodexMarkdownReader.Rendering;

namespace CodexMarkdownReader.UI
{
    /// <summary>The reader window. Split across partial files by concern.</summary>
    public sealed partial class MainWindow : Window
    {
        private const double TitleBarHeight = 44;

        private readonly AppSettings settings;
        private readonly bool isPrimaryWindow;

        // Layout
        private Border windowFrame;
        private Grid root;
        private Grid contentGrid;
        private ColumnDefinition sidebarColumn, sourceColumn, splitterColumn, previewColumn;
        private Border sidebarPanel;
        private Thumb sidebarResizer;
        private Grid sourcePanel;
        private GridSplitter splitter;
        private Grid previewPanel;
        private TextBox source;
        private FlowDocumentScrollViewer preview;
        private Border startPage;
        private StackPanel startRecentList;
        private Border dragOverlay;
        private TextBlock dragOverlayDetail;

        // Title bar
        private ToggleButton sidebarToggle;
        private Button backButton, forwardButton, saveButton, maximizeButton;
        private Button fileNameButton;
        private TextBlock fileNameText, fileDirectoryText;
        private Border dirtyDot;
        private RadioButton viewSourceButton, viewSplitButton, viewPreviewButton;
        private bool updatingViewButtons;

        // Info bar
        private Border infoBar;
        private TextBlock infoText;
        private StackPanel infoActions;
        private Border infoIconHost;

        // Sidebar
        private RadioButton outlineTab, recentTab;
        private ListBox outlineList, recentList;
        private TextBlock outlineEmpty, recentEmpty;
        private Grid outlinePage, recentPage;

        // Status bar
        private TextBlock statusText, positionText, countText;
        private Button encodingButton, zoomButton;
        private string normalStatus = "就绪";
        private bool splitterHintActive;

        // Timers
        private readonly DispatcherTimer renderTimer;
        private readonly DispatcherTimer draftTimer;
        private readonly DispatcherTimer metricsTimer;
        private readonly DispatcherTimer scrollSpyTimer;
        private readonly DispatcherTimer statusTimer;

        public string PendingStartupMessage;
        private bool constructed;

        public MainWindow(string startupFile, DraftInfo recoveredDraft, bool primary)
        {
            settings = ReaderApp.Settings;
            isPrimaryWindow = primary;
            try
            {
            Language = ReaderTypography.Language;
            FontFamily = ReaderTypography.Interface;
            FontSize = 13;
            Title = "墨读 Markdown";
            Width = settings.Width;
            Height = settings.Height;
            MinWidth = 760;
            MinHeight = 520;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            AllowDrop = true;
            SetResourceReference(BackgroundProperty, "WindowBg");
            SetResourceReference(ForegroundProperty, "Text");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            WindowChrome chrome = new WindowChrome();
            chrome.CaptionHeight = TitleBarHeight;
            chrome.ResizeBorderThickness = new Thickness(6);
            chrome.CornerRadius = new CornerRadius(0);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, chrome);

            renderTimer = new DispatcherTimer(DispatcherPriority.Background);
            renderTimer.Interval = TimeSpan.FromMilliseconds(140);
            renderTimer.Tick += delegate { renderTimer.Stop(); RenderNow(true); };
            draftTimer = new DispatcherTimer(DispatcherPriority.Background);
            draftTimer.Interval = TimeSpan.FromMilliseconds(800);
            draftTimer.Tick += delegate { draftTimer.Stop(); SaveDraftIfDirty(); };
            metricsTimer = new DispatcherTimer(DispatcherPriority.Background);
            metricsTimer.Interval = TimeSpan.FromMilliseconds(250);
            metricsTimer.Tick += delegate { metricsTimer.Stop(); UpdateWordCount(); };
            scrollSpyTimer = new DispatcherTimer(DispatcherPriority.Background);
            scrollSpyTimer.Interval = TimeSpan.FromMilliseconds(120);
            scrollSpyTimer.Tick += delegate { scrollSpyTimer.Stop(); UpdateActiveHeading(); };
            statusTimer = new DispatcherTimer(DispatcherPriority.Background);
            statusTimer.Interval = TimeSpan.FromSeconds(6);
            statusTimer.Tick += delegate { statusTimer.Stop(); RefreshDirtyStatus(); };

            draftSession = new DraftSession();
            viewMode = settings.View;
            BuildLayout();
            ApplyViewMode(false);
            ApplySidebarVisibility();
            ApplyZoom();
            OnThemeChanged();
            UpdateNavigationButtons();

            source.GotKeyboardFocus += delegate { lastFocusWasPreview = false; };
            preview.GotKeyboardFocus += delegate { lastFocusWasPreview = true; };
            source.TextChanged += SourceTextChanged;
            source.SelectionChanged += delegate { UpdatePosition(); };
            Closing += WindowClosing;
            Closed += WindowClosed;
            Loaded += WindowLoaded;
            StateChanged += WindowStateChanged;
            SourceInitialized += delegate { ApplyNativeWindowFrame(); };
            Activated += delegate { ReaderApp.RefreshSystemTheme(); CheckExternalChangeOnActivate(); };
            Deactivated += delegate { HideDragOverlay(); };
            PreviewKeyDown += WindowPreviewKeyDown;
            PreviewMouseDown += WindowPreviewMouseDown;
            PreviewMouseWheel += WindowPreviewMouseWheel;
            AddHandler(DragDrop.PreviewDragEnterEvent, new DragEventHandler(WindowPreviewDragEnter), true);
            AddHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(WindowPreviewDragOver), true);
            AddHandler(DragDrop.PreviewDragLeaveEvent, new DragEventHandler(WindowPreviewDragLeave), true);
            AddHandler(DragDrop.PreviewDropEvent, new DragEventHandler(WindowPreviewDrop), true);

            RestoreGeometry(primary);
            LoadInitialDocument(startupFile, recoveredDraft);
            constructed = true;
            }
            catch
            {
                // A half-built window would keep the process alive invisibly; tear it down before reporting.
                if (draftSession != null) draftSession.Dispose();
                allowClose = true;
                try { Close(); } catch { }
                throw;
            }
        }

        // ------------------------------------------------------------------ layout

        private void BuildLayout()
        {
            root = new Grid();
            root.AllowDrop = true;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

            windowFrame = new Border();
            windowFrame.BorderThickness = new Thickness(1);
            windowFrame.SnapsToDevicePixels = true;
            windowFrame.SetResourceReference(Border.BorderBrushProperty, "WindowBorder");
            windowFrame.SetResourceReference(Border.BackgroundProperty, "WindowBg");
            windowFrame.Child = root;
            Content = windowFrame;

            UIElement titleBar = BuildTitleBar();
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            infoBar = BuildInfoBar();
            Grid.SetRow(infoBar, 1);
            root.Children.Add(infoBar);

            BuildContent();
            Grid.SetRow(contentGrid, 2);
            root.Children.Add(contentGrid);

            startPage = BuildStartPage();
            Grid.SetRow(startPage, 2);
            Panel.SetZIndex(startPage, 50);
            root.Children.Add(startPage);

            UIElement status = BuildStatusBar();
            Grid.SetRow(status, 3);
            root.Children.Add(status);

            dragOverlay = BuildDropOverlay();
            Grid.SetRowSpan(dragOverlay, 4);
            Panel.SetZIndex(dragOverlay, 1000);
            root.Children.Add(dragOverlay);
        }

        private Button MakeIconButton(string icon, string tooltip, string automationId, RoutedEventHandler click)
        {
            Button button = new Button();
            button.SetResourceReference(StyleProperty, "IconButton");
            button.Content = Icons.Create(icon, 16);
            button.ToolTip = tooltip;
            string name = tooltip;
            int paren = name.IndexOf('（');
            if (paren > 0) name = name.Substring(0, paren);
            AutomationProperties.SetName(button, name);
            if (automationId != null) AutomationProperties.SetAutomationId(button, automationId);
            if (click != null) button.Click += click;
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            return button;
        }

        private static FrameworkElement VerticalRule()
        {
            Border rule = new Border();
            rule.Width = 1;
            rule.Height = 18;
            rule.Margin = new Thickness(6, 0, 6, 0);
            rule.SetResourceReference(Border.BackgroundProperty, "Border");
            return rule;
        }

        private UIElement BuildTitleBar()
        {
            Border shell = new Border();
            shell.SetResourceReference(Border.BackgroundProperty, "TitleBar");
            shell.SetResourceReference(Border.BorderBrushProperty, "Border");
            shell.BorderThickness = new Thickness(0, 0, 0, 1);

            Grid bar = new Grid();
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 60 });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            shell.Child = bar;

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(10, 0, 0, 0);

            Border mark = new Border();
            mark.Width = 22; mark.Height = 22;
            mark.CornerRadius = new CornerRadius(6);
            mark.Margin = new Thickness(2, 0, 10, 0);
            mark.SetResourceReference(Border.BackgroundProperty, "Accent");
            TextBlock markText = new TextBlock();
            markText.Text = "墨";
            markText.FontSize = 12.5;
            markText.FontWeight = FontWeights.SemiBold;
            markText.HorizontalAlignment = HorizontalAlignment.Center;
            markText.VerticalAlignment = VerticalAlignment.Center;
            markText.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            mark.Child = markText;
            left.Children.Add(mark);

            sidebarToggle = new ToggleButton();
            sidebarToggle.SetResourceReference(StyleProperty, "ToggleIconButton");
            sidebarToggle.Content = Icons.Create(Icons.PanelLeft, 16);
            sidebarToggle.ToolTip = "侧栏：大纲与最近文件（Ctrl+Shift+O）";
            AutomationProperties.SetName(sidebarToggle, "显示或隐藏侧栏");
            AutomationProperties.SetAutomationId(sidebarToggle, "SidebarToggle");
            sidebarToggle.Click += delegate { settings.SidebarVisible = sidebarToggle.IsChecked == true; ApplySidebarVisibility(); SharedSettingsChanged(); };
            WindowChrome.SetIsHitTestVisibleInChrome(sidebarToggle, true);
            left.Children.Add(sidebarToggle);

            backButton = MakeIconButton(Icons.ArrowLeft, "后退（Alt+←）", "BackButton", delegate { NavigateBack(); });
            forwardButton = MakeIconButton(Icons.ArrowRight, "前进（Alt+→）", "ForwardButton", delegate { NavigateForward(); });
            left.Children.Add(backButton);
            left.Children.Add(forwardButton);
            left.Children.Add(VerticalRule());
            left.Children.Add(MakeIconButton(Icons.Folder, "打开（Ctrl+O）", "OpenButton", delegate { OpenWithDialog(); }));
            saveButton = MakeIconButton(Icons.Save, "保存（Ctrl+S）", "SaveButton", delegate { SaveDocument(false); });
            left.Children.Add(saveButton);
            Grid.SetColumn(left, 0);
            bar.Children.Add(left);

            // File name (clickable) and folder
            StackPanel title = new StackPanel();
            title.Orientation = Orientation.Horizontal;
            title.VerticalAlignment = VerticalAlignment.Center;
            title.Margin = new Thickness(10, 0, 12, 0);
            fileNameButton = new Button();
            fileNameButton.SetResourceReference(StyleProperty, "GhostButton");
            fileNameButton.Height = 30;
            fileNameButton.Padding = new Thickness(8, 0, 6, 0);
            StackPanel nameContent = new StackPanel();
            nameContent.Orientation = Orientation.Horizontal;
            fileNameText = new TextBlock();
            fileNameText.FontSize = 13;
            fileNameText.FontWeight = FontWeights.SemiBold;
            fileNameText.VerticalAlignment = VerticalAlignment.Center;
            fileNameText.TextTrimming = TextTrimming.CharacterEllipsis;
            fileNameText.MaxWidth = 360;
            nameContent.Children.Add(fileNameText);
            dirtyDot = new Border();
            dirtyDot.Width = 7; dirtyDot.Height = 7;
            dirtyDot.CornerRadius = new CornerRadius(4);
            dirtyDot.Margin = new Thickness(7, 1, 0, 0);
            dirtyDot.VerticalAlignment = VerticalAlignment.Center;
            dirtyDot.SetResourceReference(Border.BackgroundProperty, "Accent");
            dirtyDot.Visibility = Visibility.Collapsed;
            dirtyDot.ToolTip = "有未保存的更改";
            nameContent.Children.Add(dirtyDot);
            FrameworkElement chevron = Icons.Create(Icons.ChevronDown, 12);
            chevron.Margin = new Thickness(6, 1, 0, 0);
            chevron.Opacity = 0.7;
            nameContent.Children.Add(chevron);
            fileNameButton.Content = nameContent;
            fileNameButton.ToolTip = "文件操作";
            AutomationProperties.SetName(fileNameButton, "文件操作");
            AutomationProperties.SetAutomationId(fileNameButton, "FileNameButton");
            fileNameButton.Click += delegate { ShowFileMenu(); };
            WindowChrome.SetIsHitTestVisibleInChrome(fileNameButton, true);
            title.Children.Add(fileNameButton);
            fileDirectoryText = new TextBlock();
            fileDirectoryText.FontSize = 12;
            fileDirectoryText.VerticalAlignment = VerticalAlignment.Center;
            fileDirectoryText.TextTrimming = TextTrimming.CharacterEllipsis;
            fileDirectoryText.Margin = new Thickness(4, 0, 0, 0);
            fileDirectoryText.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
            title.Children.Add(fileDirectoryText);
            Grid.SetColumn(title, 1);
            bar.Children.Add(title);

            // View mode segmented control
            Border segment = new Border();
            segment.CornerRadius = new CornerRadius(7);
            segment.Padding = new Thickness(2);
            segment.VerticalAlignment = VerticalAlignment.Center;
            segment.SetResourceReference(Border.BackgroundProperty, "Surface2");
            StackPanel segments = new StackPanel();
            segments.Orientation = Orientation.Horizontal;
            viewSourceButton = MakeSegment(Icons.Source, "源码", "只显示 Markdown 源码（Ctrl+1）", "ViewSourceButton", ViewMode.Source);
            viewSplitButton = MakeSegment(Icons.Split, "分栏", "源码与预览并排（Ctrl+2）", "ViewSplitButton", ViewMode.Split);
            viewPreviewButton = MakeSegment(Icons.Preview, "预览", "只显示预览，适合阅读（Ctrl+3）", "ViewPreviewButton", ViewMode.Preview);
            segments.Children.Add(viewSourceButton);
            segments.Children.Add(viewSplitButton);
            segments.Children.Add(viewPreviewButton);
            segment.Child = segments;
            Grid.SetColumn(segment, 2);
            bar.Children.Add(segment);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(8, 0, 6, 0);
            right.Children.Add(MakeIconButton(Icons.Search, "查找（Ctrl+F）", "FindButton", delegate { OpenFindBar(); }));
            Button more = MakeIconButton(Icons.More, "更多（Alt）", "MoreButton", null);
            more.Click += delegate { ShowMainMenu(more); };
            right.Children.Add(more);
            Grid.SetColumn(right, 3);
            bar.Children.Add(right);

            StackPanel caption = new StackPanel();
            caption.Orientation = Orientation.Horizontal;
            caption.VerticalAlignment = VerticalAlignment.Top;
            Button minimize = MakeCaptionButton(Icons.CaptionMinimize, "最小化", "MinimizeButton", false);
            minimize.Click += delegate { SystemCommands.MinimizeWindow(this); };
            maximizeButton = MakeCaptionButton(Icons.CaptionMaximize, "最大化", "MaximizeRestoreButton", false);
            maximizeButton.Click += delegate { ToggleMaximize(); };
            Button close = MakeCaptionButton(Icons.CaptionClose, "关闭", "CloseButton", true);
            close.Click += delegate { SystemCommands.CloseWindow(this); };
            caption.Children.Add(minimize);
            caption.Children.Add(maximizeButton);
            caption.Children.Add(close);
            Grid.SetColumn(caption, 4);
            bar.Children.Add(caption);
            return shell;
        }

        private RadioButton MakeSegment(string icon, string label, string tooltip, string automationId, ViewMode mode)
        {
            RadioButton button = new RadioButton();
            button.GroupName = "ViewMode" + GetHashCode().ToString();
            button.SetResourceReference(StyleProperty, "SegmentButton");
            StackPanel content = new StackPanel();
            content.Orientation = Orientation.Horizontal;
            content.Children.Add(Icons.Create(icon, 14));
            TextBlock text = new TextBlock();
            text.Text = label;
            text.Margin = new Thickness(6, 0, 0, 0);
            text.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(text);
            button.Content = content;
            button.ToolTip = tooltip;
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetAutomationId(button, automationId);
            button.Checked += delegate { if (!updatingViewButtons) SetViewMode(mode); };
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            return button;
        }

        private Button MakeCaptionButton(string glyph, string label, string automationId, bool close)
        {
            Button button = new Button();
            button.SetResourceReference(StyleProperty, close ? "CaptionCloseButton" : "CaptionButton");
            button.Height = TitleBarHeight - 1;
            button.Content = Icons.CreateCaption(glyph);
            button.ToolTip = label;
            AutomationProperties.SetName(button, label + "窗口");
            AutomationProperties.SetAutomationId(button, automationId);
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            return button;
        }

        private Border BuildInfoBar()
        {
            Border bar = new Border();
            bar.Visibility = Visibility.Collapsed;
            bar.BorderThickness = new Thickness(0, 0, 0, 1);
            bar.Padding = new Thickness(14, 6, 8, 6);
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            infoIconHost = new Border();
            infoIconHost.Margin = new Thickness(0, 0, 10, 0);
            infoIconHost.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(infoIconHost);
            infoText = new TextBlock();
            infoText.TextWrapping = TextWrapping.Wrap;
            infoText.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetLiveSetting(infoText, AutomationLiveSetting.Assertive);
            Grid.SetColumn(infoText, 1);
            grid.Children.Add(infoText);
            infoActions = new StackPanel();
            infoActions.Orientation = Orientation.Horizontal;
            infoActions.Margin = new Thickness(12, 0, 4, 0);
            Grid.SetColumn(infoActions, 2);
            grid.Children.Add(infoActions);
            Button close = MakeIconButton(Icons.Close, "关闭提示", "InfoBarClose", delegate { HideInfoBar(); });
            close.Width = 28; close.Height = 28;
            Grid.SetColumn(close, 3);
            grid.Children.Add(close);
            bar.Child = grid;
            return bar;
        }

        private void BuildContent()
        {
            contentGrid = new Grid();
            contentGrid.AllowDrop = true;
            sidebarColumn = new ColumnDefinition { Width = new GridLength(settings.SidebarWidth), MinWidth = 0 };
            sourceColumn = new ColumnDefinition { Width = new GridLength(settings.SplitRatio, GridUnitType.Star), MinWidth = 200 };
            splitterColumn = new ColumnDefinition { Width = new GridLength(6) };
            previewColumn = new ColumnDefinition { Width = new GridLength(1 - settings.SplitRatio, GridUnitType.Star), MinWidth = 200 };
            contentGrid.ColumnDefinitions.Add(sidebarColumn);
            contentGrid.ColumnDefinitions.Add(sourceColumn);
            contentGrid.ColumnDefinitions.Add(splitterColumn);
            contentGrid.ColumnDefinitions.Add(previewColumn);

            sidebarPanel = BuildSidebar();
            Grid.SetColumn(sidebarPanel, 0);
            contentGrid.Children.Add(sidebarPanel);

            // Resize handle on the sidebar's right edge.
            sidebarResizer = new Thumb();
            ControlTemplate thumbTemplate = new ControlTemplate(typeof(Thumb));
            FrameworkElementFactory thumbBorder = new FrameworkElementFactory(typeof(Border));
            thumbBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            thumbTemplate.VisualTree = thumbBorder;
            sidebarResizer.Template = thumbTemplate;
            sidebarResizer.Width = 6;
            sidebarResizer.HorizontalAlignment = HorizontalAlignment.Right;
            sidebarResizer.Margin = new Thickness(0, 0, -3, 0);
            sidebarResizer.Cursor = Cursors.SizeWE;
            Panel.SetZIndex(sidebarResizer, 5);
            sidebarResizer.DragDelta += delegate(object sender, DragDeltaEventArgs e)
            {
                double width = Math.Max(180, Math.Min(480, sidebarColumn.ActualWidth + e.HorizontalChange));
                sidebarColumn.Width = new GridLength(width);
            };
            sidebarResizer.DragCompleted += delegate { settings.SidebarWidth = sidebarColumn.ActualWidth; SharedSettingsChanged(); };
            Grid.SetColumn(sidebarResizer, 0);
            contentGrid.Children.Add(sidebarResizer);

            sourcePanel = new Grid();
            sourcePanel.SetResourceReference(Panel.BackgroundProperty, "EditorBg");
            source = new TextBox();
            source.SetResourceReference(StyleProperty, "BareTextBox");
            source.AcceptsReturn = true;
            source.AcceptsTab = true;
            source.TextWrapping = TextWrapping.Wrap;
            source.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            source.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            source.BorderThickness = new Thickness(0);
            source.Padding = new Thickness(28, 22, 22, 40);
            source.FontFamily = ReaderTypography.Code;
            source.Language = ReaderTypography.Language;
            source.SpellCheck.IsEnabled = false;
            source.IsInactiveSelectionHighlightEnabled = true;
            source.AllowDrop = true;
            source.UndoLimit = 500;
            source.SetResourceReference(Control.BackgroundProperty, "EditorBg");
            source.SetResourceReference(Control.ForegroundProperty, "Text");
            source.SetResourceReference(TextBoxBase.CaretBrushProperty, "Accent");
            source.SetResourceReference(TextBoxBase.SelectionBrushProperty, "Selection");
            AutomationProperties.SetName(source, "Markdown 源码编辑区");
            AutomationProperties.SetAutomationId(source, "MarkdownSourceEditor");
            sourcePanel.Children.Add(source);
            Grid.SetColumn(sourcePanel, 1);
            contentGrid.Children.Add(sourcePanel);

            splitter = new GridSplitter();
            splitter.Width = 6;
            splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            splitter.VerticalAlignment = VerticalAlignment.Stretch;
            splitter.ResizeDirection = GridResizeDirection.Columns;
            splitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
            splitter.ShowsPreview = false;
            splitter.KeyboardIncrement = 12;
            splitter.Focusable = true;
            splitter.IsTabStop = true;
            splitter.FocusVisualStyle = null;
            splitter.AllowDrop = true;
            splitter.ToolTip = "拖动调整宽度；双击恢复均分";
            splitter.SetResourceReference(Control.BackgroundProperty, "Splitter");
            splitter.MouseEnter += delegate { UpdateSplitterVisualState(); };
            splitter.MouseLeave += delegate { UpdateSplitterVisualState(); };
            splitter.PreviewMouseLeftButtonDown += delegate { splitter.Focus(); };
            splitter.GotKeyboardFocus += delegate { splitterHintActive = true; UpdateSplitterVisualState(); RefreshStatusText(); };
            splitter.LostKeyboardFocus += delegate { CaptureSplitRatio(); settings.Save(); splitterHintActive = false; UpdateSplitterVisualState(); RefreshStatusText(); };
            splitter.KeyDown += delegate(object sender, KeyEventArgs args) { if (RealKey(args) == Key.Enter) { ResetSplitter(); args.Handled = true; } };
            splitter.KeyUp += delegate(object sender, KeyEventArgs args) { if (args.Key == Key.Left || args.Key == Key.Right) { CaptureSplitRatio(); settings.Save(); } };
            splitter.DragCompleted += delegate { CaptureSplitRatio(); settings.Save(); };
            splitter.MouseDoubleClick += delegate { ResetSplitter(); };
            AutomationProperties.SetName(splitter, "调整编辑区与预览区宽度");
            AutomationProperties.SetHelpText(splitter, "按 F6 获得焦点，使用左方向键和右方向键调整宽度，按 Enter 或双击恢复均分。");
            Grid.SetColumn(splitter, 2);
            contentGrid.Children.Add(splitter);

            previewPanel = new Grid();
            previewPanel.SetResourceReference(Panel.BackgroundProperty, "PreviewBg");
            preview = new FlowDocumentScrollViewer();
            preview.IsToolBarVisible = false;
            preview.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            preview.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            preview.MinZoom = 50;
            preview.MaxZoom = 250;
            preview.Zoom = 100;
            preview.Focusable = true;
            preview.IsTabStop = true;
            preview.IsInactiveSelectionHighlightEnabled = true;
            preview.FocusVisualStyle = null;
            preview.SetResourceReference(FlowDocumentScrollViewer.SelectionBrushProperty, "Selection");
            preview.SetResourceReference(Control.BackgroundProperty, "PreviewBg");
            AutomationProperties.SetName(preview, "Markdown 预览区");
            AutomationProperties.SetAutomationId(preview, "MarkdownPreview");
            preview.SizeChanged += delegate { UpdatePreviewPadding(); };
            previewPanel.Children.Add(preview);
            Grid.SetColumn(previewPanel, 3);
            contentGrid.Children.Add(previewPanel);

            findBar = BuildFindBar();
            Grid.SetColumn(findBar, 1);
            Grid.SetColumnSpan(findBar, 3);
            Panel.SetZIndex(findBar, 20);
            contentGrid.Children.Add(findBar);
        }

        private Border BuildSidebar()
        {
            Border panel = new Border();
            panel.SetResourceReference(Border.BackgroundProperty, "Sidebar");
            panel.SetResourceReference(Border.BorderBrushProperty, "Border");
            panel.BorderThickness = new Thickness(0, 0, 1, 0);
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border tabs = new Border();
            tabs.CornerRadius = new CornerRadius(7);
            tabs.Padding = new Thickness(2);
            tabs.Margin = new Thickness(10, 10, 10, 6);
            tabs.SetResourceReference(Border.BackgroundProperty, "Surface2");
            UniformGrid tabGrid = new UniformGrid();
            tabGrid.Columns = 2;
            outlineTab = MakeSidebarTab(Icons.Outline, "大纲", SidebarTab.Outline);
            recentTab = MakeSidebarTab(Icons.Clock, "最近", SidebarTab.Recent);
            tabGrid.Children.Add(outlineTab);
            tabGrid.Children.Add(recentTab);
            tabs.Child = tabGrid;
            grid.Children.Add(tabs);

            outlinePage = new Grid();
            outlineList = new ListBox();
            outlineList.SetResourceReference(StyleProperty, "SidebarList");
            AutomationProperties.SetName(outlineList, "文档大纲");
            outlineList.SelectionMode = SelectionMode.Single;
            outlineList.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { ActivateOutlineItem(e.OriginalSource as DependencyObject); };
            outlineList.KeyDown += delegate(object sender, KeyEventArgs e) { if (RealKey(e) == Key.Enter) { ActivateSelectedOutline(); e.Handled = true; } };
            outlinePage.Children.Add(outlineList);
            outlineEmpty = MakeSidebarEmpty("当前文档还没有标题。\n用 # 开头的行会出现在这里。");
            outlinePage.Children.Add(outlineEmpty);
            Grid.SetRow(outlinePage, 1);
            grid.Children.Add(outlinePage);

            recentPage = new Grid();
            recentPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            recentPage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            recentList = new ListBox();
            recentList.SetResourceReference(StyleProperty, "SidebarList");
            AutomationProperties.SetName(recentList, "最近打开的文件");
            recentList.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { ActivateRecentItem(e.OriginalSource as DependencyObject, (Keyboard.Modifiers & ModifierKeys.Control) != 0); };
            recentList.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (RealKey(e) == Key.Enter && recentList.SelectedItem is ListBoxItem) { OpenRecent((string)((ListBoxItem)recentList.SelectedItem).Tag, false); e.Handled = true; }
                else if (RealKey(e) == Key.Delete && recentList.SelectedItem is ListBoxItem) { RemoveRecent((string)((ListBoxItem)recentList.SelectedItem).Tag); e.Handled = true; }
            };
            recentPage.Children.Add(recentList);
            recentEmpty = MakeSidebarEmpty("还没有最近打开的文件。");
            recentPage.Children.Add(recentEmpty);
            Button clear = new Button();
            clear.SetResourceReference(StyleProperty, "LinkButton");
            clear.Content = "清空列表";
            clear.FontSize = 12;
            clear.HorizontalAlignment = HorizontalAlignment.Left;
            clear.Margin = new Thickness(16, 6, 0, 12);
            clear.Click += delegate
            {
                if (settings.RecentFiles.Count == 0) return;
                if (ThemedDialog.Show(this, "清空最近文件列表？", "只会清空列表，不会删除任何文件。", DialogIcon.Question, new string[] { "清空", "取消" }, 1, 1) != 0) return;
                settings.RecentFiles.Clear();
                settings.Save();
                ReaderApp.NotifyRecentChanged();
            };
            Grid.SetRow(clear, 1);
            recentPage.Children.Add(clear);
            Grid.SetRow(recentPage, 1);
            grid.Children.Add(recentPage);

            panel.Child = grid;
            return panel;
        }

        private RadioButton MakeSidebarTab(string icon, string label, SidebarTab tab)
        {
            RadioButton button = new RadioButton();
            button.GroupName = "SidebarTab" + GetHashCode().ToString();
            button.SetResourceReference(StyleProperty, "SegmentButton");
            StackPanel content = new StackPanel();
            content.Orientation = Orientation.Horizontal;
            content.Children.Add(Icons.Create(icon, 13));
            TextBlock text = new TextBlock();
            text.Text = label;
            text.Margin = new Thickness(6, 0, 0, 0);
            text.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(text);
            button.Content = content;
            AutomationProperties.SetName(button, label);
            button.IsChecked = settings.Sidebar == tab;
            button.Checked += delegate { settings.Sidebar = tab; ApplySidebarTab(); SharedSettingsChanged(); };
            return button;
        }

        private static TextBlock MakeSidebarEmpty(string text)
        {
            TextBlock block = new TextBlock();
            block.Text = text;
            block.FontSize = 12;
            block.LineHeight = 19;
            block.TextWrapping = TextWrapping.Wrap;
            block.Margin = new Thickness(18, 10, 14, 0);
            block.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
            block.Visibility = Visibility.Collapsed;
            return block;
        }

        private Border BuildStartPage()
        {
            Border page = new Border();
            page.SetResourceReference(Border.BackgroundProperty, "PreviewBg");
            page.Visibility = Visibility.Collapsed;
            page.AllowDrop = true;
            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            StackPanel stack = new StackPanel();
            stack.MaxWidth = 520;
            stack.Margin = new Thickness(32, 48, 32, 48);
            stack.VerticalAlignment = VerticalAlignment.Center;
            stack.HorizontalAlignment = HorizontalAlignment.Center;

            Border mark = new Border();
            mark.Width = 52; mark.Height = 52;
            mark.CornerRadius = new CornerRadius(14);
            mark.HorizontalAlignment = HorizontalAlignment.Left;
            mark.SetResourceReference(Border.BackgroundProperty, "Accent");
            TextBlock markText = new TextBlock();
            markText.Text = "墨";
            markText.FontSize = 26;
            markText.FontWeight = FontWeights.SemiBold;
            markText.HorizontalAlignment = HorizontalAlignment.Center;
            markText.VerticalAlignment = VerticalAlignment.Center;
            markText.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            mark.Child = markText;
            stack.Children.Add(mark);

            TextBlock heading = new TextBlock();
            heading.Text = "墨读 Markdown";
            heading.FontSize = 24;
            heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(0, 18, 0, 6);
            stack.Children.Add(heading);
            TextBlock subtitle = new TextBlock();
            subtitle.Text = "离线、安全、为中文排版优化的 Markdown 阅读器";
            subtitle.FontSize = 14;
            subtitle.TextWrapping = TextWrapping.Wrap;
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            stack.Children.Add(subtitle);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.Margin = new Thickness(0, 24, 0, 0);
            Button open = new Button();
            open.SetResourceReference(StyleProperty, "PrimaryButton");
            open.Content = "打开文件…";
            open.Height = 36;
            open.Click += delegate { OpenWithDialog(); };
            actions.Children.Add(open);
            Button create = new Button();
            create.SetResourceReference(StyleProperty, "TextButton");
            create.Content = "新建文档";
            create.Height = 36;
            create.Margin = new Thickness(10, 0, 0, 0);
            create.Click += delegate { NewDocument(); };
            actions.Children.Add(create);
            stack.Children.Add(actions);

            TextBlock recentHeader = new TextBlock();
            recentHeader.Text = "最近打开";
            recentHeader.FontSize = 12;
            recentHeader.FontWeight = FontWeights.SemiBold;
            recentHeader.Margin = new Thickness(0, 34, 0, 8);
            recentHeader.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
            stack.Children.Add(recentHeader);
            startRecentList = new StackPanel();
            stack.Children.Add(startRecentList);

            TextBlock hint = new TextBlock();
            hint.Text = "也可以把 .md 文件拖到窗口任意位置　·　Ctrl+O 打开　·　Ctrl+N 新建";
            hint.FontSize = 12;
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 28, 0, 0);
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
            stack.Children.Add(hint);

            scroller.Content = stack;
            page.Child = scroller;
            return page;
        }

        private UIElement BuildStatusBar()
        {
            Border shell = new Border();
            shell.BorderThickness = new Thickness(0, 1, 0, 0);
            shell.SetResourceReference(Border.BackgroundProperty, "TitleBar");
            shell.SetResourceReference(Border.BorderBrushProperty, "Border");
            DockPanel row = new DockPanel();
            row.Margin = new Thickness(14, 0, 6, 0);
            row.LastChildFill = true;

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(right, Dock.Right);
            positionText = MakeStatusText();
            right.Children.Add(positionText);
            countText = MakeStatusText();
            right.Children.Add(countText);
            encodingButton = MakeStatusButton("UTF-8 · CRLF", "文件编码与换行符");
            encodingButton.Click += delegate { ShowEncodingMenu(); };
            right.Children.Add(encodingButton);
            zoomButton = MakeStatusButton("100%", "缩放（Ctrl+滚轮 / Ctrl+= / Ctrl+-），单击恢复 100%");
            zoomButton.Click += delegate { SetZoom(100); };
            right.Children.Add(zoomButton);
            row.Children.Add(right);

            statusText = MakeStatusText();
            statusText.Margin = new Thickness(0);
            statusText.TextTrimming = TextTrimming.CharacterEllipsis;
            AutomationProperties.SetLiveSetting(statusText, AutomationLiveSetting.Polite);
            row.Children.Add(statusText);
            shell.Child = row;
            return shell;
        }

        private static TextBlock MakeStatusText()
        {
            TextBlock text = new TextBlock();
            text.FontSize = 11.5;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Margin = new Thickness(0, 0, 14, 0);
            text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            return text;
        }

        private static Button MakeStatusButton(string text, string tooltip)
        {
            Button button = new Button();
            button.SetResourceReference(StyleProperty, "GhostButton");
            button.Content = text;
            button.Height = 22;
            button.FontSize = 11.5;
            button.Padding = new Thickness(8, 0, 8, 0);
            button.SetResourceReference(Control.ForegroundProperty, "Muted");
            button.ToolTip = tooltip;
            AutomationProperties.SetName(button, tooltip);
            return button;
        }

        private Border BuildDropOverlay()
        {
            Border overlay = new Border();
            overlay.Margin = new Thickness(14);
            overlay.BorderThickness = new Thickness(2);
            overlay.CornerRadius = new CornerRadius(14);
            overlay.Visibility = Visibility.Collapsed;
            overlay.IsHitTestVisible = false;
            overlay.SetResourceReference(Border.BackgroundProperty, "DropOverlayBg");
            overlay.SetResourceReference(Border.BorderBrushProperty, "Accent");
            StackPanel content = new StackPanel();
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
            Border badge = new Border();
            badge.Width = 58; badge.Height = 58;
            badge.CornerRadius = new CornerRadius(16);
            badge.Margin = new Thickness(0, 0, 0, 18);
            badge.SetResourceReference(Border.BackgroundProperty, "Accent");
            TextBlock badgeText = new TextBlock();
            badgeText.Text = "MD";
            badgeText.FontSize = 16;
            badgeText.FontWeight = FontWeights.SemiBold;
            badgeText.HorizontalAlignment = HorizontalAlignment.Center;
            badgeText.VerticalAlignment = VerticalAlignment.Center;
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            badge.Child = badgeText;
            content.Children.Add(badge);
            TextBlock title = new TextBlock();
            title.Text = "释放以打开 Markdown";
            title.FontSize = 20;
            title.FontWeight = FontWeights.SemiBold;
            title.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(title);
            dragOverlayDetail = new TextBlock();
            dragOverlayDetail.FontSize = 12;
            dragOverlayDetail.Margin = new Thickness(0, 8, 0, 0);
            dragOverlayDetail.HorizontalAlignment = HorizontalAlignment.Center;
            dragOverlayDetail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            content.Children.Add(dragOverlayDetail);
            overlay.Child = content;
            return overlay;
        }

        // ------------------------------------------------------------------ theme & window chrome

        public void OnThemeChanged()
        {
            ApplyNativeWindowFrame();
            UpdateSplitterVisualState();
            if (preview != null && source != null && IsInitializedForRender) RenderNow(true);
        }

        private bool IsInitializedForRender { get { return lastMarkdownText != null; } }

        private void ApplyNativeWindowFrame()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero) NativeWindowVisuals.Apply(handle, ReaderApp.Palette != null && ReaderApp.Palette.IsDark, WindowState == WindowState.Maximized);
            }
            catch { }
        }

        private void ToggleMaximize()
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }

        private void WindowStateChanged(object sender, EventArgs e)
        {
            bool maximized = WindowState == WindowState.Maximized;
            maximizeButton.Content = Icons.CreateCaption(maximized ? Icons.CaptionRestore : Icons.CaptionMaximize);
            maximizeButton.ToolTip = maximized ? "还原" : "最大化";
            AutomationProperties.SetName(maximizeButton, maximized ? "还原窗口" : "最大化窗口");
            // A maximized WindowChrome window overhangs the monitor by the resize frame; pad it back in.
            Thickness frame = SystemParameters.WindowResizeBorderThickness;
            windowFrame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
            windowFrame.Margin = maximized ? new Thickness(frame.Left + 1, frame.Top + 1, frame.Right + 1, frame.Bottom + 1) : new Thickness(0);
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplyNativeWindowFrame));
        }

        private void RestoreGeometry(bool primary)
        {
            if (primary && !Double.IsNaN(settings.Left) && !Double.IsNaN(settings.Top))
            {
                Rect area = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                Rect wanted = new Rect(settings.Left, settings.Top, settings.Width, settings.Height);
                if (area.IntersectsWith(wanted))
                {
                    Left = settings.Left;
                    Top = settings.Top;
                    WindowStartupLocation = WindowStartupLocation.Manual;
                }
            }
            else if (!primary)
            {
                // Cascade additional windows so they do not cover each other exactly.
                WindowStartupLocation = WindowStartupLocation.Manual;
                Rect work = SystemParameters.WorkArea;
                int offset = 28 * (1 + CountMainWindows() % 6);
                Left = Math.Max(work.Left, Math.Min(work.Right - Width, work.Left + (work.Width - Width) / 2 + offset));
                Top = Math.Max(work.Top, Math.Min(work.Bottom - Height, work.Top + (work.Height - Height) / 2 + offset));
            }
            if (primary && settings.Maximized) WindowState = WindowState.Maximized;
        }

        private static int CountMainWindows()
        {
            int count = 0;
            foreach (MainWindow window in ReaderApp.MainWindows) count++;
            return count;
        }
    }
}
