using System.Collections.Specialized;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Dispatching;

namespace CSharpGit.Presentation.Workflows;

// Per-window queue for coalescing view projections; domain monitor remains independent.
internal sealed class RepositoryPresentationRefreshController(
    OpenRepositoryViewModel viewModel,
    DispatcherQueue dispatcher,
    Func<bool> isShuttingDown,
    Action refreshWorkingTree,
    Action refreshRepositoryTree,
    Action updateStatusBar)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private bool _subscribed;
    private readonly DispatcherQueue _dispatcher = dispatcher;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Action _refreshWorkingTree = refreshWorkingTree;
    private readonly Action _refreshRepositoryTree = refreshRepositoryTree;
    private readonly Action _updateStatusBar = updateStatusBar;

    private bool _repositoryPresentationRefreshQueued;
    private bool _repositoryTreePresentationDirty;
    private bool _workingTreePresentationDirty;

    internal void Initialize()
    {
        if (_subscribed) return;
        _subscribed = true;
        _viewModel.WorkingTree.Changes.CollectionChanged += RepositoryPresentationChanges_CollectionChanged;
        _viewModel.Branches.LocalBranches.CollectionChanged += RepositoryPresentationLocalBranches_CollectionChanged;
        _viewModel.Branches.RemoteBranches.CollectionChanged += RepositoryPresentationRemoteBranches_CollectionChanged;
        _viewModel.RepositorySync.Remotes.CollectionChanged += RepositoryPresentationRemotes_CollectionChanged;
        _viewModel.Tags.Items.CollectionChanged += RepositoryPresentationTags_CollectionChanged;
        _viewModel.Stashes.Items.CollectionChanged += RepositoryPresentationStashes_CollectionChanged;
    }

    internal void Shutdown()
    {
        if (!_subscribed) return;
        _subscribed = false;
        _viewModel.WorkingTree.Changes.CollectionChanged -= RepositoryPresentationChanges_CollectionChanged;
        _viewModel.Branches.LocalBranches.CollectionChanged -= RepositoryPresentationLocalBranches_CollectionChanged;
        _viewModel.Branches.RemoteBranches.CollectionChanged -= RepositoryPresentationRemoteBranches_CollectionChanged;
        _viewModel.RepositorySync.Remotes.CollectionChanged -= RepositoryPresentationRemotes_CollectionChanged;
        _viewModel.Tags.Items.CollectionChanged -= RepositoryPresentationTags_CollectionChanged;
        _viewModel.Stashes.Items.CollectionChanged -= RepositoryPresentationStashes_CollectionChanged;
        ClearRepositoryPresentationRefreshQueue();
    }

    private void RepositoryPresentationChanges_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Queue(workingTreeChanged: true);

    private void RepositoryPresentationLocalBranches_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Queue();

    private void RepositoryPresentationRemoteBranches_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Queue();

    private void RepositoryPresentationRemotes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Queue();

    private void RepositoryPresentationTags_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Queue();

    private void RepositoryPresentationStashes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Queue();

    private void Queue(bool workingTreeChanged = false)
    {
        if (_isShuttingDown()) return;

        if (workingTreeChanged) _workingTreePresentationDirty = true;
        else _repositoryTreePresentationDirty = true;
        if (_repositoryPresentationRefreshQueued) return;

        _repositoryPresentationRefreshQueued = true;
        if (_dispatcher.TryEnqueue(FlushRepositoryPresentationRefresh)) return;

        if (!_isShuttingDown() && _dispatcher.HasThreadAccess)
        {
            FlushRepositoryPresentationRefresh();
            return;
        }

        ClearRepositoryPresentationRefreshQueue();
    }

    private void FlushRepositoryPresentationRefresh()
    {
        if (_isShuttingDown())
        {
            ClearRepositoryPresentationRefreshQueue();
            return;
        }

        _repositoryPresentationRefreshQueued = false;
        var refreshWorkingTree = _workingTreePresentationDirty;
        var refreshRepositoryTree = _repositoryTreePresentationDirty;
        _workingTreePresentationDirty = false;
        _repositoryTreePresentationDirty = false;
        if (refreshWorkingTree) _refreshWorkingTree();
        if (refreshRepositoryTree) _refreshRepositoryTree();
        _updateStatusBar();
    }

    private void ClearRepositoryPresentationRefreshQueue()
    {
        _repositoryPresentationRefreshQueued = false;
        _workingTreePresentationDirty = false;
        _repositoryTreePresentationDirty = false;
    }
}
