using CSharpGit.Application.Abstractions;
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

    private void InitializeHistoryPerformanceDiagnostics()
    {
        UpdateHistoryRenderingMode();
        UpdateHistoryPerformanceDiagnosticsAvailability();
    }

    private async void StartHistoryPerformanceCapture_Click(object sender, RoutedEventArgs e)
    {
        if (!_recentRepositorySettings.HistoryPerformanceDiagnosticsEnabled
            || _viewModel.Repository is null
            || HistoryPerformanceDiagnostics.IsCaptureActive)
            return;

        var layout = ActiveCommitGraphLayout;
        var activeRows = _viewModel.History.Rows.Count;
        var hasMore = _viewModel.History.HasMore;
        var context = new HistoryPerformanceStartContext(
            _viewModel.History.IsReferenceScoped ? "scopedReference" : "main",
            activeRows,
            activeRows,
            100,
            "topo-order",
            hasMore,
            _recentRepositorySettings.ShowReflog,
            !string.IsNullOrWhiteSpace(_viewModel.History.FilterText),
            _viewModel.Branches.LocalBranches.Count,
            _viewModel.Branches.RemoteBranches.Count,
            _viewModel.Tags.Items.Count,
            _viewModel.Branches.LocalBranches.Count + _viewModel.Branches.RemoteBranches.Count + _viewModel.Tags.Items.Count,
            layout.GraphWidth,
            layout.ObservedMaxLaneCount,
            _recentRepositorySettings.ShowAuthorAvatars,
            _recentRepositorySettings.OnlineAvatarLookupEnabled,
            _recentRepositorySettings.HistoryRenderingMode,
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

    private void StopHistoryPerformanceCapture_Click(object sender, RoutedEventArgs e) =>
        RunPageUiOperation(
            () => StopHistoryPerformanceCaptureAsync(HistoryPerformanceStopReason.Manual, showResult: true),
            "Manual history capture stop");

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

    private void HistoryPerformanceTimeoutTimer_Tick(DispatcherQueueTimer sender, object args) =>
        RunPageUiOperation(
            () => StopHistoryPerformanceCaptureAsync(HistoryPerformanceStopReason.Timeout, showResult: false),
            "History capture timeout");

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
        if (!HistoryPerformanceDiagnostics.IsCaptureBusy)
        {
            if (!IsShuttingDown) UpdateHistoryPerformanceDiagnosticsAvailability();
            return;
        }

        DetachHistoryPerformanceSubscriptions();
        StopHistoryPerformanceTimers();

        HistoryPerformanceCaptureFiles? result;
        try
        {
            result = await HistoryPerformanceDiagnostics.StopAsync(reason);
        }
        catch (Exception exception)
        {
            ReportUiFailure(exception, "History capture stop");
            if (showResult && !IsShuttingDown)
                await ShowErrorAsync("Could not save History performance capture",
                    "The diagnostics capture could not be saved.");
            return;
        }

        if (IsShuttingDown) return;
        UpdateHistoryPerformanceDiagnosticsAvailability();
        if (!showResult || result is null || XamlRoot is null) return;

        var succeeded = result.Value.Outcome == HistoryPerformanceCaptureOutcome.Success;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = succeeded ? "History performance capture saved" : "History performance capture incomplete",
            Content = succeeded
                ? result.Value.SummaryPath
                : "The capture could not be fully saved. Check the diagnostics folder for partial files.",
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close
        };
        try { await ShowPageDialogAsync(dialog); }
        catch (Exception exception)
        {
            ReportUiFailure(exception, "History capture result notification");
        }
    }

    private void HandleHistoryPerformanceSettingsChanged()
    {
        HistoryRenderDiagnostics.SettingsChanged();
        UpdateHistoryRenderingMode();
        if (!_recentRepositorySettings.HistoryPerformanceDiagnosticsEnabled
            && HistoryPerformanceDiagnostics.IsCaptureActive)
        {
            RunPageUiOperation(
                () => StopHistoryPerformanceCaptureAsync(HistoryPerformanceStopReason.DiagnosticsDisabled, showResult: false),
                "Disabled history diagnostics");
        }

        UpdateHistoryPerformanceDiagnosticsAvailability();
    }

    private void HandleHistoryPerformanceRepositoryChanged()
    {
        UpdateHistoryPerformanceDiagnosticsAvailability();
        if (!HistoryPerformanceDiagnostics.IsCaptureActive)
            return;

        HistoryRenderDiagnostics.RepositoryChanged();
        RunPageUiOperation(
            () => StopHistoryPerformanceCaptureAsync(
                _viewModel.Repository is null
                    ? HistoryPerformanceStopReason.RepositoryClosed
                    : HistoryPerformanceStopReason.RepositoryChanged,
                showResult: false),
            "Repository history capture stop");
    }

    private Task StopHistoryPerformanceCaptureOnShutdown()
    {
        if (!HistoryPerformanceDiagnostics.IsCaptureBusy) return Task.CompletedTask;
        DetachHistoryPerformanceSubscriptions();
        StopHistoryPerformanceTimers();
        return HistoryPerformanceDiagnostics.StopAsync(HistoryPerformanceStopReason.ApplicationShutdown);
    }

    private void UpdateHistoryRenderingMode()
    {
        if (HistoryList is null)
            return;

        var resourceKey = _recentRepositorySettings.HistoryRenderingMode switch
        {
            HistoryRenderingMode.SubjectOnly => "SimplifiedHistoryItemTemplate",
            HistoryRenderingMode.TextColumns => "HistoryTextColumnsItemTemplate",
            HistoryRenderingMode.TextAndGraph => "HistoryTextGraphItemTemplate",
            HistoryRenderingMode.TextGraphAndReferences => "HistoryTextGraphReferencesItemTemplate",
            HistoryRenderingMode.Full => "HistoryItemTemplate",
            _ => "HistoryItemTemplate"
        };
        if (Microsoft.UI.Xaml.Application.Current.Resources[resourceKey] is DataTemplate template)
            HistoryList.ItemTemplate = template;
    }

    private void UpdateHistoryPerformanceDiagnosticsAvailability()
    {
        if (HistoryPerformanceControls is null)
            return;

        var enabled = _recentRepositorySettings.HistoryPerformanceDiagnosticsEnabled;
        var active = HistoryPerformanceDiagnostics.IsCaptureActive;
        var busy = HistoryPerformanceDiagnostics.IsCaptureBusy;
        HistoryPerformanceControls.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        StartHistoryPerformanceCaptureMenuItem.IsEnabled =
            enabled && !busy && !IsShuttingDown && _viewModel.Repository is not null;
        StopHistoryPerformanceCaptureMenuItem.IsEnabled = enabled && active;
        OpenHistoryPerformanceDiagnosticsFolderMenuItem.IsEnabled = enabled;
        UpdateHistoryPerformanceStatus();
    }
}
