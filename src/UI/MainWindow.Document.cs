using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMarkdownReader.Core;
using CodexMarkdownReader.Markdown;
using Microsoft.Win32;

namespace CodexMarkdownReader.UI
{
    public sealed partial class MainWindow
    {
        private readonly DraftSession draftSession;
        private string currentPath;
        private string savedText = "";
        private TextFormat format = TextFormat.Default();
        private TextFormat savedFormat = TextFormat.Default();
        private string diskHash;
        private bool fileMissing;
        private bool suppressChanges;
        private bool allowClose;
        private bool discardedOnClose;
        private FileSystemWatcher watcher;
        private DispatcherTimer externalCheckTimer;
        private bool loadedExplicitFile;

        private bool IsDirty
        {
            get { return !String.Equals(source.Text, savedText, StringComparison.Ordinal) || !format.SameStorage(savedFormat) || (fileMissing && !String.IsNullOrEmpty(currentPath)); }
        }

        public string CurrentPath { get { return currentPath; } }

        // ------------------------------------------------------------------ startup

        private void LoadInitialDocument(string startupFile, DraftInfo draft)
        {
            if (draft != null) { RestoreDraft(draft); return; }
            if (!String.IsNullOrEmpty(startupFile))
            {
                loadedExplicitFile = true;
                if (LoadFile(startupFile, 0, null, null)) return;
            }
            if (isPrimaryWindow && settings.OpenLastFileOnStartup && !String.IsNullOrEmpty(settings.LastFile) && File.Exists(settings.LastFile))
            {
                if (LoadFile(settings.LastFile, 0, null, null)) return;
            }
            ResetDocument("", null, TextFormat.Default(), "", null);
            ShowStartPage(true);
        }

        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            EnsureScrollViewers();
            UpdatePreviewPadding();
            FocusPrimaryPane();
            RefreshRecentLists();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                if (!String.IsNullOrEmpty(PendingStartupMessage))
                {
                    ThemedDialog.Inform(this, "有文件无法打开", PendingStartupMessage, DialogIcon.Warning);
                    PendingStartupMessage = null;
                }
                if (isPrimaryWindow)
                {
                    bool recovered = OfferDraftRecovery();
                    if (!recovered) CheckDefaultAppReminder();
                }
            }));
        }

        private void ShowStartPage(bool show)
        {
            startPage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) RefreshRecentLists();
        }

        // ------------------------------------------------------------------ document state

        private void ResetDocument(string text, string path, TextFormat textFormat, string onDiskText, byte[] diskBytes)
        {
            suppressChanges = true;
            try
            {
                currentPath = path;
                format = textFormat ?? TextFormat.Default();
                savedFormat = format.Clone();
                savedText = onDiskText ?? "";
                diskHash = diskBytes == null ? null : TextFileCodec.Hash(diskBytes);
                fileMissing = false;
                discardedOnClose = false;
                ReplaceTextWithoutUndo(source, text ?? "");
                source.CaretIndex = 0;
            }
            finally { suppressChanges = false; }
            source.ScrollToHome();
            SetupWatcher();
            lastMarkdownText = null;
            RenderNow(false);
            UpdateDirtyState();
            UpdateWordCount();
            UpdatePosition();
            UpdateFormatIndicator();
            HideInfoBar();
            ClearFindResults();
        }

        /// <summary>
        /// Loading another document (or reloading this one) is not an edit: the new text replaces the old one
        /// and the undo history starts empty, so Ctrl+Z can never bring back a different file's content.
        /// </summary>
        internal static void ReplaceTextWithoutUndo(TextBox box, string text)
        {
            int limit = box.UndoLimit;
            box.IsUndoEnabled = false;
            box.Text = text;
            box.IsUndoEnabled = true;
            // Setting UndoLimit empties the undo and redo stacks.
            box.UndoLimit = 0;
            box.UndoLimit = limit;
        }

        private bool LoadFile(string path, int forcedCodePage, string fragment, double? previewOffset)
        {
            string full;
            byte[] bytes;
            DecodedText decoded;
            try
            {
                full = Path.GetFullPath(path);
                bytes = TextFileCodec.ReadAllBytesShared(full);
                decoded = TextFileCodec.Decode(bytes, forcedCodePage);
            }
            catch (Exception ex)
            {
                ThemedDialog.Show(this, "无法打开文件", ex.Message, path, DialogIcon.Warning, new string[] { "确定" }, 0, 0);
                // The current document stays open. If the user had just chosen "don't save" to leave it,
                // that choice no longer applies: keep protecting the unsaved text with a draft.
                if (discardedOnClose)
                {
                    discardedOnClose = false;
                    SaveDraftIfDirty();
                }
                return false;
            }
            ResetDocument(decoded.Text, full, decoded.Format, decoded.Text, bytes);
            draftSession.Delete();
            settings.LastFile = full;
            settings.LastDirectory = Path.GetDirectoryName(full);
            settings.AddRecent(full);
            settings.Save();
            ReaderApp.NotifyRecentChanged();
            ShowStartPage(false);

            string message = "已打开 " + Path.GetFileName(full);
            if (decoded.Format.DecodedWithFallback) message += "（不是有效的 UTF-8，已按 " + decoded.Format.EncodingLabel + " 解码）";
            else if (forcedCodePage != 0) message += "（按 " + decoded.Format.EncodingLabel + " 重新解码）";
            if (decoded.Format.MixedLineEndings) message += "；文件混用了多种换行符，保存时将统一为 " + decoded.Format.LineEndingLabel;
            SetStatus(message, true);
            if (decoded.Format.LossyDecode)
                ShowInfoBar(DialogIcon.Warning, "“" + Path.GetFileName(full) + "”中有不符合 " + decoded.Format.EncodingLabel + " 编码的字节，已显示为 �。直接保存会永久替换这些字节；可以先用其他编码重新打开。",
                    new InfoAction("更改编码…", delegate { ShowEncodingMenu(); }));

            if (!String.IsNullOrEmpty(fragment)) ScrollToAnchorWhenReady(fragment);
            else if (previewOffset.HasValue) RestorePreviewOffsetWhenReady(previewOffset.Value);
            return true;
        }

        private void UpdateDirtyState()
        {
            bool dirty = IsDirty;
            string name = String.IsNullOrEmpty(currentPath) ? "未命名.md" : Path.GetFileName(currentPath);
            fileNameText.Text = name;
            dirtyDot.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
            string directory = String.IsNullOrEmpty(currentPath) ? "" : Path.GetDirectoryName(currentPath);
            fileDirectoryText.Text = directory;
            fileDirectoryText.ToolTip = String.IsNullOrEmpty(currentPath) ? null : currentPath;
            Title = (dirty ? "• " : "") + name + " — 墨读 Markdown";
            AutomationProperties.SetName(fileNameButton, "文件操作：" + name + (dirty ? "，有未保存的更改" : ""));
            if (!statusTimer.IsEnabled) RefreshDirtyStatus();
        }

        private void SourceTextChanged(object sender, TextChangedEventArgs e)
        {
            if (suppressChanges) return;
            // New typing is not part of what the user chose to discard earlier; protect it with a draft again.
            discardedOnClose = false;
            if (startPage.Visibility == Visibility.Visible) ShowStartPage(false);
            UpdateDirtyState();
            ScheduleRender();
            draftTimer.Stop(); draftTimer.Start();
            metricsTimer.Stop(); metricsTimer.Start();
            if (findBar.Visibility == Visibility.Visible) InvalidateFindResults();
        }

        // ------------------------------------------------------------------ new / open / save

        private void NewDocument()
        {
            if (!ConfirmDiscardOrSave()) return;
            ResetDocument("", null, TextFormat.Default(), "", null);
            draftSession.Delete();
            ShowStartPage(false);
            if (viewMode == ViewMode.Preview) SetViewMode(ViewMode.Split);
            source.Focus();
            SetStatus("新文档", true);
        }

        private void OpenWithDialog()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "打开 Markdown";
            dialog.Filter = "Markdown 文件 (*.md;*.markdown;*.mdown;*.mkd)|*.md;*.markdown;*.mdown;*.mkd|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
            dialog.Multiselect = true;
            string folder = !String.IsNullOrEmpty(currentPath) ? Path.GetDirectoryName(currentPath) : settings.LastDirectory;
            if (!String.IsNullOrEmpty(folder) && Directory.Exists(folder)) dialog.InitialDirectory = folder;
            if (dialog.ShowDialog(this) != true) return;
            string[] files = dialog.FileNames;
            if (files.Length == 0) return;
            if (ConfirmDiscardOrSave()) NavigateToFile(files[0], null, true);
            for (int i = 1; i < files.Length; i++) ReaderApp.OpenInNewWindow(files[i], null);
        }

        private bool SaveDocument(bool saveAs)
        {
            string target = currentPath;
            if (saveAs || String.IsNullOrEmpty(target))
            {
                SaveFileDialog dialog = new SaveFileDialog();
                dialog.Title = saveAs ? "另存为" : "保存 Markdown";
                dialog.Filter = "Markdown 文件 (*.md)|*.md|Markdown 文件 (*.markdown)|*.markdown|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                dialog.DefaultExt = ".md";
                dialog.AddExtension = true;
                dialog.FileName = String.IsNullOrEmpty(currentPath) ? SuggestFileName() : Path.GetFileName(currentPath);
                string folder = !String.IsNullOrEmpty(currentPath) ? Path.GetDirectoryName(currentPath) : settings.LastDirectory;
                if (!String.IsNullOrEmpty(folder) && Directory.Exists(folder)) dialog.InitialDirectory = folder;
                if (dialog.ShowDialog(this) != true) return false;
                target = dialog.FileName;
            }
            // Prepare the bytes first: the question about switching to UTF-8 must not sit between
            // the check for changes made by other programs and the actual write.
            byte[] bytes;
            bool switchToUtf8 = false;
            try { bytes = TextFileCodec.Encode(source.Text, format); }
            catch (UnencodableTextException ex)
            {
                int choice = ThemedDialog.Show(this, "无法用当前编码保存", ex.Message + "\n改用 UTF-8 保存可以保留所有字符。",
                    DialogIcon.Warning, new string[] { "改用 UTF-8 保存", "取消" }, 0, 1);
                if (choice != 0) return false;
                TextFormat utf8 = format.Clone();
                utf8.CodePage = 65001;
                utf8.Bom = false;
                bytes = TextFileCodec.Encode(source.Text, utf8);
                switchToUtf8 = true;
            }

            if (!saveAs && !String.IsNullOrEmpty(currentPath))
            {
                int verdict = ConfirmOverwrite(target);
                if (verdict == 1) return SaveDocument(true);
                if (verdict != 0) return false;
            }

            string full;
            try
            {
                full = Path.GetFullPath(target);
                TextFileCodec.WriteAtomic(full, bytes);
                if (switchToUtf8) { format.CodePage = 65001; format.Bom = false; }
            }
            catch (Exception ex)
            {
                // Nothing was written: keep the previous baseline so the next save still detects
                // changes made by other programs in the meantime.
                ThemedDialog.Show(this, "无法保存文件", ex.Message, target, DialogIcon.Warning, new string[] { "确定" }, 0, 0);
                return false;
            }

            diskHash = TextFileCodec.Hash(bytes);
            string previousDirectory = String.IsNullOrEmpty(currentPath) ? null : Path.GetDirectoryName(currentPath);
            bool pathChanged = !String.Equals(full, currentPath, StringComparison.OrdinalIgnoreCase);
            currentPath = full;
            savedText = source.Text;
            // The file is now stored in this encoding. Detect it again next time when that is reliable;
            // otherwise keep reading it with the encoding the user picked (to open it or to save it).
            bool encodingPicked = format.ReadCodePage != 0 || savedFormat == null || format.CodePage != savedFormat.CodePage;
            format.ReadCodePage = format.IsSelfIdentifying || !encodingPicked ? 0 : format.CodePage;
            format.DecodedWithFallback = false;
            format.MixedLineEndings = false;
            format.LossyDecode = false;
            savedFormat = format.Clone();
            fileMissing = false;
            settings.LastFile = full;
            settings.LastDirectory = Path.GetDirectoryName(full);
            settings.AddRecent(full);
            settings.Save();
            ReaderApp.NotifyRecentChanged();
            draftSession.Delete();
            ReleaseRecoveredDraft(true);
            HideInfoBar();
            if (pathChanged) SetupWatcher();
            UpdateDirtyState();
            UpdateFormatIndicator();
            SetStatus("已保存 " + Path.GetFileName(full), true);
            if (pathChanged && !String.Equals(previousDirectory, Path.GetDirectoryName(full), StringComparison.OrdinalIgnoreCase)) RenderNow(true);
            return true;
        }

        /// <summary>
        /// Called right before writing over the current file. Makes sure it is still the version this window read or
        /// wrote (or the one the user has just agreed to overwrite) and asks about undecodable bytes. Every answer is
        /// followed by a fresh check, so a change another program makes while a question is open is never overwritten
        /// without asking. Returns 0 to write, 1 for "save as", anything else to cancel.
        /// </summary>
        private int ConfirmOverwrite(string target)
        {
            string expected = diskHash;
            bool lossyConfirmed = false;
            while (File.Exists(target))
            {
                string name = Path.GetFileName(target);
                string current;
                try { current = TextFileCodec.Hash(TextFileCodec.ReadAllBytesShared(target)); }
                catch (Exception)
                {
                    // An unknown version is not permission to overwrite.
                    int unreadable = ThemedDialog.Show(this, "无法读取磁盘上的文件", "保存前需要确认“" + name + "”有没有被其他程序修改，但现在无法读取它（可能正被其他程序占用）。直接保存可能覆盖其他程序的修改。",
                        DialogIcon.Warning, new string[] { "仍然保存", "另存为…", "取消" }, 2, 2);
                    if (unreadable != 0 || lossyConfirmed) return unreadable;
                    return ConfirmLossyOverwrite();
                }
                if (current != expected)
                {
                    int choice = expected == null
                        ? ThemedDialog.Show(this, "无法确认磁盘上的文件是否被修改过", "无法确认“" + name + "”是不是这些内容编辑时依据的版本（例如内容来自旧版本留下的草稿）。直接保存会覆盖磁盘上现在的内容。",
                            DialogIcon.Warning, new string[] { "覆盖保存", "另存为…", "取消" }, 1, 2)
                        : ThemedDialog.Show(this, "磁盘上的文件已被修改", "“" + name + "”在你打开之后被其他程序修改过。直接保存会覆盖那些修改。",
                            DialogIcon.Warning, new string[] { "覆盖保存", "另存为…", "取消" }, 1, 2);
                    if (choice != 0) return choice;
                    expected = current;
                    continue;
                }
                if (format.LossyDecode && !lossyConfirmed)
                {
                    int choice = ConfirmLossyOverwrite();
                    if (choice != 0) return choice;
                    lossyConfirmed = true;
                    continue;
                }
                return 0;
            }
            return 0;
        }

        /// <summary>0 when there are no undecodable bytes or the user agrees to replace them; otherwise 1 (save as) or 2 (cancel).</summary>
        private int ConfirmLossyOverwrite()
        {
            if (!format.LossyDecode) return 0;
            return ThemedDialog.Show(this, "文件中有无法解码的字节", "打开时有部分字节不符合 " + format.EncodingLabel + " 编码，已显示为 �。保存到原文件会把这些字节永久替换掉。\n可以先用“用其他编码重新打开”找到正确的编码，或者另存为新文件。",
                DialogIcon.Warning, new string[] { "仍然保存", "另存为…", "取消" }, 2, 2);
        }

        private string SuggestFileName()
        {
            string heading = lastMarkdown == null ? null : lastMarkdown.FirstHeadingText();
            if (String.IsNullOrWhiteSpace(heading)) return "未命名.md";
            char[] invalid = Path.GetInvalidFileNameChars();
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            foreach (char c in heading.Trim()) builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            string name = builder.ToString();
            if (name.Length > 60) name = name.Substring(0, 60);
            return name + ".md";
        }

        /// <summary>Returns false when the user cancels. Choosing "don't save" also removes this window's draft.</summary>
        private bool ConfirmDiscardOrSave()
        {
            if (!IsDirty) return true;
            string name = String.IsNullOrEmpty(currentPath) ? "未命名文档" : Path.GetFileName(currentPath);
            int choice = ThemedDialog.Show(this, "是否保存对“" + name + "”的更改？", "如果不保存，这些更改会被丢弃。", DialogIcon.Question,
                new string[] { "保存", "不保存", "取消" }, 0, 2);
            if (choice == 0) return SaveDocument(false);
            if (choice == 1)
            {
                draftSession.Delete();
                ReleaseRecoveredDraft(true);
                discardedOnClose = true;
                return true;
            }
            return false;
        }

        private void ReloadFromDisk()
        {
            if (String.IsNullOrEmpty(currentPath)) return;
            if (!File.Exists(currentPath)) { ThemedDialog.Inform(this, "文件不存在", currentPath, DialogIcon.Warning); return; }
            if (IsDirty && ThemedDialog.Show(this, "放弃未保存的更改并重新载入？", "将从磁盘重新读取“" + Path.GetFileName(currentPath) + "”。", DialogIcon.Warning,
                new string[] { "重新载入", "取消" }, 1, 1) != 0) return;
            double offset = previewScroll != null ? previewScroll.VerticalOffset : 0;
            LoadFile(currentPath, format.ReadCodePage, null, offset);
        }

        private void ReopenWithEncoding(int codePage)
        {
            if (String.IsNullOrEmpty(currentPath) || !File.Exists(currentPath)) return;
            if (IsDirty && ThemedDialog.Show(this, "放弃未保存的更改？", "用其他编码重新打开会丢弃当前未保存的更改。", DialogIcon.Warning,
                new string[] { "重新打开", "取消" }, 1, 1) != 0) return;
            double offset = previewScroll != null ? previewScroll.VerticalOffset : 0;
            LoadFile(currentPath, codePage, null, offset);
        }

        private void SetSaveEncoding(int codePage, bool bom)
        {
            format.CodePage = codePage;
            format.Bom = bom;
            UpdateFormatIndicator();
            UpdateDirtyState();
            // A changed encoding is an unsaved change like any edit: protect it with a draft.
            SaveDraftIfDirty();
            SetStatus("保存时将使用 " + format.EncodingLabel + " 编码", true);
        }

        private void SetLineEnding(LineEnding ending)
        {
            format.LineEnding = ending;
            UpdateFormatIndicator();
            UpdateDirtyState();
            SaveDraftIfDirty();
            SetStatus("保存时将使用 " + format.LineEndingLabel + " 换行符", true);
        }

        // ------------------------------------------------------------------ drafts

        /// <summary>
        /// Writes this window's draft. It records the file version the edits are based on (and the read encoding and
        /// lossy-decode flag in the format), so a recovered draft is checked against the right version when saved.
        /// </summary>
        private bool WriteDraft()
        {
            return draftSession.Save(currentPath, source.Text, format, String.IsNullOrEmpty(currentPath) ? null : diskHash);
        }

        private void SaveDraftIfDirty()
        {
            if (IsDirty && !discardedOnClose)
            {
                if (WriteDraft()) ReleaseRecoveredDraft(true);
                else SetStatus("无法写入自动草稿，请尽快保存文件", false);
            }
            else draftSession.Delete();
        }

        /// <summary>
        /// A crash draft this window restored but could not yet replace with its own draft. It stays on disk
        /// (and claimed) until the content is safe elsewhere: in this window's draft, or saved to a file.
        /// </summary>
        private DraftInfo recoveredDraft;

        /// <param name="contentSafe">True: the content is saved elsewhere (or deliberately discarded), so delete the old draft.
        /// False: keep the old draft on disk for the next start.</param>
        private void ReleaseRecoveredDraft(bool contentSafe)
        {
            if (recoveredDraft == null) return;
            DraftInfo draft = recoveredDraft;
            recoveredDraft = null;
            if (contentSafe)
            {
                draft.Discard();
                if (draft.Id == "legacy") TryDeleteLegacyDraft();
            }
            else draft.Dispose();
        }

        public void EmergencySaveDraft()
        {
            try { if (IsDirty) WriteDraft(); }
            catch { }
        }

        /// <summary>Shows drafts left by windows that closed unexpectedly. Returns true when a dialog was shown.</summary>
        private bool OfferDraftRecovery()
        {
            List<DraftInfo> orphans = DraftSession.ClaimOrphans(draftSession.Id);
            if (orphans.Count == 0) return false;

            StackPanel list = new StackPanel();
            List<CheckBox> checks = new List<CheckBox>();
            foreach (DraftInfo draft in orphans)
            {
                CheckBox check = new CheckBox();
                check.IsChecked = true;
                check.Margin = new Thickness(0, 0, 0, 10);
                check.Tag = draft;
                StackPanel text = new StackPanel();
                TextBlock name = new TextBlock();
                name.Text = draft.DisplayName + "　" + draft.SavedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                name.FontWeight = FontWeights.SemiBold;
                text.Children.Add(name);
                string detail = String.IsNullOrEmpty(draft.DocumentPath) ? "尚未保存过的新文档" : draft.DocumentPath;
                string note = RecoveryNote(draft);
                if (note != null) detail += "\n" + note;
                string preview = FirstLine(draft.Text);
                if (preview.Length > 0) detail += "\n“" + preview + "”";
                TextBlock detailText = new TextBlock();
                detailText.Text = detail;
                detailText.FontSize = 12;
                detailText.TextWrapping = TextWrapping.Wrap;
                detailText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                text.Children.Add(detailText);
                check.Content = text;
                checks.Add(check);
                list.Children.Add(check);
            }
            ScrollViewer scroller = new ScrollViewer();
            scroller.MaxHeight = 280;
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.Content = list;

            ThemedDialog dialog = new ThemedDialog(this, "恢复草稿", "发现未保存的内容",
                "上次有窗口在关闭前没有保存（例如程序或系统异常退出）。勾选要恢复的草稿：", null, DialogIcon.Info,
                new string[] { "恢复所选", "丢弃所选", "稍后决定" }, 0, 2, null, scroller);
            dialog.ShowDialog();
            int choice = dialog.Result;

            bool first = true;
            foreach (CheckBox check in checks)
            {
                DraftInfo draft = (DraftInfo)check.Tag;
                bool selected = check.IsChecked == true;
                if (choice == 0 && selected)
                {
                    bool useThisWindow = first && !loadedExplicitFile && !IsDirty;
                    first = false;
                    if (useThisWindow) RestoreDraft(draft);
                    else ReaderApp.OpenInNewWindow(null, draft);
                }
                else if (choice == 1 && selected)
                {
                    draft.Discard();
                    if (draft.Id == "legacy") TryDeleteLegacyDraft();
                }
                else draft.Dispose();
            }
            return true;
        }

        /// <summary>
        /// What the recovery list says about a draft's original file. With the recorded base version this is exact,
        /// including changes made before the last draft was written; older drafts fall back to the file time.
        /// </summary>
        internal static string RecoveryNote(DraftInfo draft)
        {
            if (draft == null || String.IsNullOrEmpty(draft.DocumentPath)) return null;
            try
            {
                if (!File.Exists(draft.DocumentPath)) return "注意：原文件已不存在";
                if (!String.IsNullOrEmpty(draft.BaseHash))
                {
                    string current = TextFileCodec.Hash(TextFileCodec.ReadAllBytesShared(draft.DocumentPath));
                    return current == draft.BaseHash ? null : "注意：原文件在编辑期间被其他程序修改过，恢复后保存时会先确认";
                }
                if (File.GetLastWriteTimeUtc(draft.DocumentPath) > draft.SavedUtc) return "注意：原文件在草稿之后又被修改过";
            }
            catch { }
            return null;
        }

        private static void TryDeleteLegacyDraft()
        {
            try { File.Delete(Path.Combine(AppSettings.DataDirectory, "draft.md~")); } catch { }
        }

        private static string FirstLine(string text)
        {
            if (String.IsNullOrEmpty(text)) return "";
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                return trimmed.Length > 60 ? trimmed.Substring(0, 60) + "…" : trimmed;
            }
            return "";
        }

        private void RestoreDraft(DraftInfo draft)
        {
            string path = String.IsNullOrEmpty(draft.DocumentPath) ? null : draft.DocumentPath;
            string disk = "";
            byte[] bytes = null;
            TextFormat textFormat = (draft.Format ?? TextFormat.Default()).Clone();
            TextFormat diskFormat = null;
            bool missing = false;
            if (path != null)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        bytes = TextFileCodec.ReadAllBytesShared(path);
                        // Read the file the way the draft's window read it: an encoding the user picked stays in effect.
                        DecodedText decoded = TextFileCodec.Decode(bytes, textFormat.ReadCodePage);
                        disk = decoded.Text;
                        diskFormat = decoded.Format;
                        // Saving would replace bytes that cannot be decoded: ask first, exactly as after opening the file.
                        if (decoded.Format.LossyDecode) textFormat.LossyDecode = true;
                    }
                    else missing = true;
                }
                catch { missing = !File.Exists(path); }
            }
            ResetDocument(draft.Text, path, textFormat, disk, bytes);
            // Compare with how the file is stored on disk, so a draft that only changed the encoding or the line
            // endings still counts as unsaved.
            if (diskFormat != null) savedFormat = diskFormat.Clone();
            // The edits are based on the version recorded in the draft, not on whatever is on disk now; a change made
            // by another program in between is then caught when saving. Unknown for drafts from older versions:
            // saving over the file asks first.
            if (path != null) diskHash = draft.BaseHash;
            fileMissing = missing;
            ShowStartPage(false);
            // Take ownership: write this window's draft first, and remove the old one only once that worked.
            ReleaseRecoveredDraft(false);
            recoveredDraft = draft;
            bool persisted = WriteDraft();
            if (persisted) ReleaseRecoveredDraft(true);
            UpdateDirtyState();
            string when = draft.SavedUtc.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (persisted)
                ShowInfoBar(DialogIcon.Info, "已恢复“" + draft.DisplayName + "”的草稿（" + when + "），尚未保存到文件。",
                    new InfoAction("保存", delegate { SaveDocument(false); }),
                    new InfoAction("放弃草稿", delegate { DiscardRecoveredDraft(); }));
            else
                ShowInfoBar(DialogIcon.Warning, "已恢复“" + draft.DisplayName + "”的草稿（" + when + "），但无法写入新的草稿文件，原草稿已保留。请尽快保存到文件。",
                    new InfoAction("保存", delegate { SaveDocument(false); }),
                    new InfoAction("放弃草稿", delegate { DiscardRecoveredDraft(); }));
        }

        private void DiscardRecoveredDraft()
        {
            if (ThemedDialog.Show(this, "放弃恢复的草稿？", "草稿内容会被丢弃，无法撤销。", DialogIcon.Warning, new string[] { "放弃", "取消" }, 1, 1) != 0) return;
            draftSession.Delete();
            ReleaseRecoveredDraft(true);
            if (!String.IsNullOrEmpty(currentPath) && File.Exists(currentPath)) LoadFile(currentPath, format.ReadCodePage, null, null);
            else { ResetDocument("", null, TextFormat.Default(), "", null); ShowStartPage(true); }
        }

        // ------------------------------------------------------------------ external changes

        private void SetupWatcher()
        {
            if (watcher != null)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                watcher = null;
            }
            if (String.IsNullOrEmpty(currentPath)) return;
            try
            {
                string directory = Path.GetDirectoryName(currentPath);
                if (!Directory.Exists(directory)) return;
                watcher = new FileSystemWatcher(directory, Path.GetFileName(currentPath));
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime;
                FileSystemEventHandler changed = delegate { Dispatcher.BeginInvoke(new Action(ScheduleExternalCheck)); };
                watcher.Changed += changed;
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Renamed += delegate { Dispatcher.BeginInvoke(new Action(ScheduleExternalCheck)); };
                watcher.EnableRaisingEvents = true;
            }
            catch { watcher = null; }
        }

        private void ScheduleExternalCheck()
        {
            if (externalCheckTimer == null)
            {
                externalCheckTimer = new DispatcherTimer(DispatcherPriority.Background);
                externalCheckTimer.Interval = TimeSpan.FromMilliseconds(400);
                externalCheckTimer.Tick += delegate { externalCheckTimer.Stop(); CheckDiskState(); };
            }
            externalCheckTimer.Stop();
            externalCheckTimer.Start();
        }

        private void CheckExternalChangeOnActivate()
        {
            if (!String.IsNullOrEmpty(currentPath)) ScheduleExternalCheck();
        }

        private int diskReadRetries;

        /// <summary>
        /// Decodes the current file again after it changed on disk. An encoding the user picked stays in effect;
        /// otherwise the encoding is detected again (a new BOM or valid UTF-8 is honoured). Plain ASCII reads the same
        /// in every ANSI code page, so a file stored in one keeps it instead of silently turning into UTF-8.
        /// </summary>
        private DecodedText DecodeAgain(byte[] bytes)
        {
            if (format.ReadCodePage != 0) return TextFileCodec.Decode(bytes, format.ReadCodePage);
            DecodedText decoded = TextFileCodec.Decode(bytes, 0);
            if (!format.IsSelfIdentifying && decoded.Format.CodePage == 65001 && !decoded.Format.Bom && IsPlainAscii(bytes))
            {
                decoded.Format.CodePage = format.CodePage;
                decoded.Format.Bom = format.Bom;
                decoded.Format.DecodedWithFallback = format.DecodedWithFallback;
            }
            return decoded;
        }

        private static bool IsPlainAscii(byte[] bytes)
        {
            foreach (byte b in bytes) if (b >= 0x80) return false;
            return true;
        }

        private void CheckDiskState()
        {
            if (String.IsNullOrEmpty(currentPath)) return;
            if (!File.Exists(currentPath))
            {
                if (!fileMissing)
                {
                    fileMissing = true;
                    UpdateDirtyState();
                    // The window now holds the only copy of the text: keep a draft right away, not only after the next edit.
                    SaveDraftIfDirty();
                    ShowInfoBar(DialogIcon.Warning, "“" + Path.GetFileName(currentPath) + "”已被删除、移动或重命名。内容仍保留在这里，保存时会在原位置重新创建。",
                        new InfoAction("另存为…", delegate { SaveDocument(true); }));
                }
                return;
            }
            byte[] bytes;
            try { bytes = TextFileCodec.ReadAllBytesShared(currentPath); diskReadRetries = 0; }
            catch
            {
                // The writer may still hold the file; try again shortly.
                if (diskReadRetries++ < 5) ScheduleExternalCheck();
                return;
            }
            string hash = TextFileCodec.Hash(bytes);
            if (fileMissing)
            {
                fileMissing = false;
                HideInfoBar();
            }
            if (hash == diskHash) { UpdateDirtyState(); return; }
            // Unknown base version (a draft left by an older version): whether the file changed cannot be told, so
            // nothing is claimed here; saving over the file asks first.
            if (diskHash == null && IsDirty) { UpdateDirtyState(); return; }

            if (!IsDirty || String.Equals(source.Text, savedText, StringComparison.Ordinal) && format.SameStorage(savedFormat))
            {
                DecodedText decoded = DecodeAgain(bytes);
                double offset = previewScroll != null ? previewScroll.VerticalOffset : 0;
                int caret = source.CaretIndex;
                double sourceOffset = sourceScroll != null ? sourceScroll.VerticalOffset : 0;
                string previousEncoding = format.EncodingLabel;
                ResetDocument(decoded.Text, currentPath, decoded.Format, decoded.Text, bytes);
                source.CaretIndex = Math.Min(caret, source.Text.Length);
                RestorePreviewOffsetWhenReady(offset);
                if (sourceScroll != null) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { sourceScroll.ScrollToVerticalOffset(sourceOffset); }));
                string refreshed = "文件已在外部更新，已自动刷新 · " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                if (decoded.Format.EncodingLabel != previousEncoding) refreshed += "（编码变为 " + decoded.Format.EncodingLabel + "）";
                SetStatus(refreshed, true);
                // Same warning as when the file is opened.
                if (decoded.Format.LossyDecode)
                    ShowInfoBar(DialogIcon.Warning, "“" + Path.GetFileName(currentPath) + "”中有不符合 " + decoded.Format.EncodingLabel + " 编码的字节，已显示为 �。直接保存会永久替换这些字节；可以先用其他编码重新打开。",
                        new InfoAction("更改编码…", delegate { ShowEncodingMenu(); }));
                return;
            }

            string acknowledgedHash = hash;
            ShowInfoBar(DialogIcon.Warning, "“" + Path.GetFileName(currentPath) + "”已被其他程序修改，而这里还有未保存的更改。",
                new InfoAction("载入磁盘版本", delegate
                {
                    if (ThemedDialog.Show(this, "丢弃这里的更改？", "将载入磁盘上的新版本，当前未保存的更改会丢失。", DialogIcon.Warning, new string[] { "载入磁盘版本", "取消" }, 1, 1) != 0) return;
                    double offset = previewScroll != null ? previewScroll.VerticalOffset : 0;
                    LoadFile(currentPath, format.ReadCodePage, null, offset);
                }),
                new InfoAction("保留我的版本", delegate
                {
                    diskHash = acknowledgedHash;
                    HideInfoBar();
                    SetStatus("已保留当前内容；保存时会覆盖磁盘上的版本", true);
                }));
        }

        // ------------------------------------------------------------------ recent files

        public void RefreshRecentLists()
        {
            if (recentList == null) return;
            recentList.Items.Clear();
            foreach (string path in settings.RecentFiles)
            {
                ListBoxItem item = new ListBoxItem();
                item.Tag = path;
                bool exists = SafeExists(path);
                StackPanel content = new StackPanel();
                TextBlock name = new TextBlock();
                name.Text = SafeFileName(path);
                name.FontSize = 12.5;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                if (String.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase)) name.FontWeight = FontWeights.SemiBold;
                content.Children.Add(name);
                TextBlock folder = new TextBlock();
                folder.Text = exists ? SafeDirectory(path) : "文件已不存在";
                folder.FontSize = 11;
                folder.TextTrimming = TextTrimming.CharacterEllipsis;
                folder.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
                content.Children.Add(folder);
                item.Content = content;
                item.ToolTip = path + "\n单击打开；Ctrl+单击在新窗口打开";
                if (!exists) item.Opacity = 0.55;
                AutomationProperties.SetName(item, SafeFileName(path));
                item.ContextMenu = BuildRecentItemMenu(path, exists);
                recentList.Items.Add(item);
            }
            recentEmpty.Visibility = settings.RecentFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            startRecentList.Children.Clear();
            int shown = 0;
            foreach (string path in settings.RecentFiles)
            {
                if (shown >= 6) break;
                if (!SafeExists(path)) continue;
                string target = path;
                Button button = new Button();
                button.SetResourceReference(StyleProperty, "GhostButton");
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.Height = Double.NaN;
                button.Padding = new Thickness(10, 7, 10, 7);
                StackPanel content = new StackPanel();
                content.HorizontalAlignment = HorizontalAlignment.Left;
                TextBlock name = new TextBlock();
                name.Text = SafeFileName(path);
                name.FontSize = 13;
                content.Children.Add(name);
                TextBlock folder = new TextBlock();
                folder.Text = SafeDirectory(path);
                folder.FontSize = 11.5;
                folder.TextTrimming = TextTrimming.CharacterEllipsis;
                folder.MaxWidth = 460;
                folder.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
                content.Children.Add(folder);
                button.Content = content;
                button.ToolTip = path;
                button.Click += delegate { OpenRecent(target, (Keyboard.Modifiers & ModifierKeys.Control) != 0); };
                startRecentList.Children.Add(button);
                shown++;
            }
            if (shown == 0)
            {
                TextBlock none = new TextBlock();
                none.Text = "暂无";
                none.FontSize = 12.5;
                none.Margin = new Thickness(10, 2, 0, 0);
                none.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
                startRecentList.Children.Add(none);
            }
        }

        private ContextMenu BuildRecentItemMenu(string path, bool exists)
        {
            ContextMenu menu = new ContextMenu();
            MenuItem open = new MenuItem();
            open.Header = "打开";
            open.IsEnabled = exists;
            open.Click += delegate { OpenRecent(path, false); };
            menu.Items.Add(open);
            MenuItem newWindow = new MenuItem();
            newWindow.Header = "在新窗口打开";
            newWindow.IsEnabled = exists;
            newWindow.Click += delegate { OpenRecent(path, true); };
            menu.Items.Add(newWindow);
            MenuItem reveal = new MenuItem();
            reveal.Header = "在资源管理器中显示";
            reveal.IsEnabled = exists;
            reveal.Click += delegate { RevealInExplorer(path); };
            menu.Items.Add(reveal);
            menu.Items.Add(new Separator());
            MenuItem remove = new MenuItem();
            remove.Header = "从列表中移除";
            remove.Click += delegate { RemoveRecent(path); };
            menu.Items.Add(remove);
            return menu;
        }

        private void ActivateRecentItem(DependencyObject origin, bool newWindow)
        {
            ListBoxItem item = FindAncestor<ListBoxItem>(origin);
            if (item == null || !(item.Tag is string)) return;
            OpenRecent((string)item.Tag, newWindow);
        }

        private void OpenRecent(string path, bool newWindow)
        {
            if (!SafeExists(path))
            {
                if (ThemedDialog.Show(this, "文件已不存在", "它可能已被移动、重命名或删除。", path, DialogIcon.Warning, new string[] { "从列表中移除", "取消" }, 0, 1) == 0) RemoveRecent(path);
                return;
            }
            if (newWindow) { ReaderApp.OpenInNewWindow(path, null); return; }
            if (String.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase)) { ShowStartPage(false); return; }
            if (!ConfirmDiscardOrSave()) return;
            NavigateToFile(path, null, true);
        }

        private void RemoveRecent(string path)
        {
            settings.RemoveRecent(path);
            settings.Save();
            ReaderApp.NotifyRecentChanged();
        }

        private static bool SafeExists(string path)
        {
            try { return File.Exists(path); } catch { return false; }
        }

        private static string SafeFileName(string path)
        {
            try { return Path.GetFileName(path); } catch { return path; }
        }

        private static string SafeDirectory(string path)
        {
            try { return Path.GetDirectoryName(path); } catch { return ""; }
        }

        internal static void RevealInExplorer(string path)
        {
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch { }
        }

        internal static T FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            DependencyObject current = start;
            while (current != null)
            {
                T match = current as T;
                if (match != null) return match;
                current = current is Visual || current is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
            }
            return null;
        }

        // ------------------------------------------------------------------ closing

        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (allowClose) return;
            if (!ConfirmDiscardOrSave()) { e.Cancel = true; discardedOnClose = false; return; }
            allowClose = true;
        }

        internal void PrepareForSessionEnd()
        {
            EmergencySaveDraft();
            allowClose = true;
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            if (!constructed) { ReleaseRecoveredDraft(false); if (draftSession != null) draftSession.Dispose(); return; }
            CaptureSplitRatio();
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            if (!bounds.IsEmpty && !Double.IsNaN(bounds.Left) && !Double.IsInfinity(bounds.Left) && bounds.Width >= MinWidth && bounds.Height >= MinHeight)
            {
                settings.Width = bounds.Width;
                settings.Height = bounds.Height;
                settings.Left = bounds.Left;
                settings.Top = bounds.Top;
            }
            settings.Maximized = WindowState == WindowState.Maximized;
            if (!String.IsNullOrEmpty(currentPath)) settings.LastFile = currentPath;
            settings.Save();
            if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); watcher = null; }
            renderTimer.Stop(); draftTimer.Stop(); metricsTimer.Stop(); scrollSpyTimer.Stop(); statusTimer.Stop();
            if (externalCheckTimer != null) externalCheckTimer.Stop();
            if (IsDirty && !discardedOnClose)
            {
                // A restored crash draft is only deleted once this window's own draft is on disk.
                ReleaseRecoveredDraft(WriteDraft());
            }
            else
            {
                draftSession.Delete();
                ReleaseRecoveredDraft(true);
            }
            draftSession.Dispose();
        }
    }
}
