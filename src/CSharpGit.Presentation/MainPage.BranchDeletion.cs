using CSharpGit.Application;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private void RepositoryTreeBranchDeletion_RightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as FrameworkElement;
        var node = ResolveNode(source?.DataContext);
        if (source is null || node is null)
        {
            return;
        }

        if (TryShowTagContextMenu(source, args)) return;

        var flyout = new MenuFlyout();
        switch (node.Kind)
        {
            case RepositoryTreeNodeKind.LocalBranch when node.Value is GitBranch branch:
            {
                var branchWorktree = _worktrees.FindWorktreeForBranch(branch.Name);
                if (branchWorktree is { IsCurrent: false })
                {
                    AddMenuItem(flyout, "Open Worktree", !_worktreesViewModel.IsBusy, () => _worktrees.OpenWorktreeAsync(branchWorktree));
                    AddMenuItem(flyout, "Open Worktree Folder", !_worktreesViewModel.IsBusy, () => _worktrees.OpenWorktreeFolderAsync(branchWorktree));
                }
                else
                {
                    AddMenuItem(flyout, "Switch / Checkout", !branch.IsCurrent && !_viewModel.IsBusy, async () =>
                    {
                        _viewModel.Branches.SelectedLocalBranch = branch;
                        await ExecuteCommandAsync(_viewModel.Branches.SwitchBranchCommand);
                    });
                    if (branchWorktree is null)
                        AddMenuItem(flyout, "Open in New Worktree…", _worktreesViewModel.CanMutate, () => _worktrees.CreateWorktreeFromBranchAsync(branch));
                }

                AddMenuItem(flyout, "Create branch from here…", !_viewModel.IsBusy, () => CreateBranchFromAsync(branch.Name));
                AddMenuItem(flyout, "Rename…", CanRenameBranch(), () => RenameBranchAsync(branch));
                if (!string.IsNullOrWhiteSpace(branch.Upstream))
                    AddMenuItem(flyout, "Unset upstream", _viewModel.Branches.CanMutate,
                        () => UnsetBranchUpstreamAsync(branch));
                AddMenuItem(flyout, "Merge into current branch", !branch.IsCurrent && !_viewModel.IsBusy, async () =>
                {
                    _viewModel.RepositoryOperations.SelectedMergeBranch = branch;
                    await ExecuteCommandAsync(_viewModel.RepositoryOperations.MergeCommand);
                });
                AddMenuItem(
                    flyout,
                    "Delete",
                    !branch.IsCurrent && branchWorktree is null && !_viewModel.IsBusy,
                    () => _branchDeletion.ConfirmDeleteLocalAsync(branch));
                flyout.Items.Add(new MenuFlyoutSeparator());
                AddMenuItem(flyout, "Show branch history only", !_viewModel.IsBusy,
                    () => ShowReferenceHistoryAsync(branch.Name, $"Branch: {branch.Name}"));
                AddMenuItem(flyout, "Copy branch name", true, () => CopyTextAsync(branch.Name));
                break;
            }

            case RepositoryTreeNodeKind.BranchFolder when node.Value is BranchFolderInfo folderInfo:
                AddMenuItem(
                    flyout,
                    folderInfo.Scope == BranchFolderScope.Local
                        ? "Delete all branches in this folder…"
                        : "Delete all remote branches in this folder…",
                    !_viewModel.IsBusy,
                    () => _branchDeletion.ConfirmDeleteAsync(node, folderInfo));
                break;

            case RepositoryTreeNodeKind.Worktree when node.Value is WorktreeInfo worktree:
                _worktrees.PopulateWorktreeMenu(flyout, worktree);
                break;

            case RepositoryTreeNodeKind.Group when node.Name == "Worktrees":
                AddMenuItem(flyout, "New Worktree…", _worktreesViewModel.CanMutate, _worktrees.CreateNewWorktreeAsync);
                flyout.Items.Add(new MenuFlyoutSeparator());
                AddMenuItem(flyout, "Prune Worktrees", _worktreesViewModel.CanMutate, _worktrees.PruneWorktreesAsync);
                break;

            case RepositoryTreeNodeKind.Remote when node.Value is GitRemote remote:
                AddMenuItem(flyout, "Fetch", !_viewModel.IsBusy, async () =>
                {
                    _viewModel.RepositorySync.SelectedRemote = remote;
                    await ExecuteCommandAsync(_viewModel.RepositorySync.FetchCommand);
                });
                flyout.Items.Add(new MenuFlyoutSeparator());
                AddMenuItem(flyout, "Remove remote…", !_viewModel.IsBusy, () => ConfirmRemoveRemoteAsync(remote));
                break;

            case RepositoryTreeNodeKind.RemoteBranch when node.Value is GitBranch remoteBranch:
                AddMenuItem(flyout, "Checkout as tracking branch", !_viewModel.IsBusy, async () =>
                {
                    _viewModel.Branches.SelectedRemoteBranch = remoteBranch;
                    var target = _viewModel.Branches.ResolveRemoteDeletionTarget(remoteBranch);
                    _viewModel.Branches.NewBranchName = target.BranchName;
                    await ExecuteCommandAsync(_viewModel.Branches.CheckoutRemoteCommand);
                });
                AddMenuItem(flyout, "Delete", !_viewModel.IsBusy, () => _branchDeletion.ConfirmDeleteRemoteAsync(remoteBranch));
                flyout.Items.Add(new MenuFlyoutSeparator());
                AddMenuItem(flyout, "Show branch history only", !_viewModel.IsBusy,
                    () => ShowReferenceHistoryAsync(remoteBranch.Name, $"Remote: {remoteBranch.Name}"));
                AddMenuItem(flyout, "Copy branch name", true, () => CopyTextAsync(remoteBranch.Name));
                break;

            default:
                RepositoryTree_RightTapped(sender, args);
                return;
        }

        flyout.ShowAt(source, args.GetPosition(source));
        args.Handled = true;
    }

    private async Task UnsetBranchUpstreamAsync(GitBranch branch)
    {
        var repository = _viewModel.Repository;
        if (repository is null || IsShuttingDown || !_viewModel.Branches.CanMutate) return;
        await _viewModel.Branches.UnsetUpstreamAsync(repository, branch);
    }

    private async Task ConfirmRemoveRemoteAsync(GitRemote remote)
    {
        var repository = _viewModel.Repository;
        if (repository is null || IsShuttingDown || _viewModel.IsBusy) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Remove remote '{remote.Name}'?",
            Content = "This removes the local remote configuration and its remote-tracking references. Local branches, commits, and the remote server are not deleted. Branches tracking this remote will no longer have a usable upstream.",
            PrimaryButtonText = "Remove remote",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await ShowPageDialogAsync(dialog) != ContentDialogResult.Primary
            || IsShuttingDown || !ReferenceEquals(repository, _viewModel.Repository)
            || _viewModel.IsBusy) return;

        await _viewModel.RepositorySync.RemoveRemoteAsync(repository, remote);
    }

    private async Task ConfirmCheckoutTagDetachedAsync(GitTag tag)
    {
        var originalRepository = _viewModel.Repository;
        if (originalRepository is null || _viewModel.IsBusy)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Checkout detached HEAD?",
            Content = $"Checkout tag '{tag.Name}' in detached HEAD state? New commits will not belong to a branch until you create or switch to one.",
            PrimaryButtonText = "Checkout detached",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await ShowPageDialogAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        if (IsShuttingDown || !ReferenceEquals(originalRepository, _viewModel.Repository)
            || _viewModel.IsBusy) return;
        _viewModel.Tags.SelectedTag = tag;
        await ExecuteCommandAsync(_viewModel.Tags.CheckoutTagCommand);
    }

}
