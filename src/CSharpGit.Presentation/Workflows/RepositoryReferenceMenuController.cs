using System.ComponentModel;
using System.Windows.Input;
using CSharpGit.Application;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation.Workflows;

// Owns repository-reference context menus. Mutation semantics stay in existing ViewModels/workflows.
internal sealed class RepositoryReferenceMenuController(
    OpenRepositoryViewModel viewModel,
    WorktreesViewModel worktreesViewModel,
    WorktreeController worktrees,
    BranchRenameController branchRename,
    BranchDeletionController branchDeletion,
    TagWorkflowController tags,
    StashDialogController stashes,
    CommitActionController commitActions,
    Func<string, string, Task> showReferenceHistory,
    Func<string, Task> copyText,
    Func<ICommand, Task> executeCommand,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Func<bool> isShuttingDown,
    Action<Func<Task>, string> runUiOperation)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly WorktreesViewModel _worktreesViewModel = worktreesViewModel;
    private readonly WorktreeController _worktrees = worktrees;
    private readonly BranchRenameController _branchRename = branchRename;
    private readonly BranchDeletionController _branchDeletion = branchDeletion;
    private readonly TagWorkflowController _tags = tags;
    private readonly StashDialogController _stashes = stashes;
    private readonly CommitActionController _commitActions = commitActions;
    private readonly Func<string, string, Task> _showReferenceHistory = showReferenceHistory;
    private readonly Func<string, Task> _copyText = copyText;
    private readonly Func<ICommand, Task> _executeCommand = executeCommand;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;

    internal void Show(object sender, RightTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as FrameworkElement;
        var node = RepositoryExplorerView.ResolveNode(source?.DataContext);
        if (source is null || node is null) return;

        var flyout = new MenuFlyout();
        switch (node.Kind)
        {
            case RepositoryTreeNodeKind.LocalBranch when node.Value is GitBranch branch:
                PopulateLocalBranch(flyout, branch);
                break;
            case RepositoryTreeNodeKind.BranchFolder when node.Value is BranchFolderInfo folderInfo:
                AddMenuItem(
                    flyout,
                    folderInfo.Scope == BranchFolderScope.Local
                        ? "Delete all branches in this folder…"
                        : "Delete all remote branches in this folder…",
                    () => !_viewModel.IsBusy,
                    () => _branchDeletion.ConfirmDeleteAsync(node, folderInfo));
                break;
            case RepositoryTreeNodeKind.Worktree when node.Value is WorktreeInfo worktree:
                _worktrees.PopulateWorktreeMenu(flyout, worktree);
                break;
            case RepositoryTreeNodeKind.Group when node.Name == "Worktrees":
                AddMenuItem(flyout, "New Worktree…", () => _worktreesViewModel.CanMutate, _worktrees.CreateNewWorktreeAsync);
                flyout.Items.Add(new MenuFlyoutSeparator());
                AddMenuItem(flyout, "Prune Worktrees", () => _worktreesViewModel.CanMutate, _worktrees.PruneWorktreesAsync);
                break;
            case RepositoryTreeNodeKind.Remote when node.Value is GitRemote remote:
                PopulateRemote(flyout, remote);
                break;
            case RepositoryTreeNodeKind.RemoteBranch when node.Value is GitBranch remoteBranch:
                PopulateRemoteBranch(flyout, remoteBranch);
                break;
            case RepositoryTreeNodeKind.Group when node.Name == "Tags":
                PopulateTagsGroup(flyout);
                break;
            case RepositoryTreeNodeKind.Tag when node.Value is GitTag tag:
                PopulateTag(flyout, tag);
                break;
            case RepositoryTreeNodeKind.Group when node.Name == "Remotes":
                AddMenuItem(flyout, "Fetch all", () => _viewModel.CanStartUserGitOperation,
                    () => _executeCommand(_viewModel.RepositorySync.FetchAllCommand));
                break;
            case RepositoryTreeNodeKind.Group
                when string.Equals(node.Key, RepositoryTreeDescriptorBuilder.StashesRootKey, StringComparison.Ordinal):
                AddMenuItem(flyout, "Stash…", () => _viewModel.Stashes.CanCreateStash, _stashes.ShowCreateAsync);
                break;
            case RepositoryTreeNodeKind.Stash when node.Value is GitStash stash:
                PopulateStash(flyout, stash);
                break;
        }

        if (flyout.Items.Count == 0) return;
        flyout.ShowAt(source, args.GetPosition(source));
        args.Handled = true;
    }

    private void PopulateLocalBranch(MenuFlyout flyout, GitBranch branch)
    {
        var branchWorktree = _worktrees.FindWorktreeForBranch(branch.Name);
        if (branchWorktree is { IsCurrent: false })
        {
            AddMenuItem(flyout, "Open Worktree", () => !_worktreesViewModel.IsBusy,
                () => _worktrees.OpenWorktreeAsync(branchWorktree));
            AddMenuItem(flyout, "Open Worktree Folder", () => !_worktreesViewModel.IsBusy,
                () => _worktrees.OpenWorktreeFolderAsync(branchWorktree));
        }
        else
        {
            AddMenuItem(flyout, "Switch / Checkout", () => !branch.IsCurrent && !_viewModel.IsBusy, async () =>
            {
                _viewModel.Branches.SelectedLocalBranch = branch;
                await _executeCommand(_viewModel.Branches.SwitchBranchCommand);
            });
            if (branchWorktree is null)
                AddMenuItem(flyout, "Open in New Worktree…", () => _worktreesViewModel.CanMutate,
                    () => _worktrees.CreateWorktreeFromBranchAsync(branch));
        }

        AddMenuItem(flyout, "Create branch from here…", () => !_viewModel.IsBusy,
            () => _commitActions.CreateBranchFromReferenceAsync(branch.Name));
        AddMenuItem(flyout, "Rename…", _branchRename.CanRenameBranch,
            () => _branchRename.RenameAsync(branch));
        if (!string.IsNullOrWhiteSpace(branch.Upstream))
            AddMenuItem(flyout, "Unset upstream", () => _viewModel.Branches.CanMutate,
                () => UnsetBranchUpstreamAsync(branch));
        AddMenuItem(flyout, "Merge into current branch", () => !branch.IsCurrent && !_viewModel.IsBusy, async () =>
        {
            _viewModel.RepositoryOperations.SelectedMergeBranch = branch;
            await _executeCommand(_viewModel.RepositoryOperations.MergeCommand);
        });
        AddMenuItem(flyout, "Delete",
            () => !branch.IsCurrent && branchWorktree is null && !_viewModel.IsBusy,
            () => _branchDeletion.ConfirmDeleteLocalAsync(branch));
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(flyout, "Show branch history only", () => !_viewModel.IsBusy,
            () => _showReferenceHistory(branch.Name, $"Branch: {branch.Name}"));
        AddMenuItem(flyout, "Copy branch name", () => true, () => _copyText(branch.Name));
    }

    private void PopulateRemote(MenuFlyout flyout, GitRemote remote)
    {
        AddMenuItem(flyout, "Fetch", () => !_viewModel.IsBusy, async () =>
        {
            _viewModel.RepositorySync.SelectedRemote = remote;
            await _executeCommand(_viewModel.RepositorySync.FetchCommand);
        });
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(flyout, "Remove remote…", () => !_viewModel.IsBusy, () => ConfirmRemoveRemoteAsync(remote));
    }

    private void PopulateRemoteBranch(MenuFlyout flyout, GitBranch remoteBranch)
    {
        AddMenuItem(flyout, "Checkout as tracking branch", () => !_viewModel.IsBusy, async () =>
        {
            _viewModel.Branches.SelectedRemoteBranch = remoteBranch;
            var target = _viewModel.Branches.ResolveRemoteDeletionTarget(remoteBranch);
            _viewModel.Branches.NewBranchName = target.BranchName;
            await _executeCommand(_viewModel.Branches.CheckoutRemoteCommand);
        });
        AddMenuItem(flyout, "Delete", () => !_viewModel.IsBusy,
            () => _branchDeletion.ConfirmDeleteRemoteAsync(remoteBranch));
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(flyout, "Show branch history only", () => !_viewModel.IsBusy,
            () => _showReferenceHistory(remoteBranch.Name, $"Remote: {remoteBranch.Name}"));
        AddMenuItem(flyout, "Copy branch name", () => true, () => _copyText(remoteBranch.Name));
    }

    private void PopulateTagsGroup(MenuFlyout flyout)
    {
        AddMenuItem(flyout, "Create tag…", () => _viewModel.Tags.CanMutate,
            () => _tags.ShowCreateTagDialogAsync("HEAD", selectedCommit: false));
        AddMenuItem(flyout, "Fetch tags…", () => _viewModel.Tags.CanMutate, _tags.FetchTagsFromUiAsync);
        AddMenuItem(flyout, "Remote tags…", () => _viewModel.Tags.CanMutate, _tags.ShowRemoteTagsAsync);
        AddMenuItem(flyout, "Push all tags…", () => _viewModel.Tags.CanMutate, _tags.PushAllTagsFromUiAsync);
    }

    private void PopulateTag(MenuFlyout flyout, GitTag tag)
    {
        AddMenuItem(flyout, "Tag details…", () => true, () => _tags.ShowTagDetailsAsync(tag));
        AddMenuItem(flyout, "Show history up to tag", () => _viewModel.CanStartUserGitOperation,
            () => _showReferenceHistory($"refs/tags/{tag.Name}", $"Tag: {tag.Name}"));
        AddMenuItem(flyout, "Create branch from here…", () => _viewModel.Tags.CanMutate,
            () => _commitActions.CreateBranchFromReferenceAsync($"refs/tags/{tag.Name}", tag.TargetCommit));
        AddMenuItem(flyout, "Checkout detached", () => _viewModel.Tags.CanMutate,
            () => ConfirmCheckoutTagDetachedAsync(tag));
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(flyout, "Push tag…", () => _viewModel.Tags.CanMutate, () => _tags.PushTagFromUiAsync(tag));
        AddMenuItem(flyout, "Delete from remote…", () => _viewModel.Tags.CanMutate,
            () => _tags.DeleteRemoteTagFromUiAsync(tag));
        AddMenuItem(flyout, "Delete tag…", () => _viewModel.Tags.CanMutate, () => _tags.DeleteTagFromUiAsync(tag));
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(flyout, "Copy tag name", () => true, () => _copyText(tag.Name));
    }

    private void PopulateStash(MenuFlyout flyout, GitStash stash)
    {
        AddMenuItem(flyout, "Apply", () => _viewModel.Stashes.CanMutateStash(stash), async () =>
        {
            await _viewModel.Stashes.SelectStashAsync(stash);
            await _executeCommand(_viewModel.Stashes.ApplyCommand);
        });
        AddMenuItem(flyout, "Pop", () => _viewModel.Stashes.CanMutateStash(stash), async () =>
        {
            await _viewModel.Stashes.SelectStashAsync(stash);
            await _executeCommand(_viewModel.Stashes.PopCommand);
        });
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(flyout, "Drop…", () => _viewModel.Stashes.CanMutateStash(stash), async () =>
        {
            await _viewModel.Stashes.SelectStashAsync(stash);
            await _stashes.ConfirmDropAsync(stash);
        });
    }

    private async Task UnsetBranchUpstreamAsync(GitBranch branch)
    {
        var repository = _viewModel.Repository;
        if (repository is null || _isShuttingDown() || !_viewModel.Branches.CanMutate) return;
        await _viewModel.Branches.UnsetUpstreamAsync(repository, branch);
    }

    private async Task ConfirmRemoveRemoteAsync(GitRemote remote)
    {
        var repository = _viewModel.Repository;
        if (repository is null || _isShuttingDown() || _viewModel.IsBusy) return;
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = $"Remove remote '{remote.Name}'?",
            Content = "This removes the local remote configuration and its remote-tracking references. Local branches, commits, and the remote server are not deleted. Branches tracking this remote will no longer have a usable upstream.",
            PrimaryButtonText = "Remove remote",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await _showDialog(dialog) != ContentDialogResult.Primary
            || _isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository)
            || _viewModel.IsBusy) return;
        await _viewModel.RepositorySync.RemoveRemoteAsync(repository, remote);
    }

    private async Task ConfirmCheckoutTagDetachedAsync(GitTag tag)
    {
        var repository = _viewModel.Repository;
        if (repository is null || _viewModel.IsBusy) return;
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "Checkout detached HEAD?",
            Content = $"Checkout tag '{tag.Name}' in detached HEAD state? New commits will not belong to a branch until you create or switch to one.",
            PrimaryButtonText = "Checkout detached",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await _showDialog(dialog) != ContentDialogResult.Primary
            || _isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository)
            || _viewModel.IsBusy) return;
        _viewModel.Tags.SelectedTag = tag;
        await _executeCommand(_viewModel.Tags.CheckoutTagCommand);
    }

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
        item.Click += (_, _) => _runUiOperation(action, "Repository reference menu");
        flyout.Items.Add(item);
    }
}
