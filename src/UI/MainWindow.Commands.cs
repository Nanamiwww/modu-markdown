using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CodexMarkdownReader.Core;
using CodexMarkdownReader.Platform;
using CodexMarkdownReader.Rendering;
using Microsoft.Win32;
using MarkdownDocument = CodexMarkdownReader.Markdown.MarkdownDocument;
using MarkdownParser = CodexMarkdownReader.Markdown.MarkdownParser;

namespace CodexMarkdownReader.UI
{
    public sealed partial class MainWindow
    {
        private bool defaultAppPromptShown;

        // ------------------------------------------------------------------ keyboard & mouse

        private void WindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            ModifierKeys modifiers = Keyboard.Modifiers;
            bool ctrl = (modifiers & ModifierKeys.Control) != 0;
            bool shift = (modifiers & ModifierKeys.Shift) != 0;
            bool alt = (modifiers & ModifierKeys.Alt) != 0;
            Key key = RealKey(e);

            if (alt && !ctrl && (key == Key.Left || key == Key.Right))
            {
                if (key == Key.Left) NavigateBack(); else NavigateForward();
                e.Handled = true;
                return;
            }
            if (!ctrl && !alt)
            {
                switch (key)
                {
                    case Key.F6: CycleFocus(shift); e.Handled = true; return;
                    case Key.F3: StepFind(shift ? -1 : 1); e.Handled = true; return;
                    case Key.F5: ReloadFromDisk(); e.Handled = true; return;
                    case Key.Escape:
                        if (findBar.Visibility == Visibility.Visible) { CloseFindBar(); e.Handled = true; }
                        return;
                }
                return;
            }
            if (!ctrl || alt) return;
            switch (key)
            {
                case Key.N:
                    if (shift) ReaderApp.OpenInNewWindow(null, null); else NewDocument();
                    break;
                case Key.O:
                    if (shift) { settings.SidebarVisible = !settings.SidebarVisible; ApplySidebarVisibility(); SharedSettingsChanged(); }
                    else OpenWithDialog();
                    break;
                case Key.S: SaveDocument(shift); break;
                case Key.F: OpenFindBar(); break;
                case Key.P: PrintDocument(); break;
                case Key.E:
                    if (!shift) return;
                    ExportHtml();
                    break;
                case Key.W: Close(); break;
                case Key.L:
                    if (!shift) return;
                    CycleTheme();
                    break;
                case Key.D1:
                case Key.NumPad1: SetViewMode(ViewMode.Source); break;
                case Key.D2:
                case Key.NumPad2: SetViewMode(ViewMode.Split); break;
                case Key.D3:
                case Key.NumPad3: SetViewMode(ViewMode.Preview); break;
                case Key.OemPlus:
                case Key.Add: StepZoom(1); break;
                case Key.OemMinus:
                case Key.Subtract: StepZoom(-1); break;
                case Key.D0:
                case Key.NumPad0: SetZoom(100); break;
                default: return;
            }
            e.Handled = true;
        }

        /// <summary>With a Chinese IME active, WPF reports many keys as ImeProcessed; unwrap them.</summary>
        internal static Key RealKey(KeyEventArgs e)
        {
            if (e.Key == Key.System) return e.SystemKey;
            if (e.Key == Key.ImeProcessed) return e.ImeProcessedKey;
            return e.Key;
        }

