using System.Windows;
using System.Windows.Controls;
using CodexMarkdownReader.Core;

namespace CodexMarkdownReader.UI
{
    /// <summary>Hooks used only by the built-in self-test (Tests/SelfTest.cs) to drive a window without showing it.</summary>
    public sealed partial class MainWindow
    {
        internal TextBox SourceBoxForTest { get { return source; } }
        internal Grid SourcePanelForTest { get { return sourcePanel; } }
        internal ListBox OutlineListForTest { get { return outlineList; } }
        internal bool InfoBarVisibleForTest { get { return infoBar.Visibility == Visibility.Visible; } }
        internal string DraftPathForTest { get { return draftSession.DraftPath; } }
        internal bool IsDirtyForTest { get { return IsDirty; } }

        internal bool LoadFileForTest(string path) { return LoadFile(path, 0, null, null); }
        internal bool SaveForTest() { return SaveDocument(false); }
        internal void NewDocumentForTest() { NewDocument(); }
        internal void OpenAfterConfirmForTest(string path) { if (ConfirmDiscardOrSave()) NavigateToFile(path, null, true); }
        internal void SaveDraftForTest() { SaveDraftIfDirty(); }
        internal void RestoreDraftForTest(DraftInfo draft) { RestoreDraft(draft); }
        internal void CheckDiskStateForTest() { CheckDiskState(); }
        internal void RenderForTest() { RenderNow(false); }
        internal TextFormat FormatForTest { get { return format; } }
        internal void ReopenWithEncodingForTest(int codePage) { ReopenWithEncoding(codePage); }
        internal void ReloadForTest() { ReloadFromDisk(); }
        internal void SetSaveEncodingForTest(int codePage, bool bom) { SetSaveEncoding(codePage, bom); }
        internal void SetLineEndingForTest(LineEnding ending) { SetLineEnding(ending); }
    }
}
