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
    private readonly ObservableCollection<CompactDiffLine> _compactDiffLines = [];
    private bool _changesSurfaceInitialized;
    private bool _changedFileTreeRefreshQueued;

    private void ChangesSurface_Loaded(object sender, RoutedEventArgs args)
    {
        if (_changesSurfaceInitialized) return;
        _changesSurfaceInitialized = true;

        ChangedFilesTree.ItemsSource = _changedFileTreeRoots;
        CompactDiffList.ItemsSource = _compactDiffLines;
        _commitFiles.CollectionChanged += CommitFiles_CollectionChanged;
        _viewModel.PropertyChanged += ChangesViewModel_PropertyChanged;

        RebuildChangedFileTree();
        RebuildCompactDiff();
        UpdateChangesViewActivity();
    }

    private void DetailsTabs_SelectionChanged(object sender, SelectionChangedEventArgs args) =>
        UpdateChangesViewActivity();

    private void UpdateChangesViewActivity() =>
        _viewModel.SetChangesViewActive(ReferenceEquals(DetailsTabs.SelectedItem, FilesTab));

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
        if (args.PropertyName == nameof(OpenRepositoryViewModel.SelectedDiff))
        {
            RebuildCompactDiff();
        }
        else if (args.PropertyName == nameof(OpenRepositoryViewModel.SelectedFile))
        {
            SyncChangedFileTreeSelection();
        }
        else if (args.PropertyName == nameof(OpenRepositoryViewModel.SelectedChangedFiles))
        {
            EnsureCurrentCommitFileSelection();
        }
    }

    private void RebuildChangedFileTree()
    {
        if (!_changesSurfaceInitialized) return;

        var entries = _commitFiles.Select(row => new ChangedFileTreeEntry(row.Status, row.File));
        var roots = ChangedFileTreeNode.Build(entries);

        _changedFileTreeRoots.Clear();
        foreach (var root in roots) _changedFileTreeRoots.Add(root);

        FilesTab.Header = _commitFiles.Count == 0 ? "Changes" : $"Changes ({_commitFiles.Count})";
        EnsureCurrentCommitFileSelection();
        SyncChangedFileTreeSelection();
    }

    private void RebuildCompactDiff()
    {
        if (!_changesSurfaceInitialized) return;

        _compactDiffLines.Clear();
        if (_viewModel.SelectedDiff is not { IsBinary: false } diff) return;
        foreach (var line in CompactDiffLine.Build(diff.Lines)) _compactDiffLines.Add(line);
    }

    private void EnsureCurrentCommitFileSelection()
    {
        if (!_viewModel.IsChangesViewActive) return;
        var files = _viewModel.SelectedChangedFiles;
        if (files.Count == 0) return;

        var selected = _viewModel.SelectedFile;
        var current = selected is null
            ? files[0]
            : files.FirstOrDefault(file => string.Equals(file.Path, selected.Path, StringComparison.Ordinal)) ?? files[0];

        if (ReferenceEquals(selected, current)) return;
        _viewModel.SelectedFile = current;
    }

    private void SyncChangedFileTreeSelection()
    {
        if (!_changesSurfaceInitialized || _viewModel.SelectedFile is null) return;
        var node = FindChangedFileNode(_changedFileTreeRoots, _viewModel.SelectedFile.Path);
        if (node is not null && !ReferenceEquals(ChangedFilesTree.SelectedItem, node))
            ChangedFilesTree.SelectedItem = node;
    }

    private void ChangedFilesTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        var node = ResolveChangedFileNode(args.InvokedItem);
        if (node?.Entry is null) return;

        if (!ReferenceEquals(_viewModel.SelectedFile, node.Entry.File) && _viewModel.SelectedFile == node.Entry.File)
            _viewModel.SelectedFile = null;
        _viewModel.SelectedFile = node.Entry.File;
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
