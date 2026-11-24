using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Views;

public sealed partial class HistoryWorkspaceView
{
    private OpenRepositoryViewModel _viewModel = null!;
    private readonly ObservableCollection<CommitFileRow> _commitFiles = [];
    private Func<Workflows.CommitImageDiffWorkflow> _imageWorkflow = null!;
    private Workflows.DiffPresentationController _diffPresentation = null!;
    private Action _updateRepositoryFilesViewActivity = null!;
    private Action<Func<Task>, string> _runUiOperation = null!;
    private bool _changesConfigured;
    private Workflows.CommitImageDiffWorkflow _commitImageDiff => _imageWorkflow();

    internal void ConfigureChanges(
        OpenRepositoryViewModel viewModel,
        Func<Workflows.CommitImageDiffWorkflow> imageWorkflow,
        Workflows.DiffPresentationController diffPresentation,
        Action updateRepositoryFilesViewActivity,
        Action<Func<Task>, string> runUiOperation)
    {
        if (_changesConfigured) throw new InvalidOperationException("Commit changes already configured.");
        _changesConfigured = true;
        _viewModel = viewModel;
        CommitFilesList.ItemsSource = _commitFiles;
        _imageWorkflow = imageWorkflow;
        _diffPresentation = diffPresentation;
        _updateRepositoryFilesViewActivity = updateRepositoryFilesViewActivity;
        _runUiOperation = runUiOperation;
        _viewModel.CommitDetails.PropertyChanged += CommitDetailsProjection_PropertyChanged;
        _commitSummaryViewport = new CollectionViewportController(DetailsScroller);
        _stashSummaryViewport = new CollectionViewportController(StashDetailsScroller);
        SynchronizeDetailsViewport();
    }

    internal void ShutdownChanges()
    {
        if (!_changesConfigured) return;
        _changesConfigured = false;
        _viewModel.CommitDetails.PropertyChanged -= CommitDetailsProjection_PropertyChanged;
        _changesTree?.Shutdown();
        _changesTree = null;
        _commitSummaryViewport?.Dispose();
        _stashSummaryViewport?.Dispose();
        _commitSummaryViewport = null;
        _stashSummaryViewport = null;
        if (_changesSurfaceInitialized)
            _viewModel.CommitDetails.PropertyChanged -= ChangesViewModel_PropertyChanged;
        _changesSurfaceInitialized = false;
    }

    private void CommitDetailsProjection_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CommitDetailsViewModel.ChangedFiles))
            _runUiOperation(RefreshCommitFilesAsync, "Refresh commit file list");
        else if (args.PropertyName == nameof(CommitDetailsViewModel.SelectedObjectCommit))
            SynchronizeDetailsViewport();
    }

    private Task RefreshCommitFilesAsync()
    {
        var files = _viewModel.CommitDetails.ChangedFiles;
        _commitFiles.Clear();
        ChangesTab.Header = files.Count == 0 ? "Changes" : $"Changes ({files.Count})";

        foreach (var file in files)
            _commitFiles.Add(new CommitFileRow(
                _viewModel.CommitDetails.GetChangedFileDisplayStatus(file),
                file));

        var selected = _commitFiles.FirstOrDefault(row => ReferenceEquals(row.File, _viewModel.CommitDetails.SelectedFile)) ?? _commitFiles.FirstOrDefault();
        CommitFilesList.SelectedItem = selected;
        return Task.CompletedTask;
    }

    internal IReadOnlyList<ChangedFileTreeNode> ChangedFileRoots => _changesTree?.Roots ?? [];

    private bool _changesSurfaceInitialized;
    private Workflows.CommitChangedFileTreeController? _changesTree;
    private CollectionViewportController? _commitSummaryViewport;
    private CollectionViewportController? _stashSummaryViewport;

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
        _updateRepositoryFilesViewActivity();
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
                _runUiOperation(_commitImageDiff.LoadAsync, "Commit image diff");
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

    internal void SynchronizeDetailsViewport()
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
