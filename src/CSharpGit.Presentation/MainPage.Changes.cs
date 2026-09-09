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
    }

    private void CommitFiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        RebuildChangedFileTree();

    private void ChangesViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(OpenRepositoryViewModel.SelectedDiff))
            RebuildCompactDiff();
        else if (args.PropertyName == nameof(OpenRepositoryViewModel.SelectedFile))
            SyncChangedFileTreeSelection();
    }

    private void RebuildChangedFileTree()
    {
        if (!_changesSurfaceInitialized) return;

        var entries = _commitFiles.Select(row => new ChangedFileTreeEntry(row.Status, row.File));
        var roots = ChangedFileTreeNode.Build(entries);

        _changedFileTreeRoots.Clear();
        foreach (var root in roots) _changedFileTreeRoots.Add(root);

        FilesTab.Header = _viewModel.SelectedCommit is null ? "Changes" : $"Changes ({_commitFiles.Count})";
        SyncChangedFileTreeSelection();
    }

    private void RebuildCompactDiff()
    {
        if (!_changesSurfaceInitialized) return;

        _compactDiffLines.Clear();
        if (_viewModel.SelectedDiff is not { IsBinary: false } diff) return;
        foreach (var line in CompactDiffLine.Build(diff.Lines)) _compactDiffLines.Add(line);
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
