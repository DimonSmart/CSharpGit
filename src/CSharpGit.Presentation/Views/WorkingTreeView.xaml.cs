using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation.Views;

// Owns working-tree tree selection, diff previews and view lifetime.
// File menus still forward to window-scoped workflows.
public sealed partial class WorkingTreeView : UserControl
{
    public WorkingTreeView() => InitializeComponent();

    internal Grid WorkingTreeHeaderControl => WorkingTreeHeader;
    internal TextBlock UnstagedHeaderControl => UnstagedHeader;
    internal TreeView UnstagedChangesTreeControl => UnstagedChangesTree;
    internal GridSplitter WorkingTreeStagingSplitterControl => WorkingTreeStagingSplitter;
    internal TextBlock StagedHeaderControl => StagedHeader;
    internal TreeView StagedChangesTreeControl => StagedChangesTree;
    internal TextBlock WorkingTreeDiffHeaderControl => WorkingTreeDiffHeader;
    internal TextBlock WorkingTreeDiffKindTextControl => WorkingTreeDiffKindText;
    internal Button WorkingTreeOpenOriginalButtonControl => WorkingTreeOpenOriginalButton;
    internal Button WorkingTreeOpenChangedButtonControl => WorkingTreeOpenChangedButton;
    internal Button WorkingTreeExternalDiffButtonControl => WorkingTreeExternalDiffButton;
    internal Button WorkingTreeRevealButtonControl => WorkingTreeRevealButton;
    internal Border WorkingTreeTextDiffHeaderControl => WorkingTreeTextDiffHeader;
    internal StackPanel WorkingTreeLargeDiffInfoControl => WorkingTreeLargeDiffInfo;
    internal TextBlock WorkingTreeLargeDiffMessageControl => WorkingTreeLargeDiffMessage;
    internal Button WorkingTreeLargeDiffButtonControl => WorkingTreeLargeDiffButton;
    internal StackPanel WorkingTreeDiffLoadingControl => WorkingTreeDiffLoading;
    internal InfoBar WorkingTreeBinaryInfoControl => WorkingTreeBinaryInfo;
    internal InfoBar WorkingTreeNoChangesInfoControl => WorkingTreeNoChangesInfo;
    internal StackPanel WorkingTreeImageLoadingControl => WorkingTreeImageLoading;
    internal ImageDiffPreviewHost WorkingTreeImageDiffHostControl => WorkingTreeImageDiffHost;
    internal SelectableDiffViewer WorkingTreeDiffViewerControl => WorkingTreeDiffViewer;
    internal WorkingTreeCommitEditorView WorkingTreeCommitEditorControl => WorkingTreeCommitEditor;







    internal Action<object, RoutedEventArgs>? WorkingTreeOpenOriginal_ClickCallback { get; set; }
    private void WorkingTreeOpenOriginal_Click(object sender, RoutedEventArgs args) =>
        WorkingTreeOpenOriginal_ClickCallback?.Invoke(sender, args);

    internal Action<object, RoutedEventArgs>? WorkingTreeOpenChanged_ClickCallback { get; set; }
    private void WorkingTreeOpenChanged_Click(object sender, RoutedEventArgs args) =>
        WorkingTreeOpenChanged_ClickCallback?.Invoke(sender, args);

    internal Action<object, RoutedEventArgs>? WorkingTreeExternalDiff_ClickCallback { get; set; }
    private void WorkingTreeExternalDiff_Click(object sender, RoutedEventArgs args) =>
        WorkingTreeExternalDiff_ClickCallback?.Invoke(sender, args);

    internal Action<object, RoutedEventArgs>? WorkingTreeReveal_ClickCallback { get; set; }
    private void WorkingTreeReveal_Click(object sender, RoutedEventArgs args) =>
        WorkingTreeReveal_ClickCallback?.Invoke(sender, args);

}
