using System.ComponentModel;
using System.Diagnostics;
using CSharpGit.Domain;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Controls.CommitGraph;
using CSharpGit.Presentation.Diagnostics;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace CSharpGit.Presentation.Views;

/// <summary>
/// Owns history filtering, display mode and virtual-scroll subscription for one window.
/// Commit Details remains a separate phase-3 UI; performance dialogs are transitional callbacks.
/// </summary>
public sealed partial class HistoryView : UserControl, ICommitGraphLayoutProvider
{
    private const double LoadMoreThreshold = 68;
    private HistoryViewModel? _history;
    private WorkspaceNavigator? _navigator;
    private Func<bool>? _isBusy;
    private ScrollViewer? _scrollViewer;
    private bool _loadMorePending;
    private bool _syncingScope;
    private bool _configured;
    private bool _disposed;

    public HistoryView() => InitializeComponent();

    internal ListView List => HistoryList;
    internal TextBox FilterBox => HistoryFilter;
    internal SelectorBar ScopeSelector => ScopeCombo;
    internal SelectorBarItem CurrentScope => CurrentScopeItem;
    internal SelectorBarItem AllScope => AllScopeItem;
    internal SelectorBarItem ReflogScope => ShowReflogToggle;
    internal StackPanel ReferencePanel => ReferenceScopePanel;
    internal TextBlock ReferenceLabel => ActiveReferenceText;
    internal StackPanel PerformanceControls => HistoryPerformanceControls;
    internal TextBlock PerformanceCaptureStatus => HistoryPerformanceCaptureStatus;
    internal MenuFlyoutItem StartCaptureAction => StartHistoryPerformanceCaptureMenuItem;
    internal MenuFlyoutItem StopCaptureAction => StopHistoryPerformanceCaptureMenuItem;
    internal MenuFlyoutItem OpenDiagnosticsAction => OpenHistoryPerformanceDiagnosticsFolderMenuItem;
    internal ColumnDefinition GraphHeaderColumn => HistoryGraphHeaderColumn;
    internal Border ColumnHeader => HistoryColumnHeader;
    internal Border FilterToolbar => HistoryFilterToolbar;
    internal Button LoadMoreButton => LoadMoreHistoryButton;

    internal void Configure(HistoryViewModel history, WorkspaceNavigator navigator,
        Func<bool> isBusy)
    {
        if (_configured || _disposed) return;
        _configured = true;
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _isBusy = isBusy ?? throw new ArgumentNullException(nameof(isBusy));
        _history.PropertyChanged += History_PropertyChanged;
        HistoryList.Loaded += HistoryList_Loaded;
        LoadMoreHistoryButton.Visibility = Visibility.Collapsed;
        SyncDisplayMode();
        UpdateScopePresentation();
        UpdateLoadMoreAvailability();
        InitializeScroll();
    }

    internal void InitializeScroll()
    {
        if (_disposed || !_configured) return;
        HistoryList.ApplyTemplate();
        AttachScrollViewer();
    }

    private void HistoryList_Loaded(object sender, RoutedEventArgs e) => AttachScrollViewer();

    private void AttachScrollViewer()
    {
        if (_disposed) return;
        var viewer = FindScrollViewer(HistoryList);
        if (ReferenceEquals(viewer, _scrollViewer)) return;
        if (_scrollViewer is not null)
            _scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
        _scrollViewer = viewer;
        if (_scrollViewer is not null)
            _scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
    }

