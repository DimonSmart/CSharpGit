using System.ComponentModel;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Views;

namespace CSharpGit.Presentation.Workflows;

// Per-window watcher, baseline and refresh-indicator owner. Does not depend on MainPage.
internal sealed class RepositoryRefreshController(
    OpenRepositoryViewModel viewModel,
    RepositoryChangeCoordinator repositoryChangeCoordinator,
    RepositoryToolbarView toolbar,
    Func<bool> isShuttingDown,
    Action<Func<Task>, string> runUiOperation)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly RepositoryChangeCoordinator _repositoryChangeCoordinator = repositoryChangeCoordinator;
    private readonly RepositoryToolbarView _toolbar = toolbar;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;

    private const string RefreshRepositoryTooltip = "Refresh repository.";
    private const string ExternalRepositoryChangeTooltip = "Repository has changed externally. Refresh to see the latest state.";
    private const string RefreshingRepositoryTooltip = "Refreshing repository…";

    private bool _repositoryChangeMonitoringInitialized;
    internal bool IsRefreshInProgress => _isRefreshInProgress;
    private bool _isRefreshInProgress;

    internal bool IsRefreshRequired => _repositoryChangeCoordinator.IsRefreshRequired;

    internal void InitializeRepositoryChangeMonitoring()
    {
        if (_isShuttingDown() || _repositoryChangeMonitoringInitialized) return;
        _repositoryChangeMonitoringInitialized = true;

        _repositoryChangeCoordinator.RefreshRequirementChanged += UpdateRefreshIndicator;
        _repositoryChangeCoordinator.OwnedFollowupRefreshRequested += QueueOwnedFollowupRefresh;
        _repositoryChangeCoordinator.Initialize();
        _viewModel.PropertyChanged += RepositoryRefreshTracking_PropertyChanged;
        _viewModel.RepositoryStateRefreshStarting += RepositoryStateRefreshStarting;
        _viewModel.RepositoryStateRefreshCompleted += RepositoryStateRefreshCompleted;
        _viewModel.RepositoryMutationCompleted += RepositoryMutationCompleted;
        _viewModel.ConsumeRacedRefreshRetry = _repositoryChangeCoordinator.TryConsumeRacedRefresh;
        UpdateRepositoryChangeMonitor();
        UpdateRefreshIndicator();
    }

    internal void ShutdownRepositoryChangeMonitoring()
    {
        if (!_repositoryChangeMonitoringInitialized) return;
        _repositoryChangeMonitoringInitialized = false;
        _viewModel.PropertyChanged -= RepositoryRefreshTracking_PropertyChanged;
        _viewModel.RepositoryStateRefreshStarting -= RepositoryStateRefreshStarting;
        _viewModel.RepositoryStateRefreshCompleted -= RepositoryStateRefreshCompleted;
        _viewModel.RepositoryMutationCompleted -= RepositoryMutationCompleted;
        _viewModel.ConsumeRacedRefreshRetry = null;
        _repositoryChangeCoordinator.RefreshRequirementChanged -= UpdateRefreshIndicator;
        _repositoryChangeCoordinator.OwnedFollowupRefreshRequested -= QueueOwnedFollowupRefresh;
        _repositoryChangeCoordinator.Dispose();
    }

    private void RepositoryRefreshTracking_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (_isShuttingDown() || !_repositoryChangeMonitoringInitialized) return;

        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.Repository))
            UpdateRepositoryChangeMonitor();
        else if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.DisplayedRefreshBaselineRevision))
            _repositoryChangeCoordinator.PublishBaseline(
                _viewModel.Repository,
                _viewModel.DisplayedWorkingTreeStatusSnapshot,
                _viewModel.DisplayedRefreshBaselineRevision);
    }

    private void RepositoryStateRefreshStarting(Repository repository)
    {
        _repositoryChangeCoordinator.RefreshStarted(repository);
        SetRefreshInProgress(true);
    }

    private void RepositoryStateRefreshCompleted(Repository repository)
    {
        _repositoryChangeCoordinator.RefreshCompleted(repository);
        SetRefreshInProgress(false);
    }

    private void RepositoryMutationCompleted(Repository repository) =>
        _repositoryChangeCoordinator.OwnedMutationCompleted(repository);

    private bool _ownedFollowupRefreshQueued;

    private void QueueOwnedFollowupRefresh()
    {
        if (_isShuttingDown() || _ownedFollowupRefreshQueued) return;
        _ownedFollowupRefreshQueued = true;
        _runUiOperation(RefreshAfterOwnedMutationAsync, "Post-mutation repository reconciliation");
    }

    private async Task RefreshAfterOwnedMutationAsync()
    {
        var repository = _viewModel.Repository;
        try
        {
            // Let delayed FileSystemWatcher notifications coalesce first.
            await Task.Delay(RepositoryChangeMonitor.DebounceDelay);
            if (_isShuttingDown() || repository is null
                || !ReferenceEquals(repository, _viewModel.Repository)) return;

            await _viewModel.RefreshAsyncForDesktopCheck();
        }
        finally
        {
            _ownedFollowupRefreshQueued = false;
        }
    }

    private void UpdateRepositoryChangeMonitor()
    {
        if (_isShuttingDown() || !_repositoryChangeMonitoringInitialized) return;
        _repositoryChangeCoordinator.ChangeRepository(
            _viewModel.Repository,
            _viewModel.DisplayedWorkingTreeStatusSnapshot,
            _viewModel.DisplayedRefreshBaselineRevision);
    }

    // Preserve the real queued-delivery fence used by the desktop lifecycle test.
    internal Action CapturePendingMonitorDeliveryForDesktopCheck() =>
        _repositoryChangeCoordinator.CapturePendingMonitorDeliveryForDesktopCheck();

    internal void SetRefreshInProgress(bool value)
    {
        if (_isShuttingDown()) return;
        if (_isRefreshInProgress == value) return;
        _isRefreshInProgress = value;
        UpdateRefreshIndicator();
    }

    private void UpdateRefreshIndicator()
    {
        var state = _isRefreshInProgress
            ? RefreshIndicatorState.Refreshing
            : IsRefreshRequired
                ? RefreshIndicatorState.RefreshRequired
                : RefreshIndicatorState.UpToDate;
        var isRefreshing = state == RefreshIndicatorState.Refreshing;

        _toolbar.SetRefreshPresentation(
            isRefreshing,
            state == RefreshIndicatorState.RefreshRequired,
            state switch
            {
                RefreshIndicatorState.RefreshRequired => ExternalRepositoryChangeTooltip,
                RefreshIndicatorState.Refreshing => RefreshingRepositoryTooltip,
                _ => RefreshRepositoryTooltip
            });
    }

    private enum RefreshIndicatorState
    {
        UpToDate,
        RefreshRequired,
        Refreshing
    }
}
