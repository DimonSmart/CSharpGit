using System.ComponentModel;
using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private const string RefreshRepositoryTooltip = "Refresh repository.";
    private const string ExternalRepositoryChangeTooltip = "Repository has changed externally. Refresh to see the latest state.";
    private const string RefreshingRepositoryTooltip = "Refreshing repository…";

    private readonly RepositoryChangeMonitor _repositoryChangeMonitor = new();
    private readonly CancellationTokenSource _workingTreeStatusStop = new();
    private CancellationToken _workingTreeStatusToken;
    private readonly IWorkingTreeStatusReader _workingTreeStatusReader;
    private readonly RepositoryRefreshLifecycleState _repositoryRefreshLifecycle = new();
    private bool _repositoryChangeMonitoringInitialized;
    private bool _isRefreshInProgress;
    private Repository? _monitoredRepository;
    private WorkingTreeStatusSnapshot? _displayedWorkingTreeStatus;
    private bool _workingTreeStatusRunning;
    private RepositoryInvalidationBatch? _workingTreeStatusPending;
    private bool _repositoryRefreshWindowActive;
    private long _repositoryRefreshStartGeneration;
    private Repository? _repositoryRefreshWindowRepository;
    private long _handledInvalidationGeneration;
    private bool _repositoryPresentationRefreshQueued;
    private bool _repositoryTreePresentationDirty;
    private bool _workingTreePresentationDirty;

    public bool IsRefreshRequired => _repositoryRefreshLifecycle.IsRefreshRequired;

    private void InitializeRepositoryChangeMonitoring()
    {
        if (IsShuttingDown || _repositoryChangeMonitoringInitialized) return;
        _workingTreeStatusToken = _workingTreeStatusStop.Token;
        _repositoryChangeMonitoringInitialized = true;

        _repositoryChangeMonitor.RepositoryChanged += RepositoryChangeMonitor_RepositoryChanged;
        _viewModel.PropertyChanged += RepositoryRefreshTracking_PropertyChanged;
        _viewModel.RepositoryStateRefreshStarting += RepositoryStateRefreshStarting;
        _viewModel.RepositoryStateRefreshCompleted += RepositoryStateRefreshCompleted;
        UpdateRepositoryChangeMonitor();
        UpdateRefreshIndicator();
    }

    private void ShutdownRepositoryChangeMonitoring()
    {
        if (!_repositoryChangeMonitoringInitialized) return;
        _repositoryChangeMonitoringInitialized = false;
        _workingTreeStatusPending = null;
        _repositoryRefreshWindowActive = false;
        _repositoryRefreshWindowRepository = null;

        _viewModel.PropertyChanged -= RepositoryRefreshTracking_PropertyChanged;
        _viewModel.RepositoryStateRefreshStarting -= RepositoryStateRefreshStarting;
        _viewModel.RepositoryStateRefreshCompleted -= RepositoryStateRefreshCompleted;
        _repositoryChangeMonitor.RepositoryChanged -= RepositoryChangeMonitor_RepositoryChanged;
        _workingTreeStatusStop.Cancel();
        _repositoryChangeMonitor.Dispose();
        _workingTreeStatusStop.Dispose();
    }

    private void RepositoryRefreshTracking_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;

        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.Repository))
        {
            UpdateRepositoryChangeMonitor();
            return;
        }

        if (eventArgs.PropertyName != nameof(OpenRepositoryViewModel.DisplayedRefreshBaselineRevision))
            return;

        var baseline = _viewModel.DisplayedWorkingTreeStatusSnapshot;
        if (baseline is null) return;

        var repository = _viewModel.Repository;
        var baselineRevision = _viewModel.DisplayedRefreshBaselineRevision;
        var refreshStartGeneration =
            _repositoryRefreshWindowActive
            && ReferenceEquals(_repositoryRefreshWindowRepository, repository)
                ? _repositoryRefreshStartGeneration
                : _repositoryChangeMonitor.Generation;

        if (!_repositoryRefreshLifecycle.PublishBaseline(baselineRevision)) return;

        _displayedWorkingTreeStatus = baseline;
        _workingTreeStatusPending = null;
        _repositoryRefreshWindowActive = false;
        _repositoryRefreshWindowRepository = null;
        _handledInvalidationGeneration = Math.Max(
            _handledInvalidationGeneration,
            refreshStartGeneration);
        UpdateRefreshIndicator();

        var concurrentInvalidation =
            _repositoryChangeMonitor.GetInvalidationsSince(refreshStartGeneration);
        Trace.WriteLine(
            $"Repository baseline published: revision={baselineRevision} startGeneration={refreshStartGeneration} currentGeneration={_repositoryChangeMonitor.Generation} concurrent={concurrentInvalidation.HasAny}");

        if (concurrentInvalidation.HasAny)
            HandleInvalidationBatch(concurrentInvalidation);
    }

    private void RepositoryStateRefreshStarting(Repository repository)
    {
        if (IsShuttingDown
            || !_repositoryChangeMonitoringInitialized
            || !ReferenceEquals(repository, _viewModel.Repository))
            return;

        _repositoryRefreshWindowActive = true;
        _repositoryRefreshWindowRepository = repository;
        _repositoryRefreshStartGeneration = _repositoryChangeMonitor.Generation;
        Trace.WriteLine(
            $"Repository refresh window started: generation={_repositoryRefreshStartGeneration}");
    }

    private void RepositoryStateRefreshCompleted(Repository repository)
    {
        if (IsShuttingDown
            || !_repositoryChangeMonitoringInitialized
            || !_repositoryRefreshWindowActive
            || !ReferenceEquals(repository, _repositoryRefreshWindowRepository))
            return;

        var startGeneration = _repositoryRefreshStartGeneration;
        _repositoryRefreshWindowActive = false;
        _repositoryRefreshWindowRepository = null;

        var invalidation = _repositoryChangeMonitor.GetInvalidationsSince(startGeneration);
        if (invalidation.HasAny)
            HandleInvalidationBatch(invalidation);

        StartWorkingTreeStatusLoopIfNeeded();
    }

    private void UpdateRepositoryChangeMonitor()
    {
        if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;

        var repository = _viewModel.Repository;
        if (repository is null)
        {
            _monitoredRepository = null;
            _displayedWorkingTreeStatus = null;
            _workingTreeStatusPending = null;
            _repositoryRefreshWindowActive = false;
            _repositoryRefreshWindowRepository = null;
            _handledInvalidationGeneration = _repositoryChangeMonitor.Generation;
            _repositoryRefreshLifecycle.Reset(_viewModel.DisplayedRefreshBaselineRevision);
            _repositoryChangeMonitor.Stop();
            UpdateRefreshIndicator();
            return;
        }

        if (Equals(repository, _monitoredRepository)) return;

        _monitoredRepository = repository;
        _displayedWorkingTreeStatus = _viewModel.DisplayedWorkingTreeStatusSnapshot;
        _workingTreeStatusPending = null;
        _repositoryRefreshWindowActive = false;
        _repositoryRefreshWindowRepository = null;
        _repositoryRefreshLifecycle.Reset(_viewModel.DisplayedRefreshBaselineRevision);
        _repositoryChangeMonitor.Start(repository);
        _handledInvalidationGeneration = _repositoryChangeMonitor.Generation;
        UpdateRefreshIndicator();
    }

    private void RepositoryChangeMonitor_RepositoryChanged(
        object? sender,
        RepositoryInvalidatedEventArgs eventArgs)
    {
        if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;

        var batch = eventArgs.Batch;
        Trace.WriteLine(
            $"Repository monitor invalidated: generation={batch.Generation} workingTree={batch.HasWorkingTreeChanges} metadata={batch.HasRelevantMetadataChanges} unknown={batch.HasUnknownOrOverflow} workingPaths={string.Join(",", batch.WorkingTreePaths)} metadataPaths={string.Join(",", batch.MetadataPaths)}");

        if (DispatcherQueue.HasThreadAccess)
        {
            HandleInvalidationBatch(batch);
            return;
        }

        DispatcherQueue.TryEnqueue(() => HandleInvalidationBatch(batch));
    }

    private void HandleInvalidationBatch(RepositoryInvalidationBatch batch)
    {
        if (IsShuttingDown
            || !_repositoryChangeMonitoringInitialized
            || !batch.HasAny
            || _viewModel.Repository is null
            || _displayedWorkingTreeStatus is null)
            return;

        if (_repositoryRefreshWindowActive)
            return;

        if (batch.Generation <= _handledInvalidationGeneration)
            return;

        _handledInvalidationGeneration = batch.Generation;

        if (!_repositoryRefreshLifecycle.CanRunBackgroundStatusCheck)
            return;

        if (batch.HasUnknownOrOverflow)
        {
            LatchRefreshRequired("WatcherOverflow", batch.Generation);
            return;
        }

        if (batch.HasRelevantMetadataChanges)
        {
            LatchRefreshRequired(
                $"MetadataChanged:{batch.MetadataPaths.FirstOrDefault() ?? "<unknown>"}",
                batch.Generation);
            return;
        }

        if (!batch.HasWorkingTreeChanges) return;
        _workingTreeStatusPending = RepositoryInvalidationBatch.Merge(
            _workingTreeStatusPending,
            batch);
        StartWorkingTreeStatusLoopIfNeeded();
    }

    private void StartWorkingTreeStatusLoopIfNeeded()
    {
        if (IsShuttingDown
            || !_repositoryChangeMonitoringInitialized
            || _repositoryRefreshWindowActive
            || !_repositoryRefreshLifecycle.CanRunBackgroundStatusCheck
            || _workingTreeStatusPending is null
            || _workingTreeStatusRunning)
            return;

        _workingTreeStatusRunning = true;
        _ = RunWorkingTreeStatusLoopAsync();
    }

    private async Task RunWorkingTreeStatusLoopAsync()
    {
        try
        {
            while (!IsShuttingDown
                   && _repositoryChangeMonitoringInitialized
                   && !_repositoryRefreshWindowActive
                   && _repositoryRefreshLifecycle.CanRunBackgroundStatusCheck
                   && _workingTreeStatusPending is not null)
            {
                var invalidation = _workingTreeStatusPending;
                _workingTreeStatusPending = null;

                var repository = _viewModel.Repository;
                var baseline = _displayedWorkingTreeStatus;
                var baselineRevision = _repositoryRefreshLifecycle.BaselineRevision;
                if (repository is null || baseline is null) continue;

                WorkingTreeStatusSnapshot current;
                try
                {
                    current = await _workingTreeStatusReader.ReadAsync(
                        repository,
                        _workingTreeStatusToken);
                }
                catch (OperationCanceledException) when (_workingTreeStatusToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    if (IsShuttingDown
                        || !_repositoryChangeMonitoringInitialized
                        || _repositoryRefreshWindowActive
                        || !ReferenceEquals(repository, _viewModel.Repository)
                        || baselineRevision != _repositoryRefreshLifecycle.BaselineRevision)
                        continue;

                    Trace.WriteLine(
                        $"Working-tree change probe: generation={invalidation.Generation} result=error error={exception.GetType().Name}: {exception.Message}");
                    LatchRefreshRequired("StatusProbeFailed", invalidation.Generation);
                    return;
                }

                if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;
                if (_repositoryRefreshWindowActive) continue;
                if (!ReferenceEquals(repository, _viewModel.Repository)) continue;

                var reason = WorkingTreeInvalidationEvaluator.Evaluate(
                    baseline,
                    current,
                    invalidation);
                var disposition = _repositoryRefreshLifecycle.ApplyStatusCheckResult(
                    baselineRevision,
                    reason != WorkingTreeInvalidationReason.None);

                if (disposition == RepositoryStatusCheckDisposition.StaleRevision)
                {
                    Trace.WriteLine(
                        $"Working-tree change probe: generation={invalidation.Generation} result=stale baselineRevision={baselineRevision} currentRevision={_repositoryRefreshLifecycle.BaselineRevision}");
                    continue;
                }

                if (disposition == RepositoryStatusCheckDisposition.IgnoredWhileLatched)
                    return;

                Trace.WriteLine(
                    $"Working-tree change probe: generation={invalidation.Generation} result={reason} baselineRevision={baselineRevision}");

                if (disposition == RepositoryStatusCheckDisposition.RefreshRequired)
                {
                    _workingTreeStatusPending = null;
                    UpdateRefreshIndicator();
                    return;
                }
            }
        }
        finally
        {
            _workingTreeStatusRunning = false;
            StartWorkingTreeStatusLoopIfNeeded();
        }
    }

    private void LatchRefreshRequired(string reason, long generation)
    {
        var disposition = _repositoryRefreshLifecycle.Latch(
            _repositoryRefreshLifecycle.BaselineRevision);
        if (disposition == RepositoryStatusCheckDisposition.StaleRevision)
            return;

        _workingTreeStatusPending = null;
        Trace.WriteLine(
            $"RefreshRequired reason={reason} generation={generation} baselineRevision={_repositoryRefreshLifecycle.BaselineRevision}");
        UpdateRefreshIndicator();
    }

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

        RefreshIcon.Visibility =
            !isRefreshing && state == RefreshIndicatorState.UpToDate
                ? Visibility.Visible
                : Visibility.Collapsed;
        RefreshRequiredIcon.Visibility =
            !isRefreshing && state == RefreshIndicatorState.RefreshRequired
                ? Visibility.Visible
                : Visibility.Collapsed;
        RefreshProgressRing.Visibility = isRefreshing ? Visibility.Visible : Visibility.Collapsed;
        RefreshProgressRing.IsActive = isRefreshing;

        RefreshProgressRing.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Goldenrod);

        ToolTipService.SetToolTip(
            RefreshButton,
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
