using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly ObservableCollection<ChangedFileTreeNode> _changedFileTreeRoots = [];
    private bool _changesSurfaceInitialized;
    private bool _changedFileTreeRefreshQueued;

    private PivotItem? ChangesTabControl =>
        DetailsTabs.Items.Count > 1 ? DetailsTabs.Items[1] as PivotItem : null;

    private void ChangesSurface_Loaded(object sender, RoutedEventArgs args)
    {
        if (_changesSurfaceInitialized) return;
        _changesSurfaceInitialized = true;

        ChangedFilesTree.ItemsSource = _changedFileTreeRoots;
        _commitFiles.CollectionChanged += CommitFiles_CollectionChanged;
        _viewModel.CommitDetails.PropertyChanged += ChangesViewModel_PropertyChanged;

        RebuildChangedFileTree();
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
        if (!active) CancelCommitImageDiff(clearSurface: true);
    }

    private void CommitFiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (!_changesSurfaceInitialized || _changedFileTreeRefreshQueued) return;
        _changedFileTreeRefreshQueued = true;
        if (DispatcherQueue.TryEnqueue(() =>
        {
            _changedFileTreeRefreshQueued = false;
            RebuildChangedFileTree();
        })) return;

        _changedFileTreeRefreshQueued = false;
        RebuildChangedFileTree();
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
            CancelCommitImageDiff(clearSurface: true);
            CompactDiffViewer.Clear();
            SetCommitDiffPresentationState(
                _viewModel.CommitDetails.SelectedFile is null
                    ? DiffPresentationState.NothingSelected
                    : DiffPresentationState.LoadingDiff);
            SyncChangedFileTreeSelection();
        }
        else if (args.PropertyName == nameof(CommitDetailsViewModel.ChangedFiles))
        {
            EnsureCurrentCommitFileSelection();
        }
    }

    private void RebuildChangedFileTree()
    {
        if (!_changesSurfaceInitialized) return;

        var entries = _commitFiles.Select(row => new ChangedFileTreeEntry(row.Status, row.File));
        ChangedFileTreeSynchronizer.Reconcile(_changedFileTreeRoots, entries);

        if (ChangesTabControl is { } changesTab)
            changesTab.Header = _commitFiles.Count == 0 ? "Changes" : $"Changes ({_commitFiles.Count})";
        EnsureCurrentCommitFileSelection();
        SyncChangedFileTreeSelection();
    }

    private void RebuildCompactDiff()
    {
        if (!_changesSurfaceInitialized) return;

        CancelCommitImageDiff(clearSurface: false);
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
            SetCommitDiffPresentationState(state);
            return;
        }

        var compactLines = diff.IsBinary
            ? []
            : CompactDiffLine.Build(diff.Lines);
        switch (DiffPresentationResolver.Resolve(diff, compactLines.Count))
        {
            case DiffContentPresentation.Binary:
                CompactDiffViewer.Clear();
                _ = LoadCommitImageDiffAsync();
                return;
            case DiffContentPresentation.NoTextualPatch:
                CompactDiffViewer.Clear();
                SetCommitDiffPresentationState(DiffPresentationState.NoTextualPatch);
                return;
            case DiffContentPresentation.Text:
                CompactDiffViewer.SetLines(compactLines, diff.Diagnostics);
                SetCommitDiffPresentationState(DiffPresentationState.Text);
                return;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void EnsureCurrentCommitFileSelection()
    {
        if (!_viewModel.CommitDetails.IsChangesViewActive) return;
        var files = _viewModel.CommitDetails.ChangedFiles;
        if (files.Count == 0) return;

        var selected = _viewModel.CommitDetails.SelectedFile;
        var current = selected is null
            ? files[0]
            : files.FirstOrDefault(file => string.Equals(file.Path, selected.Path, StringComparison.Ordinal)) ?? files[0];

        if (ReferenceEquals(selected, current)) return;
        _viewModel.CommitDetails.SelectedFile = current;
    }

    private void SyncChangedFileTreeSelection()
    {
        if (!_changesSurfaceInitialized || _viewModel.CommitDetails.SelectedFile is null) return;
        var node = FindChangedFileNode(_changedFileTreeRoots, _viewModel.CommitDetails.SelectedFile.Path);
        if (node is not null && !ReferenceEquals(ChangedFilesTree.SelectedItem, node))
            ChangedFilesTree.SelectedItem = node;
    }

    private void CommitLargeDiffButton_Click(object sender, RoutedEventArgs args) =>
        _viewModel.CommitDetails.LoadSelectedDiffAnyway();

    private void ChangedFilesTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        var node = ResolveChangedFileNode(args.InvokedItem);
        if (node?.Entry is null) return;

        if (!ReferenceEquals(_viewModel.CommitDetails.SelectedFile, node.Entry.File) && _viewModel.CommitDetails.SelectedFile == node.Entry.File)
            _viewModel.CommitDetails.SelectedFile = null;
        _viewModel.CommitDetails.SelectedFile = node.Entry.File;
    }

    private static ChangedFileTreeNode? ResolveChangedFileNode(object? value) => value switch
    {
        ChangedFileTreeNode node => node,
        TreeViewNode { Content: ChangedFileTreeNode node } => node,
        TreeViewItem { DataContext: ChangedFileTreeNode node } => node,
        FrameworkElement { DataContext: ChangedFileTreeNode node } => node,
        _ => null
    };

    private static ChangedFileTreeNode? FindChangedFileNode(
        IEnumerable<ChangedFileTreeNode> nodes,
        string path)
    {
        foreach (var node in nodes)
        {
            if (node.Entry is not null && string.Equals(node.Entry.File.Path, path, StringComparison.Ordinal))
                return node;
            if (FindChangedFileNode(node.Children, path) is { } match) return match;
        }
        return null;
    }
}
