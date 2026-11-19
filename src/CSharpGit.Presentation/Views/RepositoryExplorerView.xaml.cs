using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CSharpGit.Domain;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation.Views;

/// <summary>
/// Owns the tree's visual nodes, incremental synchronization, expansion, selection,
/// and interaction dispatch. The phase-3 reference-operation menus remain behind
/// a temporary, narrow ContextRequested adapter.
/// </summary>
public sealed partial class RepositoryExplorerView : UserControl
{
    private readonly ObservableCollection<RepositoryTreeNode> _roots = [];
    private readonly RepositoryTreeSynchronizer _synchronizer;
    private readonly CollectionViewportController _viewport;
    private OpenRepositoryViewModel? _repository;
    private WorktreesViewModel? _worktrees;
    private WorkspaceNavigator? _navigator;
    private bool _disposed;
    private bool _configured;

    public RepositoryExplorerView()
    {
        InitializeComponent();
        _synchronizer = new RepositoryTreeSynchronizer(_roots);
        Tree.ItemsSource = _roots;
        _viewport = new CollectionViewportController(Tree);
    }

    internal TreeView TreeControl => Tree;
    internal IReadOnlyList<RepositoryTreeNode> RootsForCheck => _roots;

    internal event Action<RepositoryTreeNode, long>? NavigateRequested;
    internal event Action<string, long>? CheckoutRequested;
    internal event Action<object, RightTappedRoutedEventArgs>? ReferenceMenuRequested;

    internal void Configure(OpenRepositoryViewModel repository,
        WorktreesViewModel worktrees, WorkspaceNavigator navigator)
    {
        if (_disposed || _configured) return;
        _configured = true;
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _worktrees = worktrees ?? throw new ArgumentNullException(nameof(worktrees));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        Tree.Expanding += Tree_Expanding;
        Tree.Collapsed += Tree_Collapsed;
        _repository.PropertyChanged += Repository_PropertyChanged;

        if (_repository.Repository is not null)
        {
            RepositoryTreeNode.ResetExpansionState();
            ResetForRepositoryChange();
        }
    }

    private void Repository_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || e.PropertyName != nameof(OpenRepositoryViewModel.Repository)) return;
        RepositoryTreeNode.ResetExpansionState();
        ResetForRepositoryChange();
    }

    internal void ResetForRepositoryChange()
    {
        if (_disposed) return;
        _roots.Clear();
        _synchronizer.ResetSession();
        Tree.SelectedItem = null;
        ObserveTreeViewport();
        if (_repository?.Repository is not null)
            ReconcileRepository();
    }

    internal void ReconcileRepository()
    {
        if (_disposed || _repository is null || _worktrees is null) return;
        var selection = CaptureSelection();
        try
        {
            _synchronizer.ReconcileRepository(
                _repository.Branches.LocalBranches,
                _repository.Branches.RemoteBranches,
                _repository.RepositorySync.Remotes,
                _repository.Tags.Items,
                _repository.Stashes.Items,
                _worktrees.Worktrees);
        }
        catch (InvalidOperationException exception)
        {
            Recover("repository-state reconciliation", exception);
        }
        RestoreSelection(selection);
        ObserveTreeViewport();
    }

    internal void ReconcileWorktrees()
    {
        if (_disposed || _repository is null || _worktrees is null) return;
        var selection = CaptureSelection();
        try
        {
            _synchronizer.ReconcileWorktrees(_worktrees.Worktrees);
        }
        catch (InvalidOperationException exception)
        {
            Recover("worktree reconciliation", exception);
        }
        RestoreSelection(selection);
        ObserveTreeViewport();
    }

    private void Recover(string operation, Exception exception)
    {
        Debug.WriteLine($"Repository Tree recovery after failed {operation}: {exception}");
        _roots.Clear();
        _synchronizer.ResetSession();
        if (_repository is null || _worktrees is null) return;
        _synchronizer.ReconcileRepository(
            _repository.Branches.LocalBranches,
            _repository.Branches.RemoteBranches,
            _repository.RepositorySync.Remotes,
            _repository.Tags.Items,
            _repository.Stashes.Items,
            _worktrees.Worktrees);
    }

    private void ObserveTreeViewport() =>
        _viewport.Observe(_repository?.Repository?.GitDirectory, CountVisibleNodes(_roots));

    private static int CountVisibleNodes(IEnumerable<RepositoryTreeNode> nodes)
    {
        var count = 0;
        foreach (var node in nodes)
        {
            count++;
            if (node.IsExpanded) count += CountVisibleNodes(node.Children);
        }
        return count;
    }

    private RepositoryTreeSelectionAnchor<RepositoryTreeNode>? CaptureSelection() =>
        RepositoryTreeSelection.Capture(
            _roots, ResolveNode(Tree.SelectedItem),
            node => node.Key, node => node.Children);

    private void RestoreSelection(RepositoryTreeSelectionAnchor<RepositoryTreeNode>? selection)
    {
        if (selection is null) return;
        Tree.SelectedItem = RepositoryTreeSelection.Resolve(
            _roots, selection, node => node.Key, node => node.Children);
    }

    private void Tree_Expanding(TreeView sender, TreeViewExpandingEventArgs e)
    {
        if (ResolveNode(e.Item) is { } node) node.IsExpanded = true;
    }

    private void Tree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs e)
    {
        if (ResolveNode(e.Item) is { } node) node.IsExpanded = false;
    }

    private void Tree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs e)
    {
        if (_disposed || _repository is null || _navigator is null
            || ResolveNode(e.InvokedItem) is not { } node) return;
        Tree.SelectedItem = node;
        var version = _navigator.Interactions.BeginInteraction();
        if (node.Kind is RepositoryTreeNodeKind.LocalBranch
            or RepositoryTreeNodeKind.RemoteBranch
            or RepositoryTreeNodeKind.Tag
            or RepositoryTreeNodeKind.Stash
            or RepositoryTreeNodeKind.Group)
            _repository.History.Invalidate();
        NavigateRequested?.Invoke(node, version);
    }

    private void Tree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_disposed || _repository is null || _navigator is null) return;
        var node = ResolveNode((e.OriginalSource as FrameworkElement)?.DataContext);
        if (node?.Kind != RepositoryTreeNodeKind.LocalBranch || node.Value is not GitBranch branch)
            return;

        var version = _navigator.Interactions.BeginInteraction();
        _repository.History.Invalidate();
        e.Handled = true;
        CheckoutRequested?.Invoke(branch.Name, version);
    }

    private void Tree_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_disposed) return;
        ReferenceMenuRequested?.Invoke(sender, e);
    }

    internal static RepositoryTreeNode? ResolveNode(object? value) => value switch
    {
        RepositoryTreeNode node => node,
        TreeViewNode { Content: RepositoryTreeNode node } => node,
        TreeViewItem { DataContext: RepositoryTreeNode node } => node,
        FrameworkElement { DataContext: RepositoryTreeNode node } => node,
        _ => null
    };

    internal void Shutdown()
    {
        if (_disposed) return;
        _disposed = true;
        if (_configured && _repository is not null)
            _repository.PropertyChanged -= Repository_PropertyChanged;
        Tree.Expanding -= Tree_Expanding;
        Tree.Collapsed -= Tree_Collapsed;
        NavigateRequested = null;
        CheckoutRequested = null;
        ReferenceMenuRequested = null;
        _repository = null;
        _worktrees = null;
        _navigator = null;
        _viewport.Dispose();
        _roots.Clear();
        Tree.ItemsSource = null;
    }
}
