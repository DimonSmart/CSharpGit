using System.ComponentModel;
using System.Diagnostics;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _repositoryTreeStateTrackingAttached;
    private RepositoryTreeSynchronizer? _repositoryTreeSynchronizer;

    private RepositoryTreeSynchronizer RepositoryTreeSynchronizer =>
        _repositoryTreeSynchronizer ??= new RepositoryTreeSynchronizer(_repositoryTreeRoots);

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        InitializeHistoryInfiniteScroll();
        InitializeLoadingOverlays();

        Loaded -= RepositoryTreeState_Loaded;
        Loaded += RepositoryTreeState_Loaded;
        TryAttachRepositoryTreeStateTracking();
    }

    private void RepositoryTreeState_Loaded(object sender, RoutedEventArgs args)
    {
        Loaded -= RepositoryTreeState_Loaded;
        InitializeLoadingOverlays();
        TryAttachRepositoryTreeStateTracking();
    }

    private void TryAttachRepositoryTreeStateTracking()
    {
        if (_repositoryTreeStateTrackingAttached || RepositoryTree is null || _viewModel is null) return;

        RepositoryTree.Expanding += RepositoryTree_Expanding;
        RepositoryTree.Collapsed += RepositoryTree_Collapsed;
        _viewModel.PropertyChanged += RepositoryTreeViewModel_PropertyChanged;
        _repositoryTreeStateTrackingAttached = true;

        if (_viewModel.Repository is null) return;
        RepositoryTreeNode.ResetExpansionState();
        ResetRepositoryTreeForLifecycle();
    }

    private void RepositoryTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (ResolveNode(args.Item) is { } node) node.IsExpanded = true;
    }

    private void RepositoryTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (ResolveNode(args.Item) is { } node) node.IsExpanded = false;
    }

    private void RepositoryTreeViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(OpenRepositoryViewModel.Repository)) return;

        RepositoryTreeNode.ResetExpansionState();
        ResetRepositoryTreeForLifecycle();
    }

    private void ResetRepositoryTreeForLifecycle()
    {
        _repositoryTreeRoots.Clear();
        RepositoryTreeSynchronizer.ResetSession();
        _worktrees = [];
        RepositoryTree.SelectedItem = null;
        if (_viewModel.Repository is not null) SynchronizeRepositoryTree();
    }

    private void SynchronizeRepositoryTree()
    {
        var selection = CaptureRepositoryTreeSelection();
        try
        {
            RepositoryTreeSynchronizer.ReconcileRepository(
                _viewModel.LocalBranches,
                _viewModel.RemoteBranches,
                _viewModel.Remotes,
                _viewModel.Tags,
                _viewModel.Stashes,
                _worktrees);
        }
        catch (InvalidOperationException exception)
        {
            RecoverRepositoryTree("repository-state reconciliation", exception);
        }
        RestoreRepositoryTreeSelection(selection);
    }

    private void SynchronizeWorktreePresentation()
    {
        var selection = CaptureRepositoryTreeSelection();
        try
        {
            RepositoryTreeSynchronizer.ReconcileWorktrees(_worktrees);
        }
        catch (InvalidOperationException exception)
        {
            RecoverRepositoryTree("worktree reconciliation", exception);
        }
        RestoreRepositoryTreeSelection(selection);
    }

    private void RecoverRepositoryTree(string operation, Exception exception)
    {
        Debug.WriteLine($"Repository Tree recovery after failed {operation}: {exception}");
        _repositoryTreeRoots.Clear();
        RepositoryTreeSynchronizer.ResetSession();
        RepositoryTreeSynchronizer.ReconcileRepository(
            _viewModel.LocalBranches,
            _viewModel.RemoteBranches,
            _viewModel.Remotes,
            _viewModel.Tags,
            _viewModel.Stashes,
            _worktrees);
    }

    private RepositoryTreeSelectionAnchor<RepositoryTreeNode>? CaptureRepositoryTreeSelection() =>
        RepositoryTreeSelection.Capture(
            _repositoryTreeRoots,
            ResolveNode(RepositoryTree.SelectedItem),
            node => node.Key,
            node => node.Children);

    private void RestoreRepositoryTreeSelection(RepositoryTreeSelectionAnchor<RepositoryTreeNode>? selection)
    {
        if (selection is null) return;
        RepositoryTree.SelectedItem = RepositoryTreeSelection.Resolve(
            _repositoryTreeRoots,
            selection,
            node => node.Key,
            node => node.Children);
    }

    private void DetachRepositoryTreeStateTracking()
    {
        Loaded -= RepositoryTreeState_Loaded;
        DetachLoadingOverlays();
        if (!_repositoryTreeStateTrackingAttached) return;

        RepositoryTree.Expanding -= RepositoryTree_Expanding;
        RepositoryTree.Collapsed -= RepositoryTree_Collapsed;
        _viewModel.PropertyChanged -= RepositoryTreeViewModel_PropertyChanged;
        _repositoryTreeStateTrackingAttached = false;
    }
}
