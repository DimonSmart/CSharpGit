using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private IRepositoryHistoryRewriteService? _repositoryHistoryRewriteService;
    private bool _historyRewriteInProgress;

    internal bool IsHistoryRewriteInProgress => _historyRewriteInProgress;

    public MainPage(
        OpenRepositoryViewModel viewModel,
        IReferenceHistoryService referenceHistoryService,
        IReferenceService referenceService,
        ITagService tagService,
        IRepositoryRefreshProbe repositoryRefreshProbe,
        IWorkingTreeDiffService workingTreeDiffService,
        IRepositoryFileVersionService fileVersionService,
        IDesktopShellService desktopShellService,
        IRepositoryPathService repositoryPathService,
        IGitToolsService gitToolsService,
        IRepositorySnapshotService repositorySnapshotService,
        IRepositoryHistoryRewriteService repositoryHistoryRewriteService)
        : this(
            viewModel,
            referenceHistoryService,
            referenceService,
            tagService,
            repositoryRefreshProbe,
            workingTreeDiffService,
            fileVersionService,
            desktopShellService,
            repositoryPathService,
            gitToolsService,
            repositorySnapshotService)
    {
        _repositoryHistoryRewriteService = repositoryHistoryRewriteService
            ?? throw new ArgumentNullException(nameof(repositoryHistoryRewriteService));
        InstallRepositoryHistoryRewriteMenus();
    }

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

        var entry = _repositorySnapshot.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, row.Match.Path, StringComparison.Ordinal));
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
                _gitToolsService is not null,
                () => OpenRepositorySnapshotFileAsync(entry, openInEditor: true));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(
                flyout,
                "Remove from repository history…",
                _repositoryHistoryRewriteService is not null && !_viewModel.IsBusy && !_historyRewriteInProgress,
                () => RemovePathFromRepositoryHistoryAsync(entry));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        AddMenuItem(flyout, "Copy path", true, () => CopyTextAsync(entry.Path));
        flyout.ShowAt(target);
    }

    private async Task RemovePathFromRepositoryHistoryAsync(RepositorySnapshotEntry entry)
    {
        var service = _repositoryHistoryRewriteService;
        var repository = _viewModel.Repository;
        if (service is null
            || repository is null
            || entry.Kind != RepositorySnapshotEntryKind.File
            || _historyRewriteInProgress
            || !RepositoryFilesSnapshotMatchesSelection)
        {
            return;
        }

        try
        {
            var tool = await service.GetToolStatusAsync(repository);
            if (!tool.IsAvailable)
            {
                await ShowHistoryRewriteMessageAsync(
                    "git-filter-repo is required",
                    "git-filter-repo is required for this operation. CSharpGit will not install Python, pip, or git-filter-repo automatically.");
                return;
            }

            var analysis = await service.AnalyzePathRemovalAsync(repository, entry.Path);
            if (analysis.PathHistoryCommitCount == 0)
            {
                await ShowHistoryRewriteMessageAsync(
                    "Nothing to remove",
                    "The file is no longer present in repository history.");
                return;
            }

            if (!await ConfirmPathHistoryRemovalAsync(analysis))
                return;

            _historyRewriteInProgress = true;
            PathRemovalResult? result = null;
            Exception? capturedFailure = null;

            var succeeded = await _viewModel.RunHistoryRewriteMutationAsync(
                async () =>
                {
                    _viewModel.InvalidateForHistoryRewrite();
                    InvalidateHistoryRewritePresentation();
                    try
                    {
                        result = await service.RemovePathFromHistoryAsync(repository, entry.Path);
                    }
                    catch (Exception exception)
                    {
                        capturedFailure = exception;
                        throw;
                    }
                },
                "Repository history rewrite did not complete successfully.");

            if (succeeded && result is not null)
            {
                await _viewModel.SelectHistoryCommitAfterRewriteAsync(result.HeadObjectId);
                await ShowHistoryRewriteSuccessAsync(analysis, result);
            }
            else if (result is not null)
            {
                await ShowHistoryRewriteMessageAsync(
                    "History rewrite refresh failed",
                    "Repository history was rewritten and verified, but CSharpGit could not refresh the UI completely.\n\n" +
                    $"Safety backup:\n{result.BackupPath}\n\n" +
                    "Use Refresh to reread the repository state.");
            }
            else
            {
                await ShowHistoryRewriteFailureAsync(capturedFailure);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (RepositoryHistoryRewriteException exception)
        {
            await ShowHistoryRewriteFailureAsync(exception);
        }
        catch (Exception)
        {
            await ShowHistoryRewriteMessageAsync(
                "History rewrite failed",
                "Repository history rewrite did not complete successfully.");
        }
        finally
        {
            _historyRewriteInProgress = false;
        }
    }

    private void InvalidateHistoryRewritePresentation()
    {
        CancelRepositoryFilesRequests();
        ClearRepositoryFileSelection();

        _repositorySnapshotCache.Clear();
        _repositoryFilesStates.Clear();
        _repositoryFilesStateLru.Clear();
        _repositoryFilesStateLruNodes.Clear();
        _repositorySnapshot = [];
        _repositorySnapshotRepository = null;
        _repositorySnapshotCommit = null;
        _repositorySnapshotLoadedSuccessfully = false;
        _repositoryContentMatches = [];
        _repositoryContentCommit = null;
        _repositoryFilesLastSelectedPath = null;
        ClearRepositoryFilesTree();
        UpdateRepositoryFilesModeSurface();
        SetRepositoryFilesStatus("Rewriting repository history…", loading: true);

        _referenceHistoryCts?.Cancel();
        _activeReference = null;
        _scopedHistory.Clear();
        _scopedHasMore = false;
        HistoryList.ItemsSource = _viewModel.History;
        ShowReflogToggle.IsEnabled = true;
        ScopeCombo.Visibility = Visibility.Visible;
        ReferenceScopePanel.Visibility = Visibility.Collapsed;
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

    private async Task ShowHistoryRewriteFailureAsync(Exception? failure)
    {
        if (failure is RepositoryHistoryRewriteException rewriteFailure)
        {
            var message = rewriteFailure.Message;
            if (rewriteFailure.DestructivePhaseStarted)
                message += "\n\nThe repository may have been partially rewritten.";

            if (!string.IsNullOrWhiteSpace(rewriteFailure.BackupPath))
            {
                var label = rewriteFailure.DestructivePhaseStarted
                    ? "Safety backup"
                    : "Backup location";
                message += $"\n\n{label}:\n{rewriteFailure.BackupPath}";
            }

            await ShowHistoryRewriteMessageAsync("History rewrite failed", message);
            return;
        }

        await ShowHistoryRewriteMessageAsync(
            "History rewrite failed",
            "Repository history rewrite did not complete successfully.");
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
