using System.ComponentModel;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private const string ExternalRepositoryChangeTooltip = "Repository has changed externally. Refresh to see the latest state.";

    private readonly RepositoryChangeMonitor _repositoryChangeMonitor = new();
    private bool _repositoryChangeMonitoringInitialized;
    private bool _repositoryStateRefreshObserved;
    private Repository? _monitoredRepository;
    private Button? _refreshButton;
    private FontIcon? _refreshIcon;
    private bool _repositoryPresentationRefreshQueued;
    private bool _workingTreePresentationDirty;

    public bool IsRefreshRequired { get; private set; }

    internal void InitializeRepositoryChangeMonitoring()
    {
        if (_repositoryChangeMonitoringInitialized) return;
        _repositoryChangeMonitoringInitialized = true;

        _repositoryChangeMonitor.RepositoryChanged += RepositoryChangeMonitor_RepositoryChanged;
        _viewModel.PropertyChanged += RepositoryRefreshTracking_PropertyChanged;
        RootLayout.Loaded += RootLayout_RefreshIndicatorLoaded;
        UpdateRepositoryChangeMonitor();
        UpdateRefreshIndicator();
    }

    private void ShutdownRepositoryChangeMonitoring()
    {
        if (!_repositoryChangeMonitoringInitialized) return;
        _repositoryChangeMonitoringInitialized = false;

        RootLayout.Loaded -= RootLayout_RefreshIndicatorLoaded;
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

    private void RootLayout_RefreshIndicatorLoaded(object sender, RoutedEventArgs e) => UpdateRefreshIndicator();

    private void UpdateRefreshIndicator()
    {
        if (_refreshButton is null)
        {
            _refreshButton = FindRefreshButton(RootLayout);
            _refreshIcon = _refreshButton is null ? null : FindRefreshIcon(_refreshButton);
        }

        if (_refreshButton is null) return;

        ToolTipService.SetToolTip(
            _refreshButton,
            IsRefreshRequired ? ExternalRepositoryChangeTooltip : null);

        if (_refreshIcon is not null)
            _refreshIcon.Foreground = IsRefreshRequired ? new SolidColorBrush(Microsoft.UI.Colors.Red) : null!;
    }

    private static Button? FindRefreshButton(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && ContainsRefreshText(button)) return button;

            var nested = FindRefreshButton(child);
            if (nested is not null) return nested;
        }

        return null;
    }

    private static bool ContainsRefreshText(DependencyObject root)
    {
        if (root is TextBlock { Text: "Refresh" }) return true;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
            if (ContainsRefreshText(VisualTreeHelper.GetChild(root, i))) return true;

        return false;
    }

    private static FontIcon? FindRefreshIcon(DependencyObject root)
    {
        if (root is FontIcon { Glyph: "\uE72C" } icon) return icon;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var nestedIcon = FindRefreshIcon(VisualTreeHelper.GetChild(root, i));
            if (nestedIcon is not null) return nestedIcon;
        }

        return null;
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