        private void WindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.XButton1) { NavigateBack(); e.Handled = true; return; }
            if (e.ChangedButton == MouseButton.XButton2) { NavigateForward(); e.Handled = true; return; }
            if (previewPanel.IsMouseOver) lastFocusWasPreview = true;
            else if (sourcePanel.IsMouseOver) lastFocusWasPreview = false;
        }

        private void WindowPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            StepZoom(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        private void CycleTheme()
        {
            ThemeMode next = settings.Theme == ThemeMode.System ? ThemeMode.Light : settings.Theme == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.System;
            ReaderApp.SetTheme(next);
            SetStatus(next == ThemeMode.System ? "主题：跟随系统" : next == ThemeMode.Light ? "主题：浅色" : "主题：深色", true);
        }

        // ------------------------------------------------------------------ menus

        private static MenuItem Item(string header, string gesture, Action action)
        {
            MenuItem item = new MenuItem();
            item.Header = header;
            if (!String.IsNullOrEmpty(gesture)) item.InputGestureText = gesture;
            if (action != null) item.Click += delegate { action(); };
            return item;
        }

        private static MenuItem CheckItem(string header, string gesture, bool isChecked, Action action)
        {
            MenuItem item = Item(header, gesture, action);
            item.IsChecked = isChecked;
            return item;
        }

        private static FrameworkElement MenuIcon(string data)
        {
            FrameworkElement icon = Icons.Create(data, 16);
            icon.SetResourceReference(TextElement.ForegroundProperty, "Muted");
            return icon;
        }

        private void ShowMenu(ContextMenu menu, UIElement anchor, PlacementMode placement)
        {
            menu.PlacementTarget = anchor;
            menu.Placement = placement;
            menu.IsOpen = true;
        }

        private void ShowMainMenu(Button anchor)
        {
            ContextMenu menu = new ContextMenu();
            MenuItem newItem = Item("新建", "Ctrl+N", NewDocument);
            newItem.Icon = MenuIcon(Icons.NewDocument);
            menu.Items.Add(newItem);
            MenuItem newWindow = Item("新建窗口", "Ctrl+Shift+N", delegate { ReaderApp.OpenInNewWindow(null, null); });
            newWindow.Icon = MenuIcon(Icons.Window);
            menu.Items.Add(newWindow);
            MenuItem open = Item("打开…", "Ctrl+O", OpenWithDialog);
            open.Icon = MenuIcon(Icons.Folder);
            menu.Items.Add(open);
            MenuItem recent = Item("最近打开", null, null);
            recent.Icon = MenuIcon(Icons.Clock);
            int count = 0;
            foreach (string path in settings.RecentFiles)
            {
                if (count++ >= 10) break;
                string target = path;
                MenuItem entry = Item(SafeFileName(path).Replace("_", "__"), null, delegate { OpenRecent(target, false); });
                entry.ToolTip = path;
                entry.IsEnabled = SafeExists(path);
                recent.Items.Add(entry);
            }
            if (recent.Items.Count == 0) { MenuItem none = Item("（暂无）", null, null); none.IsEnabled = false; recent.Items.Add(none); }
            else
            {
                recent.Items.Add(new Separator());
                recent.Items.Add(Item("显示全部…", null, delegate { ShowSidebarTab(SidebarTab.Recent); }));
            }
            menu.Items.Add(recent);
            menu.Items.Add(new Separator());

            MenuItem save = Item("保存", "Ctrl+S", delegate { SaveDocument(false); });
            save.Icon = MenuIcon(Icons.Save);
            menu.Items.Add(save);
            menu.Items.Add(Item("另存为…", "Ctrl+Shift+S", delegate { SaveDocument(true); }));
            MenuItem export = Item("导出为 HTML…", "Ctrl+Shift+E", ExportHtml);
            export.Icon = MenuIcon(Icons.Export);
            menu.Items.Add(export);
            MenuItem print = Item("打印 / 导出 PDF…", "Ctrl+P", PrintDocument);
            print.Icon = MenuIcon(Icons.Print);
            menu.Items.Add(print);
            menu.Items.Add(new Separator());

            MenuItem view = Item("视图", null, null);
            view.Icon = MenuIcon(Icons.Split);
            view.Items.Add(CheckItem("源码", "Ctrl+1", viewMode == ViewMode.Source, delegate { SetViewMode(ViewMode.Source); }));
            view.Items.Add(CheckItem("分栏", "Ctrl+2", viewMode == ViewMode.Split, delegate { SetViewMode(ViewMode.Split); }));
            view.Items.Add(CheckItem("预览", "Ctrl+3", viewMode == ViewMode.Preview, delegate { SetViewMode(ViewMode.Preview); }));
            view.Items.Add(new Separator());
            view.Items.Add(CheckItem("侧栏", "Ctrl+Shift+O", settings.SidebarVisible, delegate { settings.SidebarVisible = !settings.SidebarVisible; ApplySidebarVisibility(); SharedSettingsChanged(); }));
            view.Items.Add(CheckItem("同步滚动", null, settings.SyncScroll, delegate { settings.SyncScroll = !settings.SyncScroll; settings.Save(); SetStatus(settings.SyncScroll ? "同步滚动已开启" : "同步滚动已关闭", true); }));
            view.Items.Add(new Separator());
            view.Items.Add(Item("放大", "Ctrl+=", delegate { StepZoom(1); }));
            view.Items.Add(Item("缩小", "Ctrl+-", delegate { StepZoom(-1); }));
            view.Items.Add(Item("实际大小", "Ctrl+0", delegate { SetZoom(100); }));
            menu.Items.Add(view);

            MenuItem width = Item("阅读宽度", null, null);
            width.Icon = MenuIcon(Icons.Outline);
            width.Items.Add(CheckItem("窄（720 像素）", null, settings.ReadingWidth == 720, delegate { SetReadingWidth(720); }));
            width.Items.Add(CheckItem("适中（860 像素）", null, settings.ReadingWidth == 860, delegate { SetReadingWidth(860); }));
            width.Items.Add(CheckItem("宽（1040 像素）", null, settings.ReadingWidth == 1040, delegate { SetReadingWidth(1040); }));
            width.Items.Add(CheckItem("铺满窗口", null, settings.ReadingWidth == 0, delegate { SetReadingWidth(0); }));
            menu.Items.Add(width);

            MenuItem theme = Item("主题", null, null);
            theme.Icon = MenuIcon(Icons.Sun);
            theme.Items.Add(CheckItem("跟随系统", null, settings.Theme == ThemeMode.System, delegate { ReaderApp.SetTheme(ThemeMode.System); }));
            theme.Items.Add(CheckItem("浅色", null, settings.Theme == ThemeMode.Light, delegate { ReaderApp.SetTheme(ThemeMode.Light); }));
            theme.Items.Add(CheckItem("深色", null, settings.Theme == ThemeMode.Dark, delegate { ReaderApp.SetTheme(ThemeMode.Dark); }));
            theme.Items.Add(new Separator());
            MenuItem cycle = Item("循环切换", "Ctrl+Shift+L", CycleTheme);
            theme.Items.Add(cycle);
            menu.Items.Add(theme);
            menu.Items.Add(new Separator());

            menu.Items.Add(CheckItem("启动时打开上次的文件", null, settings.OpenLastFileOnStartup, delegate { settings.OpenLastFileOnStartup = !settings.OpenLastFileOnStartup; settings.Save(); }));
            menu.Items.Add(Item("设置 Markdown 默认应用…", null, OpenDefaultAppsSettings));
            MenuItem keys = Item("快捷键一览", null, ShowShortcuts);
            keys.Icon = MenuIcon(Icons.Keyboard);
            menu.Items.Add(keys);
            menu.Items.Add(Item("关于墨读", null, ShowAbout));
            ShowMenu(menu, anchor, PlacementMode.Bottom);
        }

        private void ShowFileMenu()
        {
            ContextMenu menu = new ContextMenu();
            bool hasPath = !String.IsNullOrEmpty(currentPath);
            MenuItem copy = Item("复制完整路径", null, delegate { try { Clipboard.SetText(currentPath); SetStatus("已复制路径", true); } catch { } });
            copy.IsEnabled = hasPath;
            copy.Icon = MenuIcon(Icons.Copy);
            menu.Items.Add(copy);
            MenuItem reveal = Item("在资源管理器中显示", null, delegate { RevealInExplorer(currentPath); });
            reveal.IsEnabled = hasPath && SafeExists(currentPath);
            reveal.Icon = MenuIcon(Icons.Folder);
            menu.Items.Add(reveal);
            MenuItem reload = Item("从磁盘重新载入", "F5", ReloadFromDisk);
            reload.IsEnabled = hasPath && SafeExists(currentPath);
            reload.Icon = MenuIcon(Icons.Reload);
            menu.Items.Add(reload);
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("另存为…", "Ctrl+Shift+S", delegate { SaveDocument(true); }));
            MenuItem window = Item("在新窗口中打开", null, delegate { ReaderApp.OpenInNewWindow(currentPath, null); });
            window.IsEnabled = hasPath && SafeExists(currentPath) && !IsDirty;
            menu.Items.Add(window);
            ShowMenu(menu, fileNameButton, PlacementMode.Bottom);
        }

        private void ShowEncodingMenu()
        {
            ContextMenu menu = new ContextMenu();
            MenuItem header = Item("保存时使用的编码", null, null);
            header.IsEnabled = false;
            menu.Items.Add(header);
            menu.Items.Add(CheckItem("UTF-8", null, format.CodePage == 65001 && !format.Bom, delegate { SetSaveEncoding(65001, false); }));
            menu.Items.Add(CheckItem("UTF-8（带 BOM）", null, format.CodePage == 65001 && format.Bom, delegate { SetSaveEncoding(65001, true); }));
            menu.Items.Add(CheckItem("GBK（简体中文 ANSI）", null, format.CodePage == 936, delegate { SetSaveEncoding(936, false); }));
            menu.Items.Add(CheckItem("GB18030", null, format.CodePage == 54936, delegate { SetSaveEncoding(54936, false); }));
            menu.Items.Add(CheckItem("UTF-16 LE", null, format.CodePage == 1200, delegate { SetSaveEncoding(1200, true); }));
            menu.Items.Add(new Separator());
            MenuItem endings = Item("换行符", null, null);
            endings.Items.Add(CheckItem("CRLF（Windows）", null, format.LineEnding == LineEnding.CrLf, delegate { SetLineEnding(LineEnding.CrLf); }));
            endings.Items.Add(CheckItem("LF（Linux / macOS）", null, format.LineEnding == LineEnding.Lf, delegate { SetLineEnding(LineEnding.Lf); }));
            menu.Items.Add(endings);
            MenuItem reopen = Item("用其他编码重新打开", null, null);
            reopen.IsEnabled = !String.IsNullOrEmpty(currentPath) && SafeExists(currentPath);
            reopen.Items.Add(Item("UTF-8", null, delegate { ReopenWithEncoding(65001); }));
            reopen.Items.Add(Item("GBK", null, delegate { ReopenWithEncoding(936); }));
            reopen.Items.Add(Item("GB18030", null, delegate { ReopenWithEncoding(54936); }));
            reopen.Items.Add(Item("Big5（繁体中文）", null, delegate { ReopenWithEncoding(950); }));
            reopen.Items.Add(Item("UTF-16 LE", null, delegate { ReopenWithEncoding(1200); }));
            menu.Items.Add(reopen);
            ShowMenu(menu, encodingButton, PlacementMode.Top);
        }

        private void ShowShortcuts()
        {
            string[,] rows =
            {
                { "打开 / 保存 / 另存为", "Ctrl+O / Ctrl+S / Ctrl+Shift+S" },
                { "新建 / 新建窗口", "Ctrl+N / Ctrl+Shift+N" },
                { "查找 / 下一个 / 上一个", "Ctrl+F / F3 / Shift+F3" },
                { "源码 / 分栏 / 预览", "Ctrl+1 / Ctrl+2 / Ctrl+3" },
                { "显示或隐藏侧栏（大纲）", "Ctrl+Shift+O" },
                { "后退 / 前进", "Alt+← / Alt+→，鼠标侧键" },
                { "缩放", "Ctrl+滚轮，Ctrl+= / Ctrl+- / Ctrl+0" },
                { "切换主题", "Ctrl+Shift+L" },
                { "导出 HTML / 打印（含导出 PDF）", "Ctrl+Shift+E / Ctrl+P" },
                { "重新载入文件", "F5" },
                { "在各区域间切换焦点", "F6 / Shift+F6" },
                { "在新窗口打开 Markdown 链接", "Ctrl+单击" },
                { "关闭窗口", "Ctrl+W" }
            };
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock label = new TextBlock();
                label.Text = rows[i, 0];
                label.Margin = new Thickness(0, 3, 16, 3);
                Grid.SetRow(label, i);
                grid.Children.Add(label);
                TextBlock keys = new TextBlock();
                keys.Text = rows[i, 1];
                keys.Margin = new Thickness(0, 3, 0, 3);
                keys.FontFamily = ReaderTypography.Code;
                keys.FontSize = 12;
                keys.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                Grid.SetRow(keys, i);
                Grid.SetColumn(keys, 1);
                grid.Children.Add(keys);
            }
            ThemedDialog dialog = new ThemedDialog(this, "快捷键", "快捷键一览", null, null, DialogIcon.None, new string[] { "关闭" }, 0, 0, null, grid);
            dialog.Width = 560;
            dialog.ShowDialog();
        }

        private void ShowAbout()
        {
            string exe = "";
            try { exe = System.Reflection.Assembly.GetEntryAssembly().Location; } catch { }
            ThemedDialog.Show(this, "墨读 Markdown " + ReaderApp.Version,
                "离线、可携带的原生 Windows Markdown 阅读与编辑器。不联网、不执行文档中的脚本，远程图片不会自动加载。\n\n设置与草稿保存在：" + AppSettings.DataDirectory,
                exe, DialogIcon.Info, new string[] { "确定" }, 0, 0);
        }

        // ------------------------------------------------------------------ default app

        private void CheckDefaultAppReminder()
        {
            if (defaultAppPromptShown || settings.SuppressDefaultAppReminder) return;
            if (DefaultAppIntegration.GetMarkdownDefaultState() != DefaultAssociationState.NotDefault) return;
            defaultAppPromptShown = true;
            ThemedDialog dialog = new ThemedDialog(this, "默认应用", "将墨读设为 Markdown 默认应用？",
                "检测到 .md 当前不是由墨读打开。继续后会打开 Windows“默认应用”设置页，请选择 .md，再选择“墨读 Markdown”完成确认。\n\n最终更改只由 Windows 完成。移动便携程序或更换 U 盘盘符后，需要重新设置一次。",
                null, DialogIcon.Question, new string[] { "打开默认应用设置", "暂不" }, 0, 1, "不再提醒", null);
            dialog.ShowDialog();
            if (dialog.IsChecked)
            {
                settings.SuppressDefaultAppReminder = true;
                settings.Save();
            }
            if (dialog.Result == 0) OpenDefaultAppsSettings();
        }

        private void OpenDefaultAppsSettings()
        {
            string error;
            if (!DefaultAppIntegration.RegisterAndOpenSettings(out error))
                ThemedDialog.Inform(this, "无法打开默认应用设置", error, DialogIcon.Warning);
        }

        // ------------------------------------------------------------------ export & print

        private string DocumentTitle(MarkdownDocument markdown)
        {
            string heading = markdown == null ? null : markdown.FirstHeadingText();
            if (!String.IsNullOrWhiteSpace(heading)) return heading;
            return String.IsNullOrEmpty(currentPath) ? "未命名" : Path.GetFileNameWithoutExtension(currentPath);
        }

        private void ExportHtml()
        {
            MarkdownDocument markdown = MarkdownParser.Parse(source.Text);
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "导出为 HTML";
            dialog.Filter = "HTML 网页 (*.html)|*.html";
            dialog.DefaultExt = ".html";
            dialog.AddExtension = true;
            string baseName = String.IsNullOrEmpty(currentPath) ? SuggestFileName().Replace(".md", "") : Path.GetFileNameWithoutExtension(currentPath);
            dialog.FileName = baseName + ".html";
            string folder = String.IsNullOrEmpty(currentPath) ? settings.LastDirectory : Path.GetDirectoryName(currentPath);
            if (!String.IsNullOrEmpty(folder) && Directory.Exists(folder)) dialog.InitialDirectory = folder;
            if (dialog.ShowDialog(this) != true) return;
            string target = dialog.FileName;
            try
            {
                Cursor = Cursors.Wait;
                HtmlExporter exporter = new HtmlExporter(String.IsNullOrEmpty(currentPath) ? null : Path.GetDirectoryName(currentPath), MathImageRenderer.Render);
                string html = exporter.Export(markdown, DocumentTitle(markdown));
                File.WriteAllText(target, html, new UTF8Encoding(false));
                ShowInfoBar(exporter.RemoteImageCount + exporter.MissingImageCount > 0 ? DialogIcon.Warning : DialogIcon.Info, ExportSummary(target, exporter),
                    new InfoAction("打开", delegate { try { Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true }); } catch { } HideInfoBar(); }),
                    new InfoAction("在资源管理器中显示", delegate { RevealInExplorer(target); HideInfoBar(); }));
            }
            catch (Exception ex)
            {
                ThemedDialog.Show(this, "导出失败", ex.Message, target, DialogIcon.Warning, new string[] { "确定" }, 0, 0);
            }
            finally { Cursor = null; }
        }

        /// <summary>Says exactly what ended up in the exported file, so "send it on its own" is only promised when it is true.</summary>
        internal static string ExportSummary(string target, HtmlExporter exporter)
        {
            string name = Path.GetFileName(target);
            int remote = exporter.RemoteImageCount, missing = exporter.MissingImageCount;
            if (remote == 0 && missing == 0) return "已导出 HTML：" + name + "（本地图片和公式已内嵌，可单独发送）";
            List<string> notes = new List<string>();
            if (remote > 0) notes.Add(remote.ToString(CultureInfo.InvariantCulture) + " 张网络图片未内嵌，以链接形式保留");
            if (missing > 0) notes.Add(missing.ToString(CultureInfo.InvariantCulture) + " 张图片找不到或无法内嵌");
            return "已导出 HTML：" + name + "。注意：" + String.Join("，", notes.ToArray()) + "。";
        }

        private void PrintDocument()
        {
            MarkdownDocument markdown = MarkdownParser.Parse(source.Text);
            PrintDialog dialog = new PrintDialog();
            dialog.UserPageRangeEnabled = false;
            if (dialog.ShowDialog() != true) return;
            try
            {
                Cursor = Cursors.Wait;
                PreviewRenderOptions options = new PreviewRenderOptions();
                options.Palette = Palette.Create(false);
                options.PrintMode = true;
                options.BaseDirectory = String.IsNullOrEmpty(currentPath) ? null : Path.GetDirectoryName(currentPath);
                options.Images = ReaderApp.Images;
                options.FontSize = 13.5;
                RenderResult result = new FlowDocumentRenderer(options).Render(markdown);
                FlowDocument document = result.Document;
                double width = dialog.PrintableAreaWidth;
                double height = dialog.PrintableAreaHeight;
                document.PageWidth = width;
                document.PageHeight = height;
                document.PagePadding = new Thickness(Math.Min(72, width * 0.09), 64, Math.Min(72, width * 0.09), 64);
                document.ColumnGap = 0;
                document.ColumnWidth = width;
                document.Background = Brushes.White;
                IDocumentPaginatorSource paginator = document;
                dialog.PrintDocument(paginator.DocumentPaginator, DocumentTitle(markdown));
                SetStatus("已发送到打印机。选择“Microsoft Print to PDF”即可导出 PDF。", true);
            }
            catch (Exception ex)
            {
                ThemedDialog.Show(this, "打印失败", ex.Message, DialogIcon.Warning, new string[] { "确定" }, 0, 0);
            }
            finally { Cursor = null; }
        }

        // ------------------------------------------------------------------ drag & drop

        private int dragGeneration;

        private void WindowPreviewDragEnter(object sender, DragEventArgs e) { UpdateDragFeedback(e); }
        private void WindowPreviewDragOver(object sender, DragEventArgs e) { UpdateDragFeedback(e); }

        private void WindowPreviewDragLeave(object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            int token = ++dragGeneration;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate
            {
                if (token == dragGeneration) dragOverlay.Visibility = Visibility.Collapsed;
            }));
            e.Handled = true;
        }

        private void UpdateDragFeedback(DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string path;
            bool valid = DragDropSupport.TryGetSupportedMarkdownFile(e.Data, false, out path) && (e.AllowedEffects & DragDropEffects.Copy) == DragDropEffects.Copy;
            dragGeneration++;
            e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
            dragOverlay.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
            if (valid)
            {
                int total = CountDroppedMarkdown(e.Data);
                dragOverlayDetail.Text = total > 1 ? Path.GetFileName(path) + " 等 " + total.ToString(CultureInfo.InvariantCulture) + " 个文件（其余在新窗口打开）" : Path.GetFileName(path);
            }
            e.Handled = true;
        }

        private static List<string> DroppedMarkdownFiles(IDataObject data)
        {
            List<string> result = new List<string>();
            string[] files = null;
            try { files = data.GetData(DataFormats.FileDrop, true) as string[]; } catch { }
            if (files == null) return result;
            foreach (string file in files)
            {
                DataObject single = new DataObject(DataFormats.FileDrop, new string[] { file });
                string path;
                if (DragDropSupport.TryGetReadableMarkdownFile(single, out path)) result.Add(path);
            }
            return result;
        }

        private static int CountDroppedMarkdown(IDataObject data)
        {
            string[] files = null;
            try { files = data.GetData(DataFormats.FileDrop, true) as string[]; } catch { }
            if (files == null) return 0;
            int count = 0;
            foreach (string file in files) if (LinkPolicy.IsMarkdownPath(file)) count++;
            return count;
        }

        private void WindowPreviewDrop(object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            dragGeneration++;
            dragOverlay.Visibility = Visibility.Collapsed;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            List<string> files = DroppedMarkdownFiles(e.Data);
            if (files.Count == 0) return;
            Activate();
            if (ConfirmDiscardOrSave())
            {
                NavigateToFile(files[0], null, true);
                e.Effects = DragDropEffects.Copy;
                SetStatus("已通过拖拽打开 " + Path.GetFileName(files[0]), true);
            }
            for (int i = 1; i < files.Count; i++) ReaderApp.OpenInNewWindow(files[i], null);
        }

        private void HideDragOverlay()
        {
            dragGeneration++;
            if (dragOverlay != null) dragOverlay.Visibility = Visibility.Collapsed;
        }
    }
}