    private void ScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_disposed) return;
        HistoryRenderDiagnostics.HistoryViewChanged(Stopwatch.GetTimestamp(), e.IsIntermediate);
        if (sender is not ScrollViewer viewer || _loadMorePending
            || viewer.ScrollableHeight <= 0
            || viewer.ScrollableHeight - viewer.VerticalOffset > LoadMoreThreshold)
            return;
        HistoryRenderDiagnostics.LoadMoreThresholdReached();
        if (_history is null || !_history.HasMore || _history.IsLoading || _isBusy?.Invoke() == true)
            return;

        RunHistoryOperation(LoadMoreAsync);
    }

    private async Task LoadMoreAsync()
    {
        if (_disposed || _loadMorePending || _history is null) return;
        _loadMorePending = true;
        HistoryRenderDiagnostics.PageLoadStarted();
        try { await _history.LoadMoreAsync(); }
        finally { _loadMorePending = false; }
    }

    private void LoadMoreHistory_Click(object sender, RoutedEventArgs e) =>
        RunHistoryOperation(LoadMoreAsync);

    private void HistoryFilter_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_disposed || e.Key != VirtualKey.Enter || _history is null) return;
        e.Handled = true;
        RunHistoryOperation(_history.RefreshAsync);
    }

    private void ClearReference_Click(object sender, RoutedEventArgs e)
    {
        if (_navigator is null) return;
        RunHistoryOperation(async () =>
        {
            var selected = await _navigator.ShowAllHistoryAsync();
            if (_disposed || selected is null) return;
            ScrollTo(selected);
        });
    }

    private void HistoryDisplayModeSelector_Loaded(object sender, RoutedEventArgs e) =>
        SyncDisplayMode();

    private void HistoryDisplayModeSelector_Unloaded(object sender, RoutedEventArgs e)
    {
        // A transient Unloaded is not a final dispose; keep the model subscription
        // until the owner calls Shutdown.
    }

    private void History_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || _history is null) return;
        if (e.PropertyName is nameof(HistoryViewModel.SelectedScope)
            or nameof(HistoryViewModel.ShowReflog))
            SyncDisplayMode();
        if (e.PropertyName is nameof(HistoryViewModel.IsReferenceScoped)
            or nameof(HistoryViewModel.ActiveReferenceLabel))
            UpdateScopePresentation();
        if (e.PropertyName is nameof(HistoryViewModel.HasMore)
            or nameof(HistoryViewModel.IsLoading))
            UpdateLoadMoreAvailability();
    }

    private void HistoryDisplayModeSelector_SelectionChanged(
        SelectorBar sender, SelectorBarSelectionChangedEventArgs e)
    {
        if (_disposed || _syncingScope || _history is null || sender.SelectedItem is null)
            return;
        var mode = ReferenceEquals(sender.SelectedItem, CurrentScopeItem)
            ? HistoryDisplayMode.CurrentBranch
            : ReferenceEquals(sender.SelectedItem, ShowReflogToggle)
                ? HistoryDisplayMode.AllReferencesWithReflog
                : HistoryDisplayMode.AllReferences;
        _history.SetHistoryDisplayMode(mode);
    }

    private void SyncDisplayMode()
    {
        if (_disposed || _history is null) return;
        var item = _history.HistoryDisplayMode switch
        {
            HistoryDisplayMode.CurrentBranch => CurrentScopeItem,
            HistoryDisplayMode.AllReferencesWithReflog => ShowReflogToggle,
            _ => AllScopeItem
        };
        if (ReferenceEquals(ScopeCombo.SelectedItem, item)) return;
        _syncingScope = true;
        try { ScopeCombo.SelectedItem = item; }
        finally { _syncingScope = false; }
    }

    internal void UpdateScopePresentation()
    {
        if (_disposed || _history is null) return;
        ShowReflogToggle.IsEnabled = !_history.IsReferenceScoped;
        ScopeCombo.Visibility = _history.IsReferenceScoped ? Visibility.Collapsed : Visibility.Visible;
        ReferenceScopePanel.Visibility = _history.IsReferenceScoped ? Visibility.Visible : Visibility.Collapsed;
        ActiveReferenceText.Text = _history.ActiveReferenceLabel ?? string.Empty;
    }

    internal void UpdateLoadMoreAvailability()
    {
        if (_disposed || _history is null) return;
        LoadMoreHistoryButton.IsEnabled = _history.LoadMoreCommand.CanExecute(null);
    }

    internal void ScrollTo(HistoryRow row)
    {
        if (_disposed || !HistoryList.Items.Contains(row)) return;
        HistoryList.SelectedItem = row;
        HistoryList.ScrollIntoView(row, ScrollIntoViewAlignment.Leading);
    }

    private void RunHistoryOperation(Func<Task> operation)
    {
        if (_disposed) return;
        _ = RunAsync();

        async Task RunAsync()
        {
            try { await operation(); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"History view operation: {ex}");
            }
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer) return viewer;
            var nested = FindScrollViewer(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    internal void Shutdown()
    {
        if (_disposed) return;
        StopPerformanceUiSubscriptions();
        _disposed = true;
        if (_history is not null)
            _history.PropertyChanged -= History_PropertyChanged;
        HistoryList.Loaded -= HistoryList_Loaded;
        if (_scrollViewer is not null)
            _scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
        _scrollViewer = null;
        if (_commitGraphLayoutInitialized && _history is not null)
            _history.Rows.CollectionChanged -= HistoryRows_CollectionChanged;
        LayoutChanged = null;
        _history = null;
        _navigator = null;
        _isBusy = null;
    }
}
