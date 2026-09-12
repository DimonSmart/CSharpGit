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

        var flyout = new MenuFlyout();
        switch (node.Kind)
        {
            case RepositoryTreeNodeKind.LocalBranch when node.Value is GitBranch branch:
                AddMenuItem(flyout, "Switch / Checkout", !branch.IsCurrent && !_viewModel.IsBusy, async () =>
                {
                    _viewModel.SelectedLocalBranch = branch;
                    await ExecuteCommandAsync(_viewModel.SwitchBranchCommand);
                });
                AddMenuItem(flyout, "Create branch from here…", !_viewModel.IsBusy, () => CreateBranchFromAsync(branch.Name));
                AddMenuItem(flyout, "Merge into current branch", !branch.IsCurrent && !_viewModel.IsBusy, async () =>
                {
                    _viewModel.SelectedMergeBranch = branch;
                    await ExecuteCommandAsync(_viewModel.MergeCommand);
                });
                AddMenuItem(flyout, "Delete", !branch.IsCurrent && !_viewModel.IsBusy, () => ConfirmDeleteLocalBranchAsync(branch));
                flyout.Items.Add(new MenuFlyoutSeparator());
                AddMenuItem(flyout, "Show branch history only", !_viewModel.IsBusy,
                    () => ShowReferenceHistoryAsync(branch.Name, $"Branch: {branch.Name}"));
                AddMenuItem(flyout, "Copy branch name", true, () => CopyTextAsync(branch.Name));
                break;

            case RepositoryTreeNodeKind.RemoteBranch when node.Value is GitBranch remoteBranch:
                AddMenuItem(flyout, "Checkout as tracking branch", !_viewModel.IsBusy, async () =>
                {
                    _viewModel.SelectedRemoteBranch = remoteBranch;
                    var target = BranchDeletionResolver.Resolve(remoteBranch, _viewModel.LocalBranches, _viewModel.Remotes);
                    _viewModel.NewBranchName = target.BranchName;
                    await ExecuteCommandAsync(_viewModel.CheckoutRemoteCommand);
                });
                AddMenuItem(flyout, "Delete", !_viewModel.IsBusy, () => ConfirmDeleteRemoteBranchAsync(remoteBranch));
                flyout.Items.Add(new MenuFlyoutSeparator());
                AddMenuItem(flyout, "Show branch history only", !_viewModel.IsBusy,
                    () => ShowReferenceHistoryAsync(remoteBranch.Name, $"Remote: {remoteBranch.Name}"));
                AddMenuItem(flyout, "Copy branch name", true, () => CopyTextAsync(remoteBranch.Name));
                break;

            case RepositoryTreeNodeKind.Tag when node.Value is GitTag tag:
                AddMenuItem(flyout, "Checkout detached", !_viewModel.IsBusy, () => ConfirmCheckoutTagDetachedAsync(tag));
                AddMenuItem(flyout, "Copy tag name", true, () => CopyTextAsync(tag.Name));
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

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete local branch?",
            Content = $"Delete local branch '{branch.Name}'?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _viewModel.SelectedLocalBranch = branch;
        await _viewModel.RunMutationAsync(
            async () =>
            {
                await _referenceService.DeleteBranchAsync(_viewModel.Repository!, branch.Name);
                if (string.Equals(_activeReference, branch.Name, StringComparison.Ordinal))
                {
                    ShowAllHistory();
                }
            },
            "Could not delete local branch");
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
            target = BranchDeletionResolver.Resolve(remoteBranch, _viewModel.LocalBranches, _viewModel.Remotes);
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
        if (target.LocalBranch is { } localBranch)
        {
            deleteLocalCheckBox = new CheckBox
            {
                Content = $"Also delete local branch '{localBranch.Name}'",
                IsChecked = false,
                IsEnabled = !localBranch.IsCurrent
            };
            content.Children.Add(deleteLocalCheckBox);

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

        _viewModel.SelectedRemoteBranch = remoteBranch;
        var deleteLocal = deleteLocalCheckBox?.IsChecked == true && target.LocalBranch is { IsCurrent: false };
        Exception? localFailure = null;

        await _viewModel.RunMutationAsync(
            async () =>
            {
                await _referenceService.DeleteRemoteBranchAsync(
                    _viewModel.Repository!,
                    target.Remote.Name,
                    target.BranchName);

                _viewModel.SelectedRemoteBranch = null;
                if (string.Equals(_activeReference, remoteBranch.Name, StringComparison.Ordinal))
                {
                    ShowAllHistory();
                }

                if (!deleteLocal || target.LocalBranch is null)
                {
                    return;
                }

                try
                {
                    await _referenceService.DeleteBranchAsync(_viewModel.Repository!, target.LocalBranch.Name);
                    if (string.Equals(_activeReference, target.LocalBranch.Name, StringComparison.Ordinal))
                    {
                        ShowAllHistory();
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    localFailure = exception;
                }
            },
            "Could not delete remote branch");

        if (localFailure is not null && target.LocalBranch is { } retainedLocalBranch)
        {
            await ShowErrorAsync(
                "Remote branch deleted; local branch retained",
                $"Remote branch '{remoteBranch.Name}' was deleted, but local branch '{retainedLocalBranch.Name}' could not be deleted.\n\nGit: {localFailure.Message}");
        }
    }
}
