using CSharpGit.Presentation.ViewModels;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.Workflows;

/// <summary>Implements an explicit user refresh without taking ownership of repository state or visual controls.</summary>
internal sealed class RepositoryManualRefreshController(
    OpenRepositoryViewModel viewModel,
    Func<bool> isShuttingDown,
    Func<bool> isRefreshInProgress,
    Action<bool> setRefreshInProgress,
    Action refreshPresentation,
    Action<Exception, string> reportFailure,
    Func<string, string, Task> showError)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Func<bool> _isRefreshInProgress = isRefreshInProgress;
    private readonly Action<bool> _setRefreshInProgress = setRefreshInProgress;
    private readonly Action _refreshPresentation = refreshPresentation;
    private readonly Action<Exception, string> _reportFailure = reportFailure;
    private readonly Func<string, string, Task> _showError = showError;

    internal async Task RefreshAsync()
    {
        var repository = _viewModel.Repository;
        if (_isShuttingDown() || _isRefreshInProgress() || repository is null) return;

        _setRefreshInProgress(true);
        try
        {
            await _viewModel.RefreshAsyncForDesktopCheck();
            if (!_isShuttingDown() && ReferenceEquals(repository, _viewModel.Repository))
                _refreshPresentation();
        }
        catch (OperationCanceledException) when (
            _isShuttingDown() || !ReferenceEquals(repository, _viewModel.Repository))
        {
        }
        catch (Exception exception)
        {
            _reportFailure(exception, "Repository refresh");
            if (!_isShuttingDown() && ReferenceEquals(repository, _viewModel.Repository)
                && !_viewModel.HasError)
                await _showError("Refresh failed", exception.Message);
        }
        finally
        {
            _setRefreshInProgress(false);
        }
    }
}
