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
    private readonly IRepositoryRefreshProbe _repositoryRefreshProbe;
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

    public bool IsRefreshRequired { get; private set; }

    private void InitializeRepositoryChangeMonitoring()
    {
        if (_repositoryChangeMonitoringInitialized) return;
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

        _viewModel.PropertyChanged -= RepositoryRefreshTracking_PropertyChanged;
        _repositoryChangeMonitor.RepositoryChanged -= RepositoryChangeMonitor_RepositoryChanged;
        _repositoryProbeStop.Cancel();
        _repositoryChangeMonitor.Dispose();
        _repositoryProbeStop.Dispose();
    }

    private void RepositoryRefreshTracking_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.Repository))
        {
            UpdateRepositoryChangeMonitor();
            return;
        }

        if (eventArgs.PropertyName != nameof(OpenRepositoryViewModel.DisplayedRefreshFingerprint)) return;

        _displayedRefreshFingerprint = _viewModel.DisplayedRefreshFingerprint;
        if (_displayedRefreshFingerprint is null) return;

        SetRefreshRequired(false);
        QueueRepositoryProbe(_repositoryChangeMonitor.Generation);
    }

    private void UpdateRepositoryChangeMonitor()
    {
        var repository = _viewModel.Repository;
        if (repository is null)
        {
            _monitoredRepository = null;
            _displayedRefreshFingerprint = null;
            _repositoryChangeMonitor.Stop();
            SetRefreshRequired(false);
            return;
        }

        if (Equals(repository, _monitoredRepository)) return;

        _monitoredRepository = repository;
        _displayedRefreshFingerprint = null;
        _repositoryChangeMonitor.Start(repository);
        SetRefreshRequired(false);
    }

    private void RepositoryChangeMonitor_RepositoryChanged(
        object? sender,
        RepositoryInvalidatedEventArgs eventArgs)
    {
        Trace.WriteLine(
            $"Repository monitor invalidated: source={eventArgs.Source} path={eventArgs.Path ?? "<unknown>"} generation={eventArgs.Generation}");
        DispatcherQueue.TryEnqueue(() => QueueRepositoryProbe(eventArgs.Generation));
    }

    private void QueueRepositoryProbe(long generation)
    {
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
            while (_repositoryProbePending)
            {
                _repositoryProbePending = false;
                var generation = _repositoryProbeGeneration;
                var repository = _viewModel.Repository;
                var baseline = _displayedRefreshFingerprint;
                if (repository is null || baseline is null) continue;

                RepositoryRefreshFingerprint current;
                try
                {
                    current = await _repositoryRefreshProbe.ReadAsync(repository, _repositoryProbeStop.Token);
                }
                catch (OperationCanceledException) when (_repositoryProbeStop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Trace.WriteLine(
                        $"Repository state probe: generation={generation} result=error error={exception.GetType().Name}: {exception.Message}");
                    continue;
                }

                if (!ReferenceEquals(repository, _viewModel.Repository) ||
                    !Equals(baseline, _displayedRefreshFingerprint))
                    continue;

                var changed = !StringComparer.Ordinal.Equals(current.Value, baseline.Value);
                Trace.WriteLine(
                    $"Repository state probe: generation={generation} result={(changed ? "changed" : "unchanged")}");
                SetRefreshRequired(changed);
            }
        }
        finally
        {
            _repositoryProbeRunning = false;
            if (_repositoryProbePending)
            {
                _repositoryProbeRunning = true;
                _ = RunRepositoryProbeLoopAsync();
            }
        }
    }

    private void SetRefreshRequired(bool value)
    {
        if (IsRefreshRequired == value) return;
        IsRefreshRequired = value;
        UpdateRefreshIndicator();
    }

    private void SetRefreshInProgress(bool value)
    {
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
        if (workingTreeChanged) _workingTreePresentationDirty = true;
        else _repositoryTreePresentationDirty = true;
        if (_repositoryPresentationRefreshQueued) return;

        _repositoryPresentationRefreshQueued = true;
        if (DispatcherQueue.TryEnqueue(FlushRepositoryPresentationRefresh)) return;

        FlushRepositoryPresentationRefresh();
    }

    private void FlushRepositoryPresentationRefresh()
    {
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
}
