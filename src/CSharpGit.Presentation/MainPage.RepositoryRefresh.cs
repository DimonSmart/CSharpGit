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
    private readonly CancellationTokenSource _repositoryProbeStop = new();
    private CancellationToken _repositoryProbeToken;
    private readonly IRepositoryRefreshProbe _repositoryRefreshProbe;
    private readonly RepositoryRefreshLifecycleState _repositoryRefreshLifecycle = new();
    private bool _repositoryChangeMonitoringInitialized;
    private bool _isRefreshInProgress;
    private Repository? _monitoredRepository;
    private RepositoryRefreshFingerprint? _displayedRefreshFingerprint;
    private bool _repositoryProbeRunning;
    private bool _repositoryProbePending;
    private long _repositoryProbeGeneration;
    private bool _repositoryPresentationRefreshQueued;
    private bool _repositoryTreePresentationDirty;
    private bool _workingTreePresentationDirty;

    public bool IsRefreshRequired => _repositoryRefreshLifecycle.IsRefreshRequired;

    private void InitializeRepositoryChangeMonitoring()
    {
        if (IsShuttingDown || _repositoryChangeMonitoringInitialized) return;
        _repositoryProbeToken = _repositoryProbeStop.Token;
        _repositoryChangeMonitoringInitialized = true;

        _repositoryChangeMonitor.RepositoryChanged += RepositoryChangeMonitor_RepositoryChanged;
        _viewModel.PropertyChanged += RepositoryRefreshTracking_PropertyChanged;
        UpdateRepositoryChangeMonitor();
        UpdateRefreshIndicator();
    }

    private void ShutdownRepositoryChangeMonitoring()
    {
        if (!_repositoryChangeMonitoringInitialized) return;
        _repositoryChangeMonitoringInitialized = false;
        _repositoryProbePending = false;

        _viewModel.PropertyChanged -= RepositoryRefreshTracking_PropertyChanged;
        _repositoryChangeMonitor.RepositoryChanged -= RepositoryChangeMonitor_RepositoryChanged;
        _repositoryProbeStop.Cancel();
        _repositoryChangeMonitor.Dispose();
        _repositoryProbeStop.Dispose();
    }

    private void RepositoryRefreshTracking_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;

        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.Repository))
        {
            UpdateRepositoryChangeMonitor();
            return;
        }

        if (eventArgs.PropertyName != nameof(OpenRepositoryViewModel.DisplayedRefreshBaselineRevision)) return;

        var baseline = _viewModel.DisplayedRefreshFingerprint;
        if (baseline is null) return;

        var baselineRevision = _viewModel.DisplayedRefreshBaselineRevision;
        if (!_repositoryRefreshLifecycle.PublishBaseline(baselineRevision)) return;

        _displayedRefreshFingerprint = baseline;
        _repositoryProbePending = false;
        _repositoryChangeMonitor.Resume();
        UpdateRefreshIndicator();
        QueueRepositoryProbe(_repositoryChangeMonitor.Generation);
    }

    private void UpdateRepositoryChangeMonitor()
    {
        if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;

        var repository = _viewModel.Repository;
        if (repository is null)
        {
            _monitoredRepository = null;
            _displayedRefreshFingerprint = null;
            _repositoryProbePending = false;
            _repositoryRefreshLifecycle.Reset(_viewModel.DisplayedRefreshBaselineRevision);
            _repositoryChangeMonitor.Stop();
            UpdateRefreshIndicator();
            return;
        }

        if (Equals(repository, _monitoredRepository)) return;

        _monitoredRepository = repository;
        _displayedRefreshFingerprint = _viewModel.DisplayedRefreshFingerprint;
        _repositoryProbePending = false;
        _repositoryRefreshLifecycle.Reset(_viewModel.DisplayedRefreshBaselineRevision);
        _repositoryChangeMonitor.Start(repository);
        UpdateRefreshIndicator();

        if (_displayedRefreshFingerprint is not null)
            QueueRepositoryProbe(_repositoryChangeMonitor.Generation);
    }

    private void RepositoryChangeMonitor_RepositoryChanged(
        object? sender,
        RepositoryInvalidatedEventArgs eventArgs)
    {
        if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;

        Trace.WriteLine(
            $"Repository monitor invalidated: source={eventArgs.Source} path={eventArgs.Path ?? "<unknown>"} generation={eventArgs.Generation}");

        if (DispatcherQueue.HasThreadAccess)
        {
            QueueRepositoryProbe(eventArgs.Generation);
            return;
        }

        DispatcherQueue.TryEnqueue(() => QueueRepositoryProbe(eventArgs.Generation));
    }

    private void QueueRepositoryProbe(long generation)
    {
        if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;
        if (!_repositoryRefreshLifecycle.CanQueueProbe) return;
        if (_viewModel.Repository is null || _displayedRefreshFingerprint is null) return;

        _repositoryProbeGeneration = Math.Max(_repositoryProbeGeneration, generation);
        _repositoryProbePending = true;
        if (_repositoryProbeRunning) return;

        _repositoryProbeRunning = true;
        _ = RunRepositoryProbeLoopAsync();
    }

    private async Task RunRepositoryProbeLoopAsync()
    {
        try
        {
            while (!IsShuttingDown &&
                   _repositoryChangeMonitoringInitialized &&
                   _repositoryProbePending)
            {
                _repositoryProbePending = false;
                var generation = _repositoryProbeGeneration;
                var repository = _viewModel.Repository;
                var baseline = _displayedRefreshFingerprint;
                var baselineRevision = _repositoryRefreshLifecycle.BaselineRevision;
                if (repository is null || baseline is null) continue;

                RepositoryRefreshFingerprint current;
                try
                {
                    current = await _repositoryRefreshProbe.ReadAsync(repository, _repositoryProbeToken);
                }
                catch (OperationCanceledException) when (_repositoryProbeToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;
                    Trace.WriteLine(
                        $"Repository state probe: generation={generation} result=error error={exception.GetType().Name}: {exception.Message}");
                    continue;
                }

                if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;

                if (!ReferenceEquals(repository, _viewModel.Repository))
                    continue;

                var changed = !StringComparer.Ordinal.Equals(current.Value, baseline.Value);
                var disposition = _repositoryRefreshLifecycle.ApplyProbeResult(baselineRevision, changed);
                if (disposition == RepositoryProbeResultDisposition.StaleRevision)
                {
                    Trace.WriteLine(
                        $"Repository state probe: generation={generation} result=stale baselineRevision={baselineRevision} currentRevision={_repositoryRefreshLifecycle.BaselineRevision}");
                    continue;
                }

                if (disposition == RepositoryProbeResultDisposition.IgnoredWhileLatched)
                    continue;

                Trace.WriteLine(
                    $"Repository state probe: generation={generation} result={(changed ? "changed" : "unchanged")} baselineRevision={baselineRevision}");
                if (IsShuttingDown || !_repositoryChangeMonitoringInitialized) return;

                if (disposition == RepositoryProbeResultDisposition.RefreshRequired)
                {
                    _repositoryProbePending = false;
                    _repositoryChangeMonitor.Suspend();
                    UpdateRefreshIndicator();
                    return;
                }
            }
        }
        finally
        {
            _repositoryProbeRunning = false;
            if (!IsShuttingDown &&
                _repositoryChangeMonitoringInitialized &&
                _repositoryProbePending)
            {
                _repositoryProbeRunning = true;
                _ = RunRepositoryProbeLoopAsync();
            }
        }
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

        RefreshIcon.Visibility = isRefreshing ? Visibility.Collapsed : Visibility.Visible;
        RefreshProgressRing.Visibility = isRefreshing ? Visibility.Visible : Visibility.Collapsed;
        RefreshProgressRing.IsActive = isRefreshing;

        RefreshIcon.Foreground = new SolidColorBrush(
            state == RefreshIndicatorState.RefreshRequired
                ? Microsoft.UI.Colors.Red
                : Microsoft.UI.Colors.LimeGreen);
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
