using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private static readonly TimeSpan HistoryPerformanceMaximumDuration = TimeSpan.FromMinutes(10);
    private DispatcherQueueTimer? _historyPerformanceStatusTimer;
    private DispatcherQueueTimer? _historyPerformanceTimeoutTimer;
    private bool _historyPerformanceSubscriptionsAttached;

    private void InitializeHistoryPerformanceDiagnostics() =>
        UpdateHistoryPerformanceDiagnosticsAvailability();

    private async void StartHistoryPerformanceCapture_Click(object sender, RoutedEventArgs e)
    {
        if (!_recentRepositorySettings.HistoryPerformanceDiagnosticsEnabled
            || _viewModel.Repository is null
            || HistoryPerformanceDiagnostics.IsCaptureActive)
            return;

        var layout = ActiveCommitGraphLayout;
        var activeRows = ReferenceEquals(HistoryList.ItemsSource, _scopedHistory)
            ? _scopedHistory.Count
            : _viewModel.History.Count;
        var hasMore = _activeReference is not null ? _scopedHasMore : _viewModel.HasMore;
        var context = new HistoryPerformanceStartContext(
            _activeReference is null ? "main" : "scopedReference",
            activeRows,
            activeRows,
            null,
            hasMore,
            _recentRepositorySettings.ShowReflog,
            !string.IsNullOrWhiteSpace(_viewModel.FilterText),
            _viewModel.LocalBranches.Count,
            _viewModel.RemoteBranches.Count,
            _viewModel.Tags.Count,
            _viewModel.LocalBranches.Count + _viewModel.RemoteBranches.Count + _viewModel.Tags.Count,
            layout.GraphWidth,
            layout.ObservedMaxLaneCount,
            _recentRepositorySettings.ShowAuthorAvatars,
            _recentRepositorySettings.OnlineAvatarLookupEnabled,
            RootLayout.ActualWidth,
            RootLayout.ActualHeight,
            XamlRoot?.RasterizationScale);

        try
        {
            if (!await HistoryPerformanceDiagnostics.StartAsync(context, _gitCommandActivitySource, _authorAvatarService))
                return;

            AttachHistoryPerformanceSubscriptions();
            StartHistoryPerformanceTimers();
            UpdateHistoryPerformanceDiagnosticsAvailability();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not start History performance capture", exception.Message);
            UpdateHistoryPerformanceDiagnosticsAvailability();
        }
    }

    private async void StopHistoryPerformanceCapture_Click(object sender, RoutedEventArgs e) =>
        await StopHistoryPerformanceCaptureAsync(HistoryPerformanceStopReason.Manual, showResult: true);

    private async void OpenHistoryPerformanceDiagnosticsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(HistoryPerformanceDiagnostics.DiagnosticsDirectory);
            await _desktopShellService.OpenFolderAsync(HistoryPerformanceDiagnostics.DiagnosticsDirectory);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not open History diagnostics folder", exception.Message);
        }
    }

    private void AttachHistoryPerformanceSubscriptions()
    {
        if (_historyPerformanceSubscriptionsAttached) return;
        _historyPerformanceSubscriptionsAttached = true;
        HistoryList.ContainerContentChanging += HistoryList_PerformanceContainerContentChanging;
        HistoryList.SelectionChanged += HistoryList_PerformanceSelectionChanged;
        RootLayout.SizeChanged += RootLayout_PerformanceSizeChanged;
    }

    private void DetachHistoryPerformanceSubscriptions()
    {
        if (!_historyPerformanceSubscriptionsAttached) return;
        _historyPerformanceSubscriptionsAttached = false;
        HistoryList.ContainerContentChanging -= HistoryList_PerformanceContainerContentChanging;
        HistoryList.SelectionChanged -= HistoryList_PerformanceSelectionChanged;
        RootLayout.SizeChanged -= RootLayout_PerformanceSizeChanged;
    }

    private static void HistoryList_PerformanceContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args) =>
        HistoryRenderDiagnostics.ContainerContentChanged(args.ItemIndex, args.InRecycleQueue);

    private static void HistoryList_PerformanceSelectionChanged(object sender, SelectionChangedEventArgs args) =>
        HistoryRenderDiagnostics.SelectionChanged();

    private static void RootLayout_PerformanceSizeChanged(object sender, SizeChangedEventArgs args) =>
        HistoryRenderDiagnostics.WindowResized();

    private void StartHistoryPerformanceTimers()
    {
        StopHistoryPerformanceTimers();

        _historyPerformanceStatusTimer = DispatcherQueue.CreateTimer();
        _historyPerformanceStatusTimer.Interval = TimeSpan.FromMilliseconds(500);
        _historyPerformanceStatusTimer.IsRepeating = true;
        _historyPerformanceStatusTimer.Tick += HistoryPerformanceStatusTimer_Tick;
        _historyPerformanceStatusTimer.Start();

        _historyPerformanceTimeoutTimer = DispatcherQueue.CreateTimer();
        _historyPerformanceTimeoutTimer.Interval = HistoryPerformanceMaximumDuration;
        _historyPerformanceTimeoutTimer.IsRepeating = false;
        _historyPerformanceTimeoutTimer.Tick += HistoryPerformanceTimeoutTimer_Tick;
        _historyPerformanceTimeoutTimer.Start();

        UpdateHistoryPerformanceStatus();
    }

    private void StopHistoryPerformanceTimers()
    {
        if (_historyPerformanceStatusTimer is { } statusTimer)
        {
            statusTimer.Stop();
            statusTimer.Tick -= HistoryPerformanceStatusTimer_Tick;
            _historyPerformanceStatusTimer = null;
        }

        if (_historyPerformanceTimeoutTimer is { } timeoutTimer)
        {
            timeoutTimer.Stop();
            timeoutTimer.Tick -= HistoryPerformanceTimeoutTimer_Tick;
            _historyPerformanceTimeoutTimer = null;
        }
    }

    private void HistoryPerformanceStatusTimer_Tick(DispatcherQueueTimer sender, object args) =>
        UpdateHistoryPerformanceStatus();

    private async void HistoryPerformanceTimeoutTimer_Tick(DispatcherQueueTimer sender, object args) =>
        await StopHistoryPerformanceCaptureAsync(HistoryPerformanceStopReason.Timeout, showResult: false);

    private void UpdateHistoryPerformanceStatus()
    {
        if (HistoryPerformanceDiagnostics.ActiveSession is not { } session)
        {
            HistoryPerformanceCaptureStatus.Visibility = Visibility.Collapsed;
            HistoryPerformanceCaptureStatus.Text = string.Empty;
            return;
        }

        HistoryPerformanceCaptureStatus.Text = $"PERF CAPTURE  {session.Elapsed:mm\\:ss}";
        HistoryPerformanceCaptureStatus.Visibility = Visibility.Visible;
    }

    private async Task StopHistoryPerformanceCaptureAsync(
        HistoryPerformanceStopReason reason,
        bool showResult)
    {
        if (!HistoryPerformanceDiagnostics.IsCaptureActive)
        {
            UpdateHistoryPerformanceDiagnosticsAvailability();
            return;
        }

        DetachHistoryPerformanceSubscriptions();
        StopHistoryPerformanceTimers();
        var result = await HistoryPerformanceDiagnostics.StopAsync(reason);
        UpdateHistoryPerformanceDiagnosticsAvailability();

        if (!showResult || result is null || XamlRoot is null)
            return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "History performance capture saved",
            Content = result.Value.SummaryPath,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private void HandleHistoryPerformanceSettingsChanged()
    {
        HistoryRenderDiagnostics.SettingsChanged();
        if (!_recentRepositorySettings.HistoryPerformanceDiagnosticsEnabled
            && HistoryPerformanceDiagnostics.IsCaptureActive)
        {
            _ = StopHistoryPerformanceCaptureAsync(
                HistoryPerformanceStopReason.DiagnosticsDisabled,
                showResult: false);
        }

        UpdateHistoryPerformanceDiagnosticsAvailability();
    }

    private void HandleHistoryPerformanceRepositoryChanged()
    {
        UpdateHistoryPerformanceDiagnosticsAvailability();
        if (!HistoryPerformanceDiagnostics.IsCaptureActive)
            return;

        HistoryRenderDiagnostics.RepositoryChanged();
        _ = StopHistoryPerformanceCaptureAsync(
            _viewModel.Repository is null
                ? HistoryPerformanceStopReason.RepositoryClosed
                : HistoryPerformanceStopReason.RepositoryChanged,
            showResult: false);
    }

    private void StopHistoryPerformanceCaptureOnShutdown()
    {
        if (!HistoryPerformanceDiagnostics.IsCaptureActive)
            return;

        DetachHistoryPerformanceSubscriptions();
        StopHistoryPerformanceTimers();
        HistoryPerformanceDiagnostics
            .StopAsync(HistoryPerformanceStopReason.ApplicationShutdown)
            .GetAwaiter()
            .GetResult();
    }

    private void UpdateHistoryPerformanceDiagnosticsAvailability()
    {
        if (HistoryPerformanceControls is null)
            return;

        var enabled = _recentRepositorySettings.HistoryPerformanceDiagnosticsEnabled;
        var active = HistoryPerformanceDiagnostics.IsCaptureActive;
        HistoryPerformanceControls.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        StartHistoryPerformanceCaptureMenuItem.IsEnabled =
            enabled && !active && _viewModel.Repository is not null;
        StopHistoryPerformanceCaptureMenuItem.IsEnabled = enabled && active;
        OpenHistoryPerformanceDiagnosticsFolderMenuItem.IsEnabled = enabled;
        UpdateHistoryPerformanceStatus();
    }
}
