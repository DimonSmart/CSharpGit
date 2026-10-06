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
                var branchWorktree = FindWorktreeForBranch(branch.Name);
                if (branchWorktree is { IsCurrent: false })
                {
                    AddMenuItem(flyout, "Open Worktree", !_worktreesViewModel.IsBusy, () => OpenWorktreeAsync(branchWorktree));
                    AddMenuItem(flyout, "Open Worktree Folder", !_worktreesViewModel.IsBusy, () => OpenWorktreeFolderAsync(branchWorktree));
                }
                else
                {
                    AddMenuItem(flyout, "Switch / Checkout", !branch.IsCurrent && !_viewModel.IsBusy, async () =>
                    {
                        _viewModel.Branches.SelectedLocalBranch = branch;
                        await ExecuteCommandAsync(_viewModel.Branches.SwitchBranchCommand);
                    });
                    if (branchWorktree is null)
                        AddMenuItem(flyout, "Open in New Worktree…", _worktreesViewModel.CanMutate, () => CreateWorktreeFromBranchAsync(branch));
                }

                AddMenuItem(flyout, "Create branch from here…", !_viewModel.IsBusy, () => CreateBranchFromAsync(branch.Name));
                AddMenuItem(flyout, "Rename…", CanRenameBranch(), () => RenameBranchAsync(branch));
                AddMenuItem(flyout, "Merge into current branch", !branch.IsCurrent && !_viewModel.IsBusy, async () =>
                {
                    _viewModel.SelectedMergeBranch = branch;
                    await ExecuteCommandAsync(_viewModel.MergeCommand);
                });
                AddMenuItem(
                    flyout,
                    "Delete",
                    !branch.IsCurrent && branchWorktree is null && !_viewModel.IsBusy,
                    () => ConfirmDeleteLocalBranchAsync(branch));
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
                    () => ConfirmDeleteBranchFolderAsync(node, folderInfo));
                break;

            case RepositoryTreeNodeKind.Worktree when node.Value is WorktreeInfo worktree:
                PopulateWorktreeMenu(flyout, worktree);
                break;

            case RepositoryTreeNodeKind.Group when node.Name == "Worktrees":
                AddMenuItem(flyout, "New Worktree…", _worktreesViewModel.CanMutate, CreateNewWorktreeAsync);
                flyout.Items.Add(new MenuFlyoutSeparator());
                AddMenuItem(flyout, "Prune Worktrees", _worktreesViewModel.CanMutate, PruneWorktreesAsync);
                break;

            case RepositoryTreeNodeKind.RemoteBranch when node.Value is GitBranch remoteBranch:
                AddMenuItem(flyout, "Checkout as tracking branch", !_viewModel.IsBusy, async () =>
                {
                    _viewModel.Branches.SelectedRemoteBranch = remoteBranch;
                    var target = _viewModel.Branches.ResolveRemoteDeletionTarget(remoteBranch);
                    _viewModel.Branches.NewBranchName = target.BranchName;
                    await ExecuteCommandAsync(_viewModel.Branches.CheckoutRemoteCommand);
                });
                AddMenuItem(flyout, "Delete", !_viewModel.IsBusy, () => ConfirmDeleteRemoteBranchAsync(remoteBranch));
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

    private async Task ConfirmCheckoutTagDetachedAsync(GitTag tag)
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy)
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

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _viewModel.SelectedTag = tag;
        await ExecuteCommandAsync(_viewModel.CheckoutTagCommand);
    }

    private async Task ConfirmDeleteLocalBranchAsync(GitBranch branch)
    {
        if (_viewModel.Repository is null || branch.IsCurrent || _viewModel.IsBusy)
        {
            return;
        }

        var remoteTarget = _viewModel.Branches.ResolveRemoteDeletionTargetForLocal(branch);

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = $"Delete local branch '{branch.Name}'?",
            TextWrapping = TextWrapping.Wrap
        });

        var forceDeleteCheckBox = new CheckBox
        {
            Content = "Force delete even if the branch is not fully merged",
            IsChecked = false
        };
        content.Children.Add(forceDeleteCheckBox);
        content.Children.Add(new TextBlock
        {
            Text = "Force delete may remove the only branch reference to commits. Those commits may become difficult to recover.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });

        CheckBox? deleteRemoteCheckBox = null;
        if (remoteTarget is not null)
        {
            deleteRemoteCheckBox = new CheckBox
            {
                Content = $"Also delete remote branch '{remoteTarget.Remote.Name}/{remoteTarget.BranchName}'",
                IsChecked = false
            };
            content.Children.Add(deleteRemoteCheckBox);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete local branch?",
            Content = content,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var repository = _viewModel.Repository;
        if (repository is null) return;

        var deletionMode = forceDeleteCheckBox.IsChecked == true
            ? BranchDeletionMode.Force
            : BranchDeletionMode.Safe;
        var deleteRemote = deleteRemoteCheckBox?.IsChecked == true && remoteTarget is not null;

        var result = await _viewModel.Branches.DeleteLocalBranchAsync(
            repository,
            branch,
            deletionMode,
            deleteRemote,
            reference =>
            {
                if (string.Equals(_viewModel.History.ActiveReference, reference, StringComparison.Ordinal))
                    _viewModel.History.ResetForRepositoryMutation();
            });

        if (result.LocalDeleted
            && deleteRemote
            && !result.RemoteDeleted
            && result.SecondaryFailureMessage is { } remoteFailure
            && remoteTarget is not null)
        {
            await ShowErrorAsync(
                "Local branch deleted; remote branch retained",
                $"Local branch '{branch.Name}' was deleted, but remote branch '{remoteTarget.Remote.Name}/{remoteTarget.BranchName}' could not be deleted.\n\nGit: {remoteFailure}");
        }
    }

    private async Task ConfirmDeleteRemoteBranchAsync(GitBranch remoteBranch)
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy)
        {
            return;
        }

        RemoteBranchDeletionTarget target;
        try
        {
            target = _viewModel.Branches.ResolveRemoteDeletionTarget(remoteBranch);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not delete remote branch", exception.Message);
            return;
        }

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = $"Delete remote branch '{remoteBranch.Name}'?",
            TextWrapping = TextWrapping.Wrap
        });

        CheckBox? deleteLocalCheckBox = null;
        CheckBox? forceDeleteLocalCheckBox = null;
        if (target.LocalBranch is { } localBranch)
        {
            deleteLocalCheckBox = new CheckBox
            {
                Content = $"Also delete local branch '{localBranch.Name}'",
                IsChecked = false,
                IsEnabled = !localBranch.IsCurrent
            };
            content.Children.Add(deleteLocalCheckBox);

            var forceCheckBox = new CheckBox
            {
                Content = "Force delete local branch even if it is not fully merged",
                IsChecked = false,
                IsEnabled = false
            };
            forceDeleteLocalCheckBox = forceCheckBox;
            deleteLocalCheckBox.Checked += (_, _) => forceCheckBox.IsEnabled = true;
            deleteLocalCheckBox.Unchecked += (_, _) =>
            {
                forceCheckBox.IsChecked = false;
                forceCheckBox.IsEnabled = false;
            };
            content.Children.Add(forceCheckBox);
            content.Children.Add(new TextBlock
            {
                Text = "Force delete may remove the only branch reference to commits. Those commits may become difficult to recover.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.72
            });

            if (localBranch.IsCurrent)
            {
                content.Children.Add(new TextBlock
                {
                    Text = "The local branch is currently checked out and cannot be deleted.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.72
                });
            }
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete remote branch?",
            Content = content,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var repository = _viewModel.Repository;
        if (repository is null) return;

        _viewModel.Branches.SelectedRemoteBranch = remoteBranch;
        var deleteLocal = deleteLocalCheckBox?.IsChecked == true && target.LocalBranch is { IsCurrent: false };
        var forceDeleteLocal = deleteLocal && forceDeleteLocalCheckBox?.IsChecked == true;
        var localDeletionMode = forceDeleteLocal
            ? BranchDeletionMode.Force
            : BranchDeletionMode.Safe;

        var result = await _viewModel.Branches.DeleteRemoteBranchAsync(
            repository,
            remoteBranch,
            target,
            deleteLocal,
            localDeletionMode,
            reference =>
            {
                if (string.Equals(_viewModel.History.ActiveReference, reference, StringComparison.Ordinal))
                    _viewModel.History.ResetForRepositoryMutation();
            });

        if (result.RemoteDeleted
            && deleteLocal
            && !result.LocalDeleted
            && result.SecondaryFailureMessage is { } localFailure
            && target.LocalBranch is { } retainedLocalBranch)
        {
            await ShowErrorAsync(
                "Remote branch deleted; local branch retained",
                $"Remote branch '{remoteBranch.Name}' was deleted, but local branch '{retainedLocalBranch.Name}' could not be deleted.\n\nGit: {localFailure}");
        }
    }

}
