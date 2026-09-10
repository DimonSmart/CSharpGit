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
        CompactDiffList.MinHeight = 96;
        _commitFiles.CollectionChanged += CommitFiles_CollectionChanged;
        _viewModel.PropertyChanged += ChangesViewModel_PropertyChanged;

        EnsureCurrentCommitFileSelection();
        UpdateCommitDiffHeaderRow();
        RebuildChangedFileTree();
        RebuildCompactDiff();
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
        if (args.PropertyName == nameof(OpenRepositoryViewModel.SelectedDiff))
        {
            UpdateCommitDiffHeaderRow();
            RebuildCompactDiff();
        }
        else if (args.PropertyName == nameof(OpenRepositoryViewModel.SelectedFile))
        {
            SyncChangedFileTreeSelection();
        }
        else if (args.PropertyName == nameof(OpenRepositoryViewModel.SelectedCommit))
        {
            EnsureCurrentCommitFileSelection();
        }
    }

    private void UpdateCommitDiffHeaderRow()
    {
        if (CompactDiffList.Parent is not Grid contentGrid ||
            contentGrid.Parent is not Grid diffGrid ||
            diffGrid.RowDefinitions.Count < 2)
            return;

        diffGrid.RowDefinitions[0].Height = _viewModel.DiffVisibility == Visibility.Visible
            ? new GridLength(30)
            : new GridLength(0);
    }

    private void RebuildChangedFileTree()
    {
        if (!_changesSurfaceInitialized) return;

        var entries = _commitFiles.Select(row => new ChangedFileTreeEntry(row.Status, row.File));
        var roots = ChangedFileTreeNode.Build(entries);

        _changedFileTreeRoots.Clear();
        foreach (var root in roots) _changedFileTreeRoots.Add(root);

        FilesTab.Header = _viewModel.SelectedCommit is null ? "Changes" : $"Changes ({_commitFiles.Count})";
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
        var commit = _viewModel.SelectedCommit;
        if (commit is null || commit.Files.Count == 0) return;

        var selected = _viewModel.SelectedFile;
        var current = selected is null
            ? commit.Files[0]
            : commit.Files.FirstOrDefault(file => string.Equals(file.Path, selected.Path, StringComparison.Ordinal)) ?? commit.Files[0];

        if (ReferenceEquals(selected, current)) return;

        // ChangedFile is a record. Two different commits can therefore produce
        // value-equal file objects, while the diff must still be reloaded for the
        // newly selected commit.
        _viewModel.SelectedFile = null;
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
