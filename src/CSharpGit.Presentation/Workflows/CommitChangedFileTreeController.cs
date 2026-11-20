using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

// Owns incremental Commit changed-files tree reconciliation, selection and viewport lifetime.
internal sealed class CommitChangedFileTreeController(
    CommitDetailsViewModel details,
    ObservableCollection<CommitFileRow> commitFiles,
    TreeView tree,
    PivotItem? changesTab,
    DispatcherQueue dispatcher,
    Func<string?> viewportKey)
{
    private readonly CommitDetailsViewModel _details = details;
    private readonly ObservableCollection<CommitFileRow> _commitFiles = commitFiles;
    private readonly TreeView _tree = tree;
    private readonly PivotItem? _changesTab = changesTab;
    private readonly DispatcherQueue _dispatcher = dispatcher;
    private readonly Func<string?> _viewportKey = viewportKey;
    private readonly ObservableCollection<ChangedFileTreeNode> _roots = [];
    private CollectionViewportController? _viewport;
    private bool _initialized;
    private bool _refreshQueued;

    internal void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _tree.ItemsSource = _roots;
        _viewport = new CollectionViewportController(_tree);
        _tree.Expanding += ChangedFilesTree_Expanding;
        _tree.Collapsed += ChangedFilesTree_Collapsed;
        _commitFiles.CollectionChanged += CommitFiles_CollectionChanged;
        Rebuild();
    }

    internal void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;
        _tree.Expanding -= ChangedFilesTree_Expanding;
        _tree.Collapsed -= ChangedFilesTree_Collapsed;
        _commitFiles.CollectionChanged -= CommitFiles_CollectionChanged;
        _viewport?.Dispose();
        _viewport = null;
    }

    private void CommitFiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (!_initialized || _refreshQueued) return;
        _refreshQueued = true;
        if (_dispatcher.TryEnqueue(() =>
        {
            _refreshQueued = false;
            Rebuild();
        })) return;

        _refreshQueued = false;
        Rebuild();
    }

    internal void Rebuild()
    {
        if (!_initialized) return;

        var entries = _commitFiles.Select(row => new ChangedFileTreeEntry(row.Status, row.File));
        ChangedFileTreeSynchronizer.Reconcile(_roots, entries);
        ObserveChangedFilesViewport();

        if (_changesTab is { } changesTab)
            changesTab.Header = _commitFiles.Count == 0 ? "Changes" : $"Changes ({_commitFiles.Count})";
        EnsureCurrentFileSelection();
        SyncSelection();
    }

    internal void EnsureCurrentFileSelection()
    {
        if (!_details.IsChangesViewActive) return;
        var files = _details.ChangedFiles;
        if (files.Count == 0)
        {
            if (_details.SelectedFile is not null)
                _details.SelectedFile = null;
            return;
        }

        var selected = _details.SelectedFile;
        var current = selected is null
            ? files[0]
            : files.FirstOrDefault(file => string.Equals(file.Path, selected.Path, StringComparison.Ordinal)) ?? files[0];

        if (ReferenceEquals(selected, current)) return;
        _details.SelectedFile = current;
    }

    internal void SyncSelection()
    {
        if (!_initialized) return;
        var selected = _details.SelectedFile;
        var node = selected is null
            ? null
            : FindChangedFileNode(_roots, selected.Path);
        if (!ReferenceEquals(_tree.SelectedItem, node))
            _tree.SelectedItem = node;
    }

    private void ChangedFilesTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (ResolveChangedFileNode(args.Item) is { } node) node.IsExpanded = true;
        ObserveChangedFilesViewport();
    }

    private void ChangedFilesTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (ResolveChangedFileNode(args.Item) is { } node) node.IsExpanded = false;
        ObserveChangedFilesViewport();
    }

    private void ObserveChangedFilesViewport() =>
        _viewport?.Observe(
            _viewportKey(), CountVisibleChangedFileNodes(_roots));

    private static int CountVisibleChangedFileNodes(IEnumerable<ChangedFileTreeNode> nodes)
    {
        var count = 0;
        foreach (var node in nodes)
        {
            count++;
            if (node.IsExpanded) count += CountVisibleChangedFileNodes(node.Children);
        }
        return count;
    }

    internal static ChangedFileTreeNode? ResolveChangedFileNode(object? value) => value switch
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
