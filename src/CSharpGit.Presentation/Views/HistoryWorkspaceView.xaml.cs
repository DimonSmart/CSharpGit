using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation.Views;

// Owns History and Commit Details controls and commit-change interactions.
public sealed partial class HistoryWorkspaceView : UserControl
{
    public HistoryWorkspaceView() => InitializeComponent();

    internal RowDefinitionCollection RowDefinitions => HistoryWorkspaceLayout.RowDefinitions;

    internal HistoryView HistoryViewControl => HistoryView;
    internal Pivot DetailsTabsControl => DetailsTabs;
    internal PivotItem SummaryTabControl => SummaryTab;
    internal ScrollViewer DetailsScrollerControl => DetailsScroller;
    internal CommitDetailsView CommitDetailsContentControl => CommitDetailsContent;
    internal ScrollViewer StashDetailsScrollerControl => StashDetailsScroller;
    internal PivotItem ChangesTabControl => ChangesTab;
    internal Border ChangedFilesHeaderControl => ChangedFilesHeader;
    internal TreeView ChangedFilesTreeControl => ChangedFilesTree;
    internal ListView CommitFilesListControl => CommitFilesList;
    internal Border CommitDiffHeaderControl => CommitDiffHeader;
    internal Grid CommitTextDiffHeaderControl => CommitTextDiffHeader;
    internal Button CommitOpenOriginalButtonControl => CommitOpenOriginalButton;
    internal Button CommitOpenChangedButtonControl => CommitOpenChangedButton;
    internal Button CommitExternalDiffButtonControl => CommitExternalDiffButton;
    internal Button CommitRevealButtonControl => CommitRevealButton;
    internal StackPanel CommitLargeDiffInfoControl => CommitLargeDiffInfo;
    internal TextBlock CommitLargeDiffMessageControl => CommitLargeDiffMessage;
    internal Button CommitLargeDiffButtonControl => CommitLargeDiffButton;
    internal InfoBar CommitBinaryInfoControl => CommitBinaryInfo;
    internal StackPanel CommitImageLoadingControl => CommitImageLoading;
    internal ImageDiffPreviewHost CommitImageDiffHostControl => CommitImageDiffHost;
    internal SelectableDiffViewer CompactDiffViewerControl => CompactDiffViewer;




    internal Action<object, RoutedEventArgs>? DropSelectedStash_ClickCallback { get; set; }
    private void DropSelectedStash_Click(object sender, RoutedEventArgs args) =>
        DropSelectedStash_ClickCallback?.Invoke(sender, args);







    internal Action<object, SelectionChangedEventArgs>? CommitFilesList_SelectionChangedCallback { get; set; }
    private void CommitFilesList_SelectionChanged(object sender, SelectionChangedEventArgs args) =>
        CommitFilesList_SelectionChangedCallback?.Invoke(sender, args);

    internal Action<object, KeyRoutedEventArgs>? CommitFilesList_KeyDownCallback { get; set; }
    private void CommitFilesList_KeyDown(object sender, KeyRoutedEventArgs args) =>
        CommitFilesList_KeyDownCallback?.Invoke(sender, args);

    internal Action<object, DoubleTappedRoutedEventArgs>? CommitFilesList_DoubleTappedCallback { get; set; }
    private void CommitFilesList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args) =>
        CommitFilesList_DoubleTappedCallback?.Invoke(sender, args);

    internal Action<object, RoutedEventArgs>? CommitOpenOriginal_ClickCallback { get; set; }
    private void CommitOpenOriginal_Click(object sender, RoutedEventArgs args) =>
        CommitOpenOriginal_ClickCallback?.Invoke(sender, args);

    internal Action<object, RoutedEventArgs>? CommitOpenChanged_ClickCallback { get; set; }
    private void CommitOpenChanged_Click(object sender, RoutedEventArgs args) =>
        CommitOpenChanged_ClickCallback?.Invoke(sender, args);

    internal Action<object, RoutedEventArgs>? CommitExternalDiff_ClickCallback { get; set; }
    private void CommitExternalDiff_Click(object sender, RoutedEventArgs args) =>
        CommitExternalDiff_ClickCallback?.Invoke(sender, args);

    internal Action<object, RoutedEventArgs>? CommitReveal_ClickCallback { get; set; }
    private void CommitReveal_Click(object sender, RoutedEventArgs args) =>
        CommitReveal_ClickCallback?.Invoke(sender, args);



}
