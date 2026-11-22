using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _changesSurfaceInitialized;
    private Workflows.CommitChangedFileTreeController? _changesTree;
    private CollectionViewportController? _commitSummaryViewport;
    private CollectionViewportController? _stashSummaryViewport;

    private PivotItem? ChangesTabControl =>
        DetailsTabs.Items.Count > 1 ? DetailsTabs.Items[1] as PivotItem : null;

    private void ChangesSurface_Loaded(object sender, RoutedEventArgs args)
    {
        if (_changesSurfaceInitialized) return;
        _changesSurfaceInitialized = true;

        _changesTree = new Workflows.CommitChangedFileTreeController(
            _viewModel.CommitDetails, _commitFiles, ChangedFilesTree,
            ChangesTabControl, DispatcherQueue, SelectedDetailsViewportKey);
        _changesTree.Initialize();
        _viewModel.CommitDetails.PropertyChanged += ChangesViewModel_PropertyChanged;

        _changesTree.Rebuild();
        RebuildCompactDiff();
        UpdateChangesViewActivity();
    }

    private void DetailsTabs_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        UpdateChangesViewActivity();
        UpdateRepositoryFilesViewActivity();
    }

    private void UpdateChangesViewActivity()
    {
        var active = ReferenceEquals(DetailsTabs.SelectedItem, ChangesTabControl);
        _viewModel.CommitDetails.SetActive(active);
        if (!active) _commitImageDiff.Cancel(clearSurface: true);
    }

    private void ChangesViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(CommitDetailsViewModel.SelectedDiff)
            or nameof(CommitDetailsViewModel.DiffLoadErrorMessage)
            or nameof(CommitDetailsViewModel.IsDiffPreviewDeferred)
            or nameof(CommitDetailsViewModel.DiffPreviewDeferredMessage)
            or nameof(CommitDetailsViewModel.DiffPreviewActionText)
            or nameof(CommitDetailsViewModel.IsNoNetStashDiff))
        {
            RebuildCompactDiff();
        }
        else if (args.PropertyName == nameof(CommitDetailsViewModel.SelectedFile))
        {
            _commitImageDiff.Cancel(clearSurface: true);
            CompactDiffViewer.Clear();
            _diffPresentation.RenderCommit(
                _viewModel.CommitDetails.SelectedFile is null
                    ? DiffPresentationState.NothingSelected
                    : DiffPresentationState.LoadingDiff);
            _changesTree?.SyncSelection();
        }
        else if (args.PropertyName == nameof(CommitDetailsViewModel.ChangedFiles))
        {
            _changesTree?.EnsureCurrentFileSelection();
        }
    }

    private void RebuildCompactDiff()
    {
        if (!_changesSurfaceInitialized) return;

        _commitImageDiff.Cancel(clearSurface: false);
        if (_viewModel.CommitDetails.SelectedDiff is not { } diff)
        {
            CompactDiffViewer.Clear();
            var state = _viewModel.CommitDetails.SelectedFile is null
                ? DiffPresentationState.NothingSelected
                : _viewModel.CommitDetails.IsNoNetStashDiff
                    ? DiffPresentationState.NoNetStashDiff
                : _viewModel.CommitDetails.IsDiffPreviewDeferred
                    ? DiffPresentationState.LargeDiff
                    : !string.IsNullOrWhiteSpace(_viewModel.CommitDetails.DiffLoadErrorMessage)
                        ? DiffPresentationState.Error
                        : DiffPresentationState.LoadingDiff;
            _diffPresentation.RenderCommit(state);
            return;
        }

        var compactLines = diff.IsBinary
            ? []
            : CompactDiffLine.Build(diff.Lines);
        switch (DiffPresentationResolver.Resolve(diff, compactLines.Count))
        {
            case DiffContentPresentation.Binary:
                CompactDiffViewer.Clear();
                RunPageUiOperation(_commitImageDiff.LoadAsync, "Commit image diff");
                return;
            case DiffContentPresentation.NoTextualPatch:
                CompactDiffViewer.Clear();
                _diffPresentation.RenderCommit(DiffPresentationState.NoTextualPatch);
                return;
            case DiffContentPresentation.Text:
                CompactDiffViewer.SetLines(compactLines, diff.Diagnostics);
                _diffPresentation.RenderCommit(DiffPresentationState.Text);
                return;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void SynchronizeDetailsViewport()
    {
        var details = _viewModel.CommitDetails;
        var key = SelectedDetailsViewportKey();
        _commitSummaryViewport?.Observe(key, details.HasSelectedCommit ? 1 : 0);
        _stashSummaryViewport?.Observe(key, details.HasSelectedStash ? 1 : 0);
    }

    private void CommitLargeDiffButton_Click(object sender, RoutedEventArgs args) =>
        _viewModel.CommitDetails.LoadSelectedDiffAnyway();

    private void ChangedFilesTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        var node = Workflows.CommitChangedFileTreeController.ResolveChangedFileNode(args.InvokedItem);
        if (node?.Entry is null) return;

        if (!ReferenceEquals(_viewModel.CommitDetails.SelectedFile, node.Entry.File) && _viewModel.CommitDetails.SelectedFile == node.Entry.File)
            _viewModel.CommitDetails.SelectedFile = null;
        _viewModel.CommitDetails.SelectedFile = node.Entry.File;
    }

    private string? SelectedDetailsViewportKey()
    {
        var details = _viewModel.CommitDetails;
        var selectedCommit = details.SelectedObjectCommit;
        return selectedCommit is null
            ? null
            : $"{_viewModel.Repository?.GitDirectory}|{(details.HasSelectedStash ? "stash" : "commit")}|{selectedCommit}";
    }

}
