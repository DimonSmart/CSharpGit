using System.ComponentModel;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private const string RefreshRepositoryTooltip = "Refresh repository.";
    private const string ExternalRepositoryChangeTooltip = "Repository has changed externally. Refresh to see the latest state.";
    private const string RefreshingRepositoryTooltip = "Refreshing repository…";

    private readonly RepositoryChangeCoordinator _repositoryChangeCoordinator;
    private bool _repositoryChangeMonitoringInitialized;
    private bool _isRefreshInProgress;
    private bool _repositoryPresentationRefreshQueued;
    private bool _repositoryTreePresentationDirty;
    private bool _workingTreePresentationDirty;

    public bool IsRefreshRequired => _repositoryChangeCoordinator.IsRefreshRequired;

    private void InitializeRepositoryChangeMonitoring()
    {
        if (IsShuttingDown || _repositoryChangeMonitoringInitialized) return;
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

    private void ShutdownRepositoryChangeMonitoring()
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
        if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;

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
        if (IsShuttingDown || _ownedFollowupRefreshQueued) return;
        _ownedFollowupRefreshQueued = true;
        RunPageUiOperation(RefreshAfterOwnedMutationAsync, "Post-mutation repository reconciliation");
    }

    private async Task RefreshAfterOwnedMutationAsync()
    {
        var repository = _viewModel.Repository;
        try
        {
            // Let delayed FileSystemWatcher notifications coalesce first.
            await Task.Delay(RepositoryChangeMonitor.DebounceDelay);
            if (IsShuttingDown || repository is null
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
        if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;
        _repositoryChangeCoordinator.ChangeRepository(
            _viewModel.Repository,
            _viewModel.DisplayedWorkingTreeStatusSnapshot,
            _viewModel.DisplayedRefreshBaselineRevision);
    }

    // Preserve the real queued-delivery fence used by the desktop lifecycle test.
    internal Action CapturePendingMonitorDeliveryForDesktopCheck() =>
        _repositoryChangeCoordinator.CapturePendingMonitorDeliveryForDesktopCheck();

    private void SetRefreshInProgress(bool value)
    {
        if (IsShuttingDown) return;
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

        RepositoryToolbar.SetRefreshPresentation(
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

    private void QueueRepositoryPresentationRefresh(bool workingTreeChanged = false)
    {
        if (IsShuttingDown) return;

        if (workingTreeChanged) _workingTreePresentationDirty = true;
        else _repositoryTreePresentationDirty = true;
        if (_repositoryPresentationRefreshQueued) return;

        _repositoryPresentationRefreshQueued = true;
        if (DispatcherQueue.TryEnqueue(FlushRepositoryPresentationRefresh)) return;

        if (!IsShuttingDown && DispatcherQueue.HasThreadAccess)
        {
            FlushRepositoryPresentationRefresh();
            return;
        }

        ClearRepositoryPresentationRefreshQueue();
    }

    private void FlushRepositoryPresentationRefresh()
    {
        if (IsShuttingDown)
        {
            ClearRepositoryPresentationRefreshQueue();
            return;
        }

        _repositoryPresentationRefreshQueued = false;
        var refreshWorkingTree = _workingTreePresentationDirty;
        var refreshRepositoryTree = _repositoryTreePresentationDirty;
        _workingTreePresentationDirty = false;
        _repositoryTreePresentationDirty = false;
        InitializeTagSupportIfNeeded();

        if (refreshWorkingTree) RefreshPresentationCollections();
        if (refreshRepositoryTree) SynchronizeRepositoryTree();
        UpdateStatusBar();
    }

    private void ClearRepositoryPresentationRefreshQueue()
    {
        _repositoryPresentationRefreshQueued = false;
        _workingTreePresentationDirty = false;
        _repositoryTreePresentationDirty = false;
    }
}
