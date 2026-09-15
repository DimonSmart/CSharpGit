using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private const double BranchFolderListMaxHeight = 360;

    private async Task ConfirmDeleteBranchFolderAsync(
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
        var snapshot = CollectBranchFolderCandidates(folderNode, RepositoryTreeNodeKind.LocalBranch);
        var plan = BranchFolderDeletionPlanner.CreateLocal(snapshot);
        if (plan.Attempted == 0)
        {
            await ShowNoEligibleLocalBranchesAsync(folderInfo, plan);
            return;
        }

        var content = new StackPanel { Spacing = 8 };
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
        {
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
            XamlRoot = XamlRoot,
            Title = $"Delete branches in '{folderInfo.Prefix}'?",
            Content = content,
            PrimaryButtonText = $"Delete {plan.Attempted} branches",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var deletionMode = forceDeleteCheckBox.IsChecked == true
            ? BranchDeletionMode.Force
            : BranchDeletionMode.Safe;
        BranchFolderDeletionExecutionResult? executionResult = null;

        var mutationSucceeded = await _viewModel.RunMutationAsync(
            async () =>
            {
                executionResult = await BranchFolderDeletionExecutor.ExecuteAsync(
                    plan.EligibleBranches,
                    candidate => candidate.Branch.Name,
                    candidate => _referenceService.DeleteBranchAsync(
                        _viewModel.Repository!,
                        candidate.Branch.Name,
                        deletionMode));

                if (_activeReference is not null
                    && executionResult.SuccessfulBranches.Contains(_activeReference, StringComparer.Ordinal))
                {
                    ShowAllHistory();
                }
            },
            "Could not delete branches in folder");

        if (!mutationSucceeded || executionResult is null || executionResult.Failures.Count == 0)
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
        var snapshot = CollectBranchFolderCandidates(folderNode, RepositoryTreeNodeKind.RemoteBranch);
        IReadOnlyList<RemoteBranchFolderDeletionTarget> targets;
        try
        {
            targets = BranchFolderDeletionPlanner.CreateRemoteTargets(folderInfo, snapshot);
        }
        catch (InvalidOperationException exception)
        {
            await ShowErrorAsync(
                "Could not delete remote branches",
                $"{exception.Message}\n\nRefresh the repository and try again.");
            return;
        }

        if (targets.Count == 0)
        {
            await ShowInformationAsync(
                "No remote branches to delete",
                $"The folder '{folderInfo.RemoteName}/{folderInfo.Prefix}' contains no remote branch leaves.");
            return;
        }

        var content = new StackPanel { Spacing = 8 };
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
            XamlRoot = XamlRoot,
            Title = $"Delete remote branches in '{folderDisplay}'?",
            Content = content,
            PrimaryButtonText = $"Delete {targets.Count} branches",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        BranchFolderDeletionExecutionResult? executionResult = null;
        var mutationSucceeded = await _viewModel.RunMutationAsync(
            async () =>
            {
                executionResult = await BranchFolderDeletionExecutor.ExecuteAsync(
                    targets,
                    target => target.BranchName,
                    target => _referenceService.DeleteRemoteBranchAsync(
                        _viewModel.Repository!,
                        folderInfo.RemoteName!,
                        target.RelativeBranchName));

                if (_activeReference is not null
                    && executionResult.SuccessfulBranches.Contains(_activeReference, StringComparer.Ordinal))
                {
                    ShowAllHistory();
                }
            },
            "Could not delete remote branches in folder");

        if (!mutationSucceeded || executionResult is null || executionResult.Failures.Count == 0)
            return;

        await ShowBranchFolderDeletionFailuresAsync(
            remote: true,
            targets.Count,
            executionResult.SuccessfulBranches.Count,
            executionResult.Failures,
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
        var content = new StackPanel { Spacing = 8 };
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
            XamlRoot = XamlRoot,
            Title = $"No branches can be deleted from '{folderInfo.Prefix}'.",
            Content = content,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private async Task ShowBranchFolderDeletionFailuresAsync(
        bool remote,
        int attempted,
        int successful,
        IReadOnlyList<BranchDeletionFailure> failures,
        int skipped)
    {
        var content = new StackPanel { Spacing = 8 };
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
            XamlRoot = XamlRoot,
            Title = remote ? "Some remote branches were not deleted" : "Some branches were not deleted",
            Content = content,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private async Task ShowInformationAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
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
}
