using System.Windows.Input;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation.Workflows;

// Owns workspace navigation and deferred repository-tree interactions.
// WorkspaceNavigator remains the XAML-free semantic owner of navigation intent.
internal sealed class WorkspaceNavigationController(
    OpenRepositoryViewModel viewModel,
    WorkspaceNavigator navigator,
    HistoryWorkspaceView historyWorkspace,
    WorkingTreeView workingTree,
    RepositoryToolbarView toolbar,
    DispatcherQueue dispatcher,
    Func<ICommand, Task> executeCommand,
    Func<bool> isShuttingDown,
    Action<bool> setStashFilesMode)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly WorkspaceNavigator _navigator = navigator;
    private readonly HistoryWorkspaceView _historyWorkspace = historyWorkspace;
    private readonly WorkingTreeView _workingTree = workingTree;
    private readonly RepositoryToolbarView _toolbar = toolbar;
    private readonly DispatcherQueue _dispatcher = dispatcher;
    private readonly Func<ICommand, Task> _executeCommand = executeCommand;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Action<bool> _setStashFilesMode = setStashFilesMode;

    private RepositoryTreeNavigationCoordinator Interactions => _navigator.Interactions;
    private HistoryView HistoryView => _historyWorkspace.HistoryViewControl;

    internal void ResetForRepositoryChange() => _navigator.ResetForRepositoryChange();

    internal async Task NavigateToReferenceAsync(
        string commitHash,
        bool preserveSelectedStash = false,
        long? treeInteractionVersion = null)
    {
        var target = await _navigator.NavigateToCommitAsync(
            commitHash, preserveSelectedStash, treeInteractionVersion);
        if (_isShuttingDown()) return;

        ShowHistorySurface();
        if (target is null || !_navigator.IsTargetCurrent(target)) return;

        HistoryView.ScrollTo(target.Row);
        if (target.WasPreviouslyLoaded) return;

        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_isShuttingDown() || !_navigator.IsTargetCurrent(target)) return;
            HistoryView.ScrollTo(target.Row, updateLayout: true);
        });
    }

    internal async Task ResumePendingAsync()
    {
        if (Interactions.HasPendingCheckout)
        {
            await ResumePendingCheckoutAsync();
            return;
        }

        if (Interactions.HasPendingReference)
            await ResumePendingReferenceAsync();
    }

    private Task ResumePendingReferenceAsync()
    {
        if (!Interactions.TryTakeReference(
                _viewModel.Repository,
                _viewModel.History.SelectionIntentVersion,
                out var pending))
            return Task.CompletedTask;

        return NavigateToReferenceAsync(
            pending.CommitHash,
            pending.PreserveSelectedStash,
            pending.Version);
    }

    private Task ResumePendingCheckoutAsync()
    {
        if (!Interactions.TryTakeCheckout(_viewModel.Repository, out var pending))
            return Task.CompletedTask;

        return SwitchBranchAsync(pending.BranchName, pending.Version);
    }

    internal async Task SwitchBranchAsync(string branchName, long interactionVersion)
    {
        if (_viewModel.Repository is not { } repository
            || interactionVersion != Interactions.Version)
            return;

        if (!_viewModel.CanStartUserGitOperation)
        {
            Interactions.DeferCheckout(repository, interactionVersion, branchName);
            return;
        }

        var branch = _viewModel.Branches.LocalBranches.FirstOrDefault(
            candidate => string.Equals(candidate.Name, branchName, StringComparison.Ordinal));
        if (branch is null || branch.IsCurrent) return;

        _viewModel.Branches.SelectedLocalBranch = branch;
        await _executeCommand(_viewModel.Branches.SwitchBranchCommand);
    }

    internal async Task ShowReferenceHistoryAsync(string reference, string label)
    {
        await _navigator.ShowReferenceAsync(reference, label);
        if (_isShuttingDown()) return;
        ShowHistorySurface();
    }

    internal async Task ShowAllHistoryAsync()
    {
        var selected = await _navigator.ShowAllHistoryAsync();
        if (_isShuttingDown()) return;

        ShowHistorySurface();
        if (selected is not null)
            HistoryView.ScrollTo(selected);
    }

    internal async Task ToggleCommitNavigationAsync()
    {
        if (_workingTree.Visibility == Visibility.Visible)
            await ShowAllHistoryAsync();
        else
            ShowWorkingTree();
    }

    internal void ShowWorkingTree()
    {
        _navigator.ShowWorkingTree();
        _historyWorkspace.Visibility = Visibility.Collapsed;
        _workingTree.Visibility = Visibility.Visible;
        UpdateCommitNavigationText();

        var change = _viewModel.WorkingTree.Changes.FirstOrDefault(candidate => candidate.IsUnstaged);
        var kind = WorkingTreeDiffKind.Unstaged;
        if (change is null)
        {
            change = _viewModel.WorkingTree.Changes.FirstOrDefault(candidate => candidate.IsStaged);
            kind = WorkingTreeDiffKind.Staged;
        }

        if (change is not null)
            _workingTree.SelectWorkingTreeChange(change, kind);
    }

    internal async Task SelectRepositoryNodeAsync(RepositoryTreeNode node, long interactionVersion)
    {
        if (interactionVersion != Interactions.Version) return;

        switch (node.Kind)
        {
            case RepositoryTreeNodeKind.LocalBranch when node.Value is GitBranch local:
                _viewModel.Branches.SelectedLocalBranch = local;
                await NavigateToReferenceAsync(local.Commit, treeInteractionVersion: interactionVersion);
                break;
            case RepositoryTreeNodeKind.RemoteBranch when node.Value is GitBranch remoteBranch:
                _viewModel.Branches.SelectedRemoteBranch = remoteBranch;
                await NavigateToReferenceAsync(remoteBranch.Commit, treeInteractionVersion: interactionVersion);
                break;
            case RepositoryTreeNodeKind.Tag when node.Value is GitTag tag:
                _viewModel.Tags.SelectedTag = tag;
                await NavigateToReferenceAsync(tag.TargetCommit, treeInteractionVersion: interactionVersion);
                break;
            case RepositoryTreeNodeKind.Stash when node.Value is GitStash stash:
                await _viewModel.Stashes.SelectStashAsync(stash);
                if (interactionVersion != Interactions.Version) return;
                await NavigateToReferenceAsync(
                    stash.Commit,
                    preserveSelectedStash: true,
                    treeInteractionVersion: interactionVersion);
                UpdateStashPresentation();
                break;
            case RepositoryTreeNodeKind.Group:
                await ShowAllHistoryAsync();
                break;
        }
    }

    internal void UpdateHistoryPresentation()
    {
        HistoryView.UpdateScopePresentation();
        HistoryView.UpdateLoadMoreAvailability();
        UpdateCommitNavigationText();
    }

    internal void UpdateLoadMoreAvailability() => HistoryView.UpdateLoadMoreAvailability();

    internal void UpdateCommitNavigationText() =>
        _toolbar.SetCommitNavigationText(
            _workingTree.Visibility == Visibility.Visible,
            _viewModel.WorkingTree.Changes.Count);

    internal void UpdateStashPresentation() =>
        _setStashFilesMode(_viewModel.Stashes.HasSelectedStash);

    private void ShowHistorySurface()
    {
        _historyWorkspace.Visibility = Visibility.Visible;
        _workingTree.Visibility = Visibility.Collapsed;
        UpdateHistoryPresentation();
    }
}
