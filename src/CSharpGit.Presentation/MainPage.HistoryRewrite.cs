using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    internal bool IsHistoryRewriteInProgress => _viewModel.RepositoryHistoryRewrite.IsInProgress;

    private void InstallRepositoryHistoryRewriteMenus()
    {
        if (_repositoryFilesTree is not null)
        {
            _repositoryFilesTree.RightTapped -= RepositoryFilesTree_RightTapped;
            _repositoryFilesTree.RightTapped += RepositoryFilesTree_HistoryRewriteRightTapped;
        }

        if (_repositoryContentResults is not null)
        {
            _repositoryContentResults.RightTapped -= RepositoryContentResults_RightTapped;
            _repositoryContentResults.RightTapped += RepositoryContentResults_HistoryRewriteRightTapped;
        }
    }

    private void RepositoryFilesTree_HistoryRewriteRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (args.OriginalSource is not FrameworkElement source
            || SelectedRepositorySnapshotEntry() is not { } entry)
        {
            return;
        }

        ShowRepositorySnapshotFileMenuWithHistoryRewrite(source, entry);
        args.Handled = true;
    }

    private void RepositoryContentResults_HistoryRewriteRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (args.OriginalSource is not FrameworkElement source
            || _repositoryContentResults?.SelectedItem is not RepositoryContentSearchRow row)
        {
            return;
        }

        var entry = _repositoryFilesViewModel.FindEntry(row.Match.Path);
        if (entry is null) return;

        ShowRepositorySnapshotFileMenuWithHistoryRewrite(source, entry);
        args.Handled = true;
    }

    private void ShowRepositorySnapshotFileMenuWithHistoryRewrite(
        FrameworkElement target,
        RepositorySnapshotEntry entry)
    {
        var flyout = new MenuFlyout();
        if (entry.Kind == RepositorySnapshotEntryKind.File)
        {
            AddMenuItem(flyout, "Open", true, () => OpenRepositorySnapshotFileAsync(entry, openInEditor: false));
            AddMenuItem(
                flyout,
                "Open in editor",
                true,
                () => OpenRepositorySnapshotFileAsync(entry, openInEditor: true));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(
                flyout,
                "Remove from repository history…",
                !_viewModel.IsBusy && !_viewModel.RepositoryHistoryRewrite.IsInProgress,
                () => RemovePathFromRepositoryHistoryAsync(entry));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        AddMenuItem(flyout, "Copy path", true, () => CopyTextAsync(entry.Path));
        flyout.ShowAt(target);
    }

    private async Task RemovePathFromRepositoryHistoryAsync(RepositorySnapshotEntry entry)
    {
        var repository = _viewModel.Repository;
        if (repository is null || entry.Kind != RepositorySnapshotEntryKind.File
            || _viewModel.RepositoryHistoryRewrite.IsInProgress
            || !_repositoryFilesViewModel.SnapshotMatchesSelection)
            return;

        var preparation = await _viewModel.RepositoryHistoryRewrite.PreparePathRemovalAsync(repository, entry.Path);
        switch (preparation.Status)
        {
            case PathRemovalPreparationStatus.ToolUnavailable:
                await ShowHistoryRewriteMessageAsync("git-filter-repo is required",
                    "git-filter-repo is required for this operation. CSharpGit will not install Python, pip, or git-filter-repo automatically.");
                return;
            case PathRemovalPreparationStatus.NothingToRemove:
                await ShowHistoryRewriteMessageAsync("Nothing to remove",
                    "The file is no longer present in repository history.");
                return;
            case PathRemovalPreparationStatus.RepositoryChanged:
            case PathRemovalPreparationStatus.Cancelled:
                return;
            case PathRemovalPreparationStatus.Failed:
                await ShowHistoryRewriteMessageAsync("History rewrite failed",
                    preparation.FailureMessage ?? "Repository history rewrite did not complete successfully.");
                return;
            case PathRemovalPreparationStatus.Ready:
                break;
            default:
                return;
        }

        var analysis = preparation.Analysis!;
        if (!await ConfirmPathHistoryRemovalAsync(analysis)) return;
        var execution = await _viewModel.RepositoryHistoryRewrite.RemovePathAsync(
            repository, entry.Path, InvalidateHistoryRewritePresentation);
        switch (execution.Status)
        {
            case PathRemovalExecutionStatus.Completed when execution.Result is { } result:
                await RestoreCommitActionSelectionAsync(result.HeadObjectId);
                await ShowHistoryRewriteSuccessAsync(analysis, result);
                break;
            case PathRemovalExecutionStatus.RewriteCompletedButRefreshFailed:
                await ShowHistoryRewriteMessageAsync("History rewrite refresh failed",
                    "Repository history was rewritten and verified, but CSharpGit could not refresh the UI completely.\n\n" +
                    $"Safety backup:\n{execution.BackupPath}\n\n" +
                    "Use Refresh to reread the repository state.");
                break;
            case PathRemovalExecutionStatus.Failed:
                await ShowHistoryRewriteFailureAsync(execution);
                break;
        }
    }

    private void InvalidateHistoryRewritePresentation()
    {
        _repositoryFilesViewModel.Invalidate(
            "Rewriting repository history…",
            loading: true);
        ClearRepositoryFileSelection();
        UpdateRepositoryFilesModeSurface();

        UpdateHistoryScopePresentation();
        HistoryPane.Visibility = Visibility.Visible;
        WorkingTreePane.Visibility = Visibility.Collapsed;
    }

    private async Task<bool> ConfirmPathHistoryRemovalAsync(PathRemovalAnalysis analysis)
    {
        var content = new TextBlock
        {
            Text =
                $"Path:\n{analysis.Path}\n\n" +
                $"Path history:\n{analysis.PathHistoryCommitCount} commits\n\n" +
                "Affected refs:\n" +
                $"{analysis.AffectedLocalBranches.Count} local branches\n" +
                $"{analysis.AffectedTags.Count} tags\n" +
                $"{analysis.AffectedRemoteTrackingBranches.Count} remote-tracking branches\n\n" +
                "This operation rewrites Git history.\n" +
                $"Commit hashes will change, potentially in more than {analysis.PathHistoryCommitCount} commits.\n\n" +
                "Signed commits or tags may lose their cryptographic signatures.\n\n" +
                "A safety backup will be created before rewriting history.\n" +
                "Remote repositories will NOT be changed.\n\n" +
                "If this file contains an exposed password, token or API key, revoke or rotate it first.\n" +
                "Rewriting Git history does not make an exposed credential safe.",
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            MaxWidth = 620
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Remove file from repository history?",
            Content = content,
            PrimaryButtonText = "Remove from history",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowHistoryRewriteSuccessAsync(
        PathRemovalAnalysis analysis,
        PathRemovalResult result)
    {
        var message =
            "Repository history was rewritten locally.\n\n" +
            $"Removed:\n{result.Path}\n\n" +
            $"Path history:\n{analysis.PathHistoryCommitCount} commits\n\n" +
            $"Rewritten commits:\n{result.RewrittenCommitCount}\n\n" +
            "Remote repositories were not changed.\n\n" +
            $"Safety backup:\n{result.BackupPath}\n\n" +
            "Publishing rewritten history is a separate operation.";

        await ShowHistoryRewriteMessageAsync("History rewritten", message);
    }

    private async Task ShowHistoryRewriteFailureAsync(PathRemovalExecutionResult failure)
    {
        var message = failure.FailureMessage ?? "Repository history rewrite did not complete successfully.";
        if (failure.DestructivePhaseStarted)
            message += "\n\nThe repository may have been partially rewritten.";
        if (!string.IsNullOrWhiteSpace(failure.BackupPath))
            message += $"\n\n{(failure.DestructivePhaseStarted ? "Safety backup" : "Backup location")}:\n{failure.BackupPath}";
        await ShowHistoryRewriteMessageAsync("History rewrite failed", message);
    }

    private async Task ShowHistoryRewriteMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                MaxWidth = 620
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }
}
