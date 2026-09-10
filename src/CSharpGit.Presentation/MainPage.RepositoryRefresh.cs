using System.ComponentModel;
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
    private bool _repositoryChangeMonitoringInitialized;
    private bool _repositoryStateRefreshObserved;
    private bool _isRefreshInProgress;
    private Repository? _monitoredRepository;
    private bool _repositoryPresentationRefreshQueued;
    private bool _workingTreePresentationDirty;

    public bool IsRefreshRequired { get; private set; }

    internal void InitializeRepositoryChangeMonitoring()
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
        _repositoryChangeMonitor.Dispose();
    }

    private void RepositoryRefreshTracking_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.Repository))
        {
            _repositoryStateRefreshObserved = false;
            UpdateRepositoryChangeMonitor();
            return;
        }

        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.HeadDisplay))
        {
            _repositoryStateRefreshObserved = true;
            return;
        }

        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.IsBusy)
            && !_viewModel.IsBusy
            && _repositoryStateRefreshObserved)
        {
            _repositoryStateRefreshObserved = false;
            AcknowledgeRepositoryRefresh();
        }
    }

    private void UpdateRepositoryChangeMonitor()
    {
        var repository = _viewModel.Repository;
        if (repository is null)
        {
            _monitoredRepository = null;
            _repositoryChangeMonitor.Stop();
            SetRefreshRequired(false);
            return;
        }

        if (Equals(repository, _monitoredRepository)) return;

        _monitoredRepository = repository;
        _repositoryChangeMonitor.Start(repository);
        SetRefreshRequired(false);
    }

    private void RepositoryChangeMonitor_RepositoryChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => SetRefreshRequired(true));
    }

    private void AcknowledgeRepositoryRefresh()
    {
        if (_viewModel.Repository is null) return;
        _repositoryChangeMonitor.Acknowledge();
        SetRefreshRequired(false);
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
        _workingTreePresentationDirty |= workingTreeChanged;
        if (_repositoryPresentationRefreshQueued) return;

        _repositoryPresentationRefreshQueued = true;
        if (DispatcherQueue.TryEnqueue(FlushRepositoryPresentationRefresh)) return;

        FlushRepositoryPresentationRefresh();
    }

    private void FlushRepositoryPresentationRefresh()
    {
        _repositoryPresentationRefreshQueued = false;
        var refreshWorkingTree = _workingTreePresentationDirty;
        _workingTreePresentationDirty = false;

        if (refreshWorkingTree)
        {
            RefreshPresentationCollections();
            return;
        }

        RebuildRepositoryTree();
        UpdateStatusBar();
    }
}
