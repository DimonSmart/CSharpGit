using CSharpGit.Application;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using CSharpGit.Presentation;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Workflows;

// Owns local and remote branch deletion confirmations, including branch folders, and partial-failure reports.
internal sealed class BranchDeletionController(
    OpenRepositoryViewModel viewModel,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Func<bool> isShuttingDown,
    Func<string, string, Task> showError,
    Func<string, string, Task> showInformation)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Func<string, string, Task> _showError = showError;
    private readonly Func<string, string, Task> _showInformation = showInformation;
    private const double BranchFolderListMaxHeight = 360;

    internal async Task ConfirmDeleteAsync(
        RepositoryTreeNode folderNode,
        BranchFolderInfo folderInfo)
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy)
            return;

        if (folderInfo.Scope == BranchFolderScope.Local)
        {
            await ConfirmDeleteLocalBranchFolderAsync(folderNode, folderInfo);
            return;
        }

        await ConfirmDeleteRemoteBranchFolderAsync(folderNode, folderInfo);
    }

    private async Task ConfirmDeleteLocalBranchFolderAsync(
        RepositoryTreeNode folderNode,
        BranchFolderInfo folderInfo)
    {
        var originalRepository = _viewModel.Repository;
        if (originalRepository is null) return;
        var snapshot = CollectBranchFolderCandidates(folderNode, RepositoryTreeNodeKind.LocalBranch);
        var plan = _viewModel.Branches.CreateLocalFolderDeletionPlan(snapshot);
        if (plan.Attempted == 0)
        {
            await ShowNoEligibleLocalBranchesAsync(folderInfo, plan);
            return;
        }

        var content = new StackPanel { Spacing = UiStyles.Resolve<double>("Spacing.L") };
        content.Children.Add(new TextBlock
        {
            Text = $"{plan.Attempted} local branches will be attempted:",
            TextWrapping = TextWrapping.Wrap
        });

        var branchList = new StackPanel { Spacing = 4 };
        foreach (var candidate in plan.EligibleBranches)
            AddBranchLine(branchList, candidate.Branch.Name);

        if (plan.Skipped > 0)
        {
            branchList.Children.Add(new TextBlock
            {
                Text = $"{plan.Skipped} branches will be skipped:",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            foreach (var skipped in plan.SkippedBranches)
                AddBranchLine(branchList, $"{skipped.BranchName}    [{FormatSkipReason(skipped.Reason)}]");
        }
        content.Children.Add(CreateBoundedBranchList(branchList));

        var forceDeleteCheckBox = new CheckBox
        { Style = UiStyles.Resolve<Style>("CompactCheckBoxStyle"),
            Content = "Force delete branches even if they are not fully merged",
            IsChecked = false
        };
        content.Children.Add(forceDeleteCheckBox);
        content.Children.Add(new TextBlock
        {
            Text = "Force delete may remove the only branch reference to commits. Those commits may become difficult to recover.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });

        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = $"Delete branches in '{folderInfo.Prefix}'?",
            Content = content,
            PrimaryButtonText = $"Delete {plan.Attempted} branches",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await _showDialog(dialog) != ContentDialogResult.Primary)
            return;

        var deletionMode = forceDeleteCheckBox.IsChecked == true
            ? BranchDeletionMode.Force
            : BranchDeletionMode.Safe;
        if (_isShuttingDown() || !ReferenceEquals(originalRepository, _viewModel.Repository)
            || _viewModel.IsBusy) return;
        var repository = originalRepository;

        var executionResult = await _viewModel.Branches.DeleteLocalFolderAsync(
            repository,
            plan,
            deletionMode,
            successfulBranches =>
            {
                if (_viewModel.History.ActiveReference is { } activeReference
                    && successfulBranches.Contains(activeReference, StringComparer.Ordinal))
                {
                    _viewModel.History.HandleReferenceDeleted(activeReference);
                }
            });

        if (executionResult is null || executionResult.Failures.Count == 0)
            return;

        await ShowBranchFolderDeletionFailuresAsync(
            remote: false,
            plan.Attempted,
            executionResult.SuccessfulBranches.Count,
            executionResult.Failures,
            plan.Skipped);
    }

    private async Task ConfirmDeleteRemoteBranchFolderAsync(
        RepositoryTreeNode folderNode,
        BranchFolderInfo folderInfo)
    {
        var originalRepository = _viewModel.Repository;
        if (originalRepository is null) return;
        var snapshot = CollectBranchFolderCandidates(folderNode, RepositoryTreeNodeKind.RemoteBranch);
        IReadOnlyList<RemoteBranchFolderDeletionTarget> targets;
        try
        {
            targets = _viewModel.Branches.CreateRemoteFolderDeletionTargets(folderInfo, snapshot);
        }
        catch (InvalidOperationException exception)
        {
            await _showError(
                "Could not delete remote branches",
                $"{exception.Message}\n\nRefresh the repository and try again.");
            return;
        }

        if (targets.Count == 0)
        {
            await _showInformation(
                "No remote branches to delete",
                $"The folder '{folderInfo.RemoteName}/{folderInfo.Prefix}' contains no remote branch leaves.");
            return;
        }

        var content = new StackPanel { Spacing = UiStyles.Resolve<double>("Spacing.L") };
        content.Children.Add(new TextBlock
        {
            Text = $"{targets.Count} remote branches will be attempted:",
            TextWrapping = TextWrapping.Wrap
        });
        var branchList = new StackPanel { Spacing = 4 };
        foreach (var target in targets)
            AddBranchLine(branchList, target.BranchName);
        content.Children.Add(CreateBoundedBranchList(branchList));

        var folderDisplay = $"{folderInfo.RemoteName}/{folderInfo.Prefix}";
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = $"Delete remote branches in '{folderDisplay}'?",
            Content = content,
            PrimaryButtonText = $"Delete {targets.Count} branches",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await _showDialog(dialog) != ContentDialogResult.Primary)
            return;

        if (_isShuttingDown() || !ReferenceEquals(originalRepository, _viewModel.Repository)
            || _viewModel.IsBusy) return;
        var repository = originalRepository;

        var completedResult = await _viewModel.Branches.DeleteRemoteFolderAsync(
            repository,
            folderInfo.RemoteName!,
            targets,
            successfulBranches =>
            {
                if (_viewModel.History.ActiveReference is { } activeReference
                    && successfulBranches.Contains(activeReference, StringComparer.Ordinal))
                {
                    _viewModel.History.HandleReferenceDeleted(activeReference);
                }
            });

        if (completedResult is null || completedResult.Failures.Count == 0)
            return;

        await ShowBranchFolderDeletionFailuresAsync(
            remote: true,
            targets.Count,
            completedResult.SuccessfulBranches.Count,
            completedResult.Failures,
            skipped: 0);
    }

    private static IReadOnlyList<BranchFolderDeletionCandidate> CollectBranchFolderCandidates(
        RepositoryTreeNode folderNode,
        RepositoryTreeNodeKind branchKind) =>
        BranchFolderDeletionPlanner.Collect(
            folderNode,
            node => node.Children,
            node => node.Kind == branchKind && node.Value is GitBranch branch
                ? new BranchFolderDeletionCandidate(branch, node.AssociatedWorktreePath)
                : null);

    private async Task ShowNoEligibleLocalBranchesAsync(
        BranchFolderInfo folderInfo,
        BranchFolderDeletionPlan plan)
    {
        var content = new StackPanel { Spacing = UiStyles.Resolve<double>("Spacing.L") };
        content.Children.Add(new TextBlock
        {
            Text = $"The folder contains {plan.Total} branches:",
            TextWrapping = TextWrapping.Wrap
        });
        var branchList = new StackPanel { Spacing = 4 };
        foreach (var skipped in plan.SkippedBranches)
            AddBranchLine(branchList, $"{skipped.BranchName}    [{FormatSkipReason(skipped.Reason)}]");
        content.Children.Add(CreateBoundedBranchList(branchList));

        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = $"No branches can be deleted from '{folderInfo.Prefix}'.",
            Content = content,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await _showDialog(dialog);
    }

    private async Task ShowBranchFolderDeletionFailuresAsync(
        bool remote,
        int attempted,
        int successful,
        IReadOnlyList<BranchDeletionFailure> failures,
        int skipped)
    {
        var content = new StackPanel { Spacing = UiStyles.Resolve<double>("Spacing.L") };
        content.Children.Add(new TextBlock
        {
            Text = remote
                ? $"Deleted {successful} of {attempted} attempted remote branches."
                : $"Deleted {successful} of {attempted} attempted branches.",
            TextWrapping = TextWrapping.Wrap
        });

        var failureList = new StackPanel { Spacing = 4 };
        failureList.Children.Add(new TextBlock
        {
            Text = "Could not delete:",
            TextWrapping = TextWrapping.Wrap
        });
        foreach (var failure in failures)
        {
            AddBranchLine(failureList, failure.BranchName);
            failureList.Children.Add(new TextBlock
            {
                Text = $"Git: {failure.Message}",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.72
            });
        }
        content.Children.Add(CreateBoundedBranchList(failureList));

        if (skipped > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"{skipped} branches were skipped before deletion.",
                TextWrapping = TextWrapping.Wrap
            });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = remote ? "Some remote branches were not deleted" : "Some branches were not deleted",
            Content = content,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await _showDialog(dialog);
    }

    private static ScrollViewer CreateBoundedBranchList(UIElement content) =>
        new()
        {
            Content = content,
            MaxHeight = BranchFolderListMaxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

    private static void AddBranchLine(Panel panel, string text) =>
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap
        });

    private static string FormatSkipReason(BranchFolderDeletionSkipReason reason) => reason switch
    {
        BranchFolderDeletionSkipReason.CurrentBranch => "current branch",
        BranchFolderDeletionSkipReason.UsedByWorktree => "used by worktree",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };
    internal async Task ConfirmDeleteLocalAsync(GitBranch branch)
    {
        var originalRepository = _viewModel.Repository;
        if (originalRepository is null || branch.IsCurrent || _viewModel.IsBusy)
        {
            return;
        }

        var remoteTarget = _viewModel.Branches.ResolveRemoteDeletionTargetForLocal(branch);

        var content = new StackPanel { Spacing = UiStyles.Resolve<double>("Spacing.L") };
        content.Children.Add(new TextBlock
        {
            Text = $"Delete local branch '{branch.Name}'?",
            TextWrapping = TextWrapping.Wrap
        });

        var forceDeleteCheckBox = new CheckBox
        { Style = UiStyles.Resolve<Style>("CompactCheckBoxStyle"),
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
            { Style = UiStyles.Resolve<Style>("CompactCheckBoxStyle"),
                Content = $"Also delete remote branch '{remoteTarget.Remote.Name}/{remoteTarget.BranchName}'",
                IsChecked = false
            };
            content.Children.Add(deleteRemoteCheckBox);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "Delete local branch?",
            Content = content,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await _showDialog(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        if (_isShuttingDown() || !ReferenceEquals(originalRepository, _viewModel.Repository)
            || _viewModel.IsBusy || branch.IsCurrent) return;
        var repository = originalRepository;

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
                _viewModel.History.HandleReferenceDeleted(reference);
            });

        if (result.LocalDeleted
            && deleteRemote
            && !result.RemoteDeleted
            && result.SecondaryFailureMessage is { } remoteFailure
            && remoteTarget is not null)
        {
            await _showError(
                "Local branch deleted; remote branch retained",
                $"Local branch '{branch.Name}' was deleted, but remote branch '{remoteTarget.Remote.Name}/{remoteTarget.BranchName}' could not be deleted.\n\nGit: {remoteFailure}");
        }
    }

    internal async Task ConfirmDeleteRemoteAsync(GitBranch remoteBranch)
    {
        var originalRepository = _viewModel.Repository;
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
            await _showError("Could not delete remote branch", exception.Message);
            return;
        }

        var content = new StackPanel { Spacing = UiStyles.Resolve<double>("Spacing.L") };
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
            { Style = UiStyles.Resolve<Style>("CompactCheckBoxStyle"),
                Content = $"Also delete local branch '{localBranch.Name}'",
                IsChecked = false,
                IsEnabled = !localBranch.IsCurrent
            };
            content.Children.Add(deleteLocalCheckBox);

            var forceCheckBox = new CheckBox
            { Style = UiStyles.Resolve<Style>("CompactCheckBoxStyle"),
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
            XamlRoot = _xamlRoot(),
            Title = "Delete remote branch?",
            Content = content,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await _showDialog(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        if (_isShuttingDown() || !ReferenceEquals(originalRepository, _viewModel.Repository)
            || _viewModel.IsBusy) return;
        var repository = originalRepository;

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
                _viewModel.History.HandleReferenceDeleted(reference);
            });

        if (result.RemoteDeleted
            && deleteLocal
            && !result.LocalDeleted
            && result.SecondaryFailureMessage is { } localFailure
            && target.LocalBranch is { } retainedLocalBranch)
        {
            await _showError(
                "Remote branch deleted; local branch retained",
                $"Remote branch '{remoteBranch.Name}' was deleted, but local branch '{retainedLocalBranch.Name}' could not be deleted.\n\nGit: {localFailure}");
        }
    }


}
