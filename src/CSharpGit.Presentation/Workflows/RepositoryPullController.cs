using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Views;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation.Workflows;

// Owns pull execution, result presentation and post-pull history selection restoration.
internal sealed class RepositoryPullController(
    OpenRepositoryViewModel viewModel,
    HistoryWorkspaceView historyWorkspace,
    Func<ICommand, Task> executeCommand,
    Func<string, string, Task> showError,
    Func<bool> isShuttingDown)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly HistoryWorkspaceView _historyWorkspace = historyWorkspace;
    private readonly Func<ICommand, Task> _executeCommand = executeCommand;
    private readonly Func<string, string, Task> _showError = showError;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;

    internal async Task PullAsync(PullStrategy? strategy)
    {
        var repository = _viewModel.Repository;
        if (_isShuttingDown() || repository is null || !_viewModel.RepositorySync.CanPull)
            return;

        var previousHead = _viewModel.CurrentHeadCommit;
        var history = _viewModel.History;
        var selectionIntentVersion = history.SelectionIntentVersion;
        PullExecutionResult result;

        if (strategy is { } requestedStrategy)
        {
            result = await _viewModel.RepositorySync.PullAsync(repository, requestedStrategy);
        }
        else
        {
            var previousResult = _viewModel.RepositorySync.LastPullResult;
            await _executeCommand(_viewModel.RepositorySync.PullCommand);
            if (_viewModel.RepositorySync.LastPullResult is not { } completedResult
                || ReferenceEquals(previousResult, completedResult))
                return;
            result = completedResult;
        }

        var currentHead = _viewModel.CurrentHeadCommit;
        if (!_isShuttingDown()
            && ReferenceEquals(_viewModel.Repository, repository)
            && result.Kind == PullExecutionKind.Completed
            && currentHead is not null
            && !string.Equals(previousHead, currentHead, StringComparison.Ordinal)
            && history.SelectionIntentVersion == selectionIntentVersion
            && _viewModel.Stashes.SelectedStash is null
            && _historyWorkspace.Visibility == Visibility.Visible)
        {
            var target = await history.SelectCommitInCurrentHistoryAsync(
                currentHead, selectionIntentVersion);
            if (!_isShuttingDown()
                && ReferenceEquals(_viewModel.Repository, repository)
                && target is not null
                && ReferenceEquals(target, history.SelectedRow))
                _historyWorkspace.HistoryViewControl.ScrollTo(target);
        }

        await ShowResultAsync(result);
    }

    private async Task ShowResultAsync(PullExecutionResult result)
    {
        if (result.Kind is PullExecutionKind.Completed or PullExecutionKind.Unavailable)
            return;

        var title = result.Kind switch
        {
            PullExecutionKind.NeedsAttention => "Pull needs attention",
            PullExecutionKind.Refused => "Pull cannot fast-forward",
            _ => "Pull failed"
        };
        await _showError(title, result.Message);
    }
}
