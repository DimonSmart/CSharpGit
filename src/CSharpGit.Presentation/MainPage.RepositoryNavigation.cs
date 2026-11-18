using System.ComponentModel;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private async Task NavigateToReferenceAsync(
        string commitHash,
        bool preserveSelectedStash = false,
        long? treeInteractionVersion = null)
    {
        var repository = _viewModel.Repository;
        if (treeInteractionVersion is { } version
            && (version != _repositoryTreeNavigation.Version || repository is null))
            return;

        if (!preserveSelectedStash)
            _viewModel.Stashes.ClearSelection();

        HistoryPane.Visibility = Visibility.Visible;
        WorkingTreePane.Visibility = Visibility.Collapsed;

        // When the commit is not materialized yet, navigation replaces the history
        // ItemsSource content. Its layout may not be ready for ScrollIntoView.
        var previouslyMaterialized = _viewModel.History.Rows.FirstOrDefault(row =>
            string.Equals(row.Commit.Hash, commitHash, StringComparison.Ordinal));
        var target = await _viewModel.History.NavigateToCommitAsync(commitHash);
        if (treeInteractionVersion is { } currentVersion
            && (currentVersion != _repositoryTreeNavigation.Version
                || !ReferenceEquals(repository, _viewModel.Repository)))
            return;

        UpdateHistoryScopePresentation();
        if (target is null)
        {
            // An in-flight history read can temporarily hold the shared Git-operation gate.
            // Keep only the latest explicit tree navigation and retry when the gate is free.
            if (treeInteractionVersion is { } deferredVersion
                && repository is not null
                && !_viewModel.CanStartUserGitOperation)
                _repositoryTreeNavigation.DeferReference(
                    repository,
                    deferredVersion,
                    _viewModel.History.SelectionIntentVersion,
                    commitHash,
                    preserveSelectedStash);
            return;
        }

        HistoryList.SelectedItem = target;
        HistoryList.ScrollIntoView(target, ScrollIntoViewAlignment.Leading);

        if (ReferenceEquals(previouslyMaterialized, target)) return;

        // A Reset can invalidate the ListView's pending selection/scroll before
        // virtualized containers are laid out. Retry once on the next layout pass.
        var selectionIntentVersion = _viewModel.History.SelectionIntentVersion;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (IsShuttingDown
                || !ReferenceEquals(_viewModel.Repository, repository)
                || (treeInteractionVersion.HasValue
                    && treeInteractionVersion.Value != _repositoryTreeNavigation.Version)
                || HistoryPane.Visibility != Visibility.Visible
                || _viewModel.History.SelectionIntentVersion != selectionIntentVersion
                || !ReferenceEquals(_viewModel.History.SelectedRow, target)
                || !_viewModel.History.Rows.Contains(target))
                return;

            HistoryList.UpdateLayout();
            HistoryList.SelectedItem = target;
            HistoryList.ScrollIntoView(target, ScrollIntoViewAlignment.Leading);
        });
    }

    private Task ResumePendingRepositoryTreeNavigationAsync()
    {
        if (!_repositoryTreeNavigation.TryTakeReference(
                _viewModel.Repository,
                _viewModel.History.SelectionIntentVersion,
                out var pending))
            return Task.CompletedTask;

        return NavigateToReferenceAsync(
            pending.CommitHash,
            pending.PreserveSelectedStash,
            pending.Version);
    }

    private Task ResumePendingRepositoryTreeCheckoutAsync()
    {
        if (!_repositoryTreeNavigation.TryTakeCheckout(
                _viewModel.Repository,
                out var pending))
            return Task.CompletedTask;

        return SwitchRepositoryTreeBranchAsync(pending.BranchName, pending.Version);
    }

    private async Task SwitchRepositoryTreeBranchAsync(string branchName, long interactionVersion)
    {
        if (_viewModel.Repository is not { } repository
            || interactionVersion != _repositoryTreeNavigation.Version)
            return;

        if (!_viewModel.CanStartUserGitOperation)
        {
            _repositoryTreeNavigation.DeferCheckout(repository, interactionVersion, branchName);
            return;
        }

        // Resolve the branch again: a repository refresh may have replaced GitBranch instances.
        var branch = _viewModel.Branches.LocalBranches.FirstOrDefault(
            candidate => string.Equals(candidate.Name, branchName, StringComparison.Ordinal));
        if (branch is null || branch.IsCurrent) return;

        _viewModel.Branches.SelectedLocalBranch = branch;
        await ExecuteCommandAsync(_viewModel.Branches.SwitchBranchCommand);
    }

    private async Task ShowReferenceHistoryAsync(string reference, string label)
    {
        HistoryPane.Visibility = Visibility.Visible;
        WorkingTreePane.Visibility = Visibility.Collapsed;
        await _viewModel.History.ShowReferenceAsync(reference, label);
        UpdateHistoryScopePresentation();
    }

    private async Task ShowAllHistoryAsync()
    {
        _viewModel.Stashes.ClearSelection();
        HistoryPane.Visibility = Visibility.Visible;
        WorkingTreePane.Visibility = Visibility.Collapsed;
        await _viewModel.History.ShowAllAsync();
        UpdateHistoryScopePresentation();

        if (_viewModel.History.SelectedRow is { } selected)
        {
            HistoryList.SelectedItem = selected;
            HistoryList.ScrollIntoView(selected, ScrollIntoViewAlignment.Leading);
        }
    }

    private void ShowWorkingTree()
    {
        HistoryPane.Visibility = Visibility.Collapsed;
        WorkingTreePane.Visibility = Visibility.Visible;
        UpdateCommitNavigationText();
        if (_unstagedChanges.FirstOrDefault() is { } unstaged)
        {
            UnstagedChangesList.SelectedItem = unstaged;
            SelectWorkingTreeChange(unstaged, WorkingTreeDiffKind.Unstaged);
        }
        else if (_stagedChanges.FirstOrDefault() is { } staged)
        {
            StagedChangesList.SelectedItem = staged;
            SelectWorkingTreeChange(staged, WorkingTreeDiffKind.Staged);
        }
    }

    private void UpdateCommitNavigationText() =>
        RepositoryToolbar.SetCommitNavigationText(
            WorkingTreePane.Visibility == Visibility.Visible,
            _viewModel.WorkingTree.Changes.Count);

    private void RepositoryTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (ResolveNode(args.InvokedItem) is not { } node) return;

        RepositoryTree.SelectedItem = node;
        var version = _repositoryTreeNavigation.BeginInteraction();
        if (node.Kind is RepositoryTreeNodeKind.LocalBranch
            or RepositoryTreeNodeKind.RemoteBranch
            or RepositoryTreeNodeKind.Tag
            or RepositoryTreeNodeKind.Stash
            or RepositoryTreeNodeKind.Group)
            _viewModel.History.Invalidate();

        RunPageUiOperation(
            () => SelectRepositoryNodeAsync(node, version), "Repository tree navigation");
    }

    private async Task SelectRepositoryNodeAsync(RepositoryTreeNode node, long interactionVersion)
    {
        if (interactionVersion != _repositoryTreeNavigation.Version) return;

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
                if (interactionVersion != _repositoryTreeNavigation.Version) return;
                await NavigateToReferenceAsync(
                    stash.Commit, preserveSelectedStash: true, treeInteractionVersion: interactionVersion);
                UpdateStashPresentation();
                break;
            case RepositoryTreeNodeKind.Group:
                await ShowAllHistoryAsync();
                break;
        }
    }

    private void RepositoryTree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (IsShuttingDown) return;
        var node = ResolveNode((args.OriginalSource as FrameworkElement)?.DataContext);
        if (node?.Kind != RepositoryTreeNodeKind.LocalBranch || node.Value is not GitBranch branch) return;

        var version = _repositoryTreeNavigation.BeginInteraction();
        _viewModel.History.Invalidate();
        args.Handled = true;
        RunPageUiOperation(() => SwitchRepositoryTreeBranchAsync(branch.Name, version), "Switch branch");
    }

    private void RepositoryTree_RightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as FrameworkElement;
        var node = ResolveNode(source?.DataContext);
        if (source is null || node is null) return;

        var flyout = new MenuFlyout();
        switch (node.Kind)
        {
            case RepositoryTreeNodeKind.LocalBranch when node.Value is GitBranch branch:
                AddMenuItem(flyout, "Switch / Checkout", () => !branch.IsCurrent && _viewModel.CanStartUserGitOperation, async () =>
                {
                    _viewModel.Branches.SelectedLocalBranch = branch;
                    await ExecuteCommandAsync(_viewModel.Branches.SwitchBranchCommand);
                });
                AddMenuItem(flyout, "Create branch from here…", () => _viewModel.CanStartUserGitOperation, () => CreateBranchFromAsync(branch.Name));
                AddMenuItem(flyout, "Merge into current branch", () => !branch.IsCurrent && _viewModel.CanStartUserGitOperation, async () =>
                {
                    _viewModel.RepositoryOperations.SelectedMergeBranch = branch;
                    await ExecuteCommandAsync(_viewModel.RepositoryOperations.MergeCommand);
                });
                AddMenuItem(
                    flyout,
                    "Delete",
                    () => !branch.IsCurrent && _viewModel.CanStartUserGitOperation,
                    () => ConfirmDeleteLocalBranchAsync(branch));
                flyout.Items.Add(new MenuFlyoutSeparator());
                AddMenuItem(flyout, "Show branch history only", () => _viewModel.CanStartUserGitOperation,
                    () => ShowReferenceHistoryAsync(branch.Name, $"Branch: {branch.Name}"));
                AddMenuItem(flyout, "Copy branch name", true, () => CopyTextAsync(branch.Name));
                break;

            case RepositoryTreeNodeKind.Group when node.Name == "Remotes":
                AddMenuItem(flyout, "Fetch all", () => _viewModel.CanStartUserGitOperation, async () => await ExecuteCommandAsync(_viewModel.RepositorySync.FetchAllCommand));
                break;

            case RepositoryTreeNodeKind.Remote when node.Value is GitRemote remote:
                AddMenuItem(flyout, "Fetch", () => _viewModel.CanStartUserGitOperation, async () =>
                {
                    _viewModel.RepositorySync.SelectedRemote = remote;
                    await ExecuteCommandAsync(_viewModel.RepositorySync.FetchCommand);
                });
                break;

            case RepositoryTreeNodeKind.RemoteBranch when node.Value is GitBranch remoteBranch:
                AddMenuItem(flyout, "Checkout as tracking branch", () => _viewModel.CanStartUserGitOperation, async () =>
                {
                    _viewModel.Branches.SelectedRemoteBranch = remoteBranch;
                    var slash = remoteBranch.Name.IndexOf('/');
                    _viewModel.Branches.NewBranchName = slash >= 0 ? remoteBranch.Name[(slash + 1)..] : remoteBranch.Name;
                    await ExecuteCommandAsync(_viewModel.Branches.CheckoutRemoteCommand);
                });
                AddMenuItem(flyout, "Show branch history only", () => _viewModel.CanStartUserGitOperation,
                    () => ShowReferenceHistoryAsync(remoteBranch.Name, $"Remote: {remoteBranch.Name}"));
                AddMenuItem(flyout, "Copy branch name", true, () => CopyTextAsync(remoteBranch.Name));
                break;

            case RepositoryTreeNodeKind.Tag when node.Value is GitTag tag:
                AddMenuItem(flyout, "Checkout detached", () => _viewModel.CanStartUserGitOperation, async () =>
                {
                    _viewModel.Tags.SelectedTag = tag;
                    await ExecuteCommandAsync(_viewModel.Tags.CheckoutTagCommand);
                });
                AddMenuItem(flyout, "Copy tag name", true, () => CopyTextAsync(tag.Name));
                break;

            case RepositoryTreeNodeKind.Group
                when string.Equals(node.Key, RepositoryTreeDescriptorBuilder.StashesRootKey, StringComparison.Ordinal):
                AddMenuItem(flyout, "Stash…", () => _viewModel.Stashes.CanCreateStash, ShowCreateStashDialogAsync);
                break;

            case RepositoryTreeNodeKind.Stash when node.Value is GitStash stash:
                AddMenuItem(flyout, "Apply", () => _viewModel.Stashes.CanMutateStash(stash), async () =>
                {
                    await _viewModel.Stashes.SelectStashAsync(stash);
                    await ExecuteCommandAsync(_viewModel.Stashes.ApplyCommand);
                });
                AddMenuItem(flyout, "Pop", () => _viewModel.Stashes.CanMutateStash(stash), async () =>
                {
                    await _viewModel.Stashes.SelectStashAsync(stash);
                    await ExecuteCommandAsync(_viewModel.Stashes.PopCommand);
                });
                flyout.Items.Add(new MenuFlyoutSeparator());
                AddMenuItem(flyout, "Drop…", () => _viewModel.Stashes.CanMutateStash(stash), async () =>
                {
                    await _viewModel.Stashes.SelectStashAsync(stash);
                    await ConfirmDropStashAsync(stash);
                });
                break;
        }

        if (flyout.Items.Count == 0) return;
        flyout.ShowAt(source, args.GetPosition(source));
        args.Handled = true;
    }

    private void UpdateStashPresentation()
    {
        if (_repositoryFilesTab is not null)
            _repositoryFilesTab.Header = _viewModel.Stashes.HasSelectedStash
                ? "Tracked files"
                : "Files";
    }

    private static RepositoryTreeNode? ResolveNode(object? value) => value switch
    {
        RepositoryTreeNode node => node,
        TreeViewNode { Content: RepositoryTreeNode node } => node,
        TreeViewItem { DataContext: RepositoryTreeNode node } => node,
        FrameworkElement { DataContext: RepositoryTreeNode node } => node,
        _ => null
    };

    private void AddMenuItem(MenuFlyout flyout, string text, bool enabled, Func<Task> action) =>
        AddMenuItem(flyout, text, () => enabled, action);

    // Capture live availability for menu items that can remain open across Working/Ready.
    private void AddMenuItem(MenuFlyout flyout, string text, Func<bool> canExecute, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = canExecute() };
        void UpdateAvailability(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(OpenRepositoryViewModel.IsBusy)
                or nameof(OpenRepositoryViewModel.Repository)
                or nameof(OpenRepositoryViewModel.CanChangeRepository))
                item.IsEnabled = canExecute();
        }

        void Detach(object? sender, object args)
        {
            _viewModel.PropertyChanged -= UpdateAvailability;
            flyout.Closed -= Detach;
        }

        _viewModel.PropertyChanged += UpdateAvailability;
        flyout.Closed += Detach;
        item.Click += (_, _) => RunPageUiOperation(action, "Menu action", showError: true);
        flyout.Items.Add(item);
    }

}
