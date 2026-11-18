using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Diagnostics;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Views;

public sealed partial class HistoryView
{
    private static readonly TimeSpan HistoryPerformanceMaximumDuration = TimeSpan.FromMinutes(10);
    private DispatcherQueueTimer? _historyPerformanceStatusTimer;
    private DispatcherQueueTimer? _historyPerformanceTimeoutTimer;
    private bool _historyPerformanceSubscriptionsAttached;
    private OpenRepositoryViewModel _repository = null!;
    private IAppSettingsService _perfSettings = null!;
    private IGitCommandActivitySource _activitySource = null!;
    private IAuthorAvatarService _avatarService = null!;
    private IDesktopShellService _desktopShell = null!;
    private Func<string, string, Task> _showError = null!;
    private Func<ContentDialog, Task> _showDialog = null!;
    private Action<Exception, string> _reportFailure = null!;
    private bool _perfConfigured;

    internal void ConfigurePerformance(OpenRepositoryViewModel repository,
        IAppSettingsService settings, IGitCommandActivitySource activitySource,
        IAuthorAvatarService avatarService, IDesktopShellService desktopShell,
        Func<string, string, Task> showError,
        Func<ContentDialog, Task> showDialog,
        Action<Exception, string> reportFailure)
    {
        if (_perfConfigured || _disposed) return;
        _perfConfigured = true;
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _perfSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));
        _avatarService = avatarService ?? throw new ArgumentNullException(nameof(avatarService));
        _desktopShell = desktopShell ?? throw new ArgumentNullException(nameof(desktopShell));
        _showError = showError ?? throw new ArgumentNullException(nameof(showError));
        _showDialog = showDialog ?? throw new ArgumentNullException(nameof(showDialog));
        _reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        settings.Changed += PerformanceSettings_Changed;
        repository.PropertyChanged += PerformanceRepository_PropertyChanged;
        InitializeHistoryPerformanceDiagnostics();
    }

    private void PerformanceSettings_Changed(object? sender, EventArgs args)
    {
        if (_disposed) return;
        if (DispatcherQueue.HasThreadAccess) HandleHistoryPerformanceSettingsChanged();
        else DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed) HandleHistoryPerformanceSettingsChanged();
        });
    }

    private void PerformanceRepository_PropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (!_disposed && args.PropertyName == nameof(OpenRepositoryViewModel.Repository))
            HandleHistoryPerformanceRepositoryChanged();
    }

    private void RunHistoryOperation(Func<Task> work, string description) =>
        RunHistoryOperation(work);

    private void StopPerformanceUiSubscriptions()
    {
        if (!_perfConfigured) return;
        _perfSettings.Changed -= PerformanceSettings_Changed;
        _repository.PropertyChanged -= PerformanceRepository_PropertyChanged;
        DetachHistoryPerformanceSubscriptions();
        StopHistoryPerformanceTimers();
        _perfConfigured = false;
    }

    internal void InitializeHistoryPerformanceDiagnostics()
    {
        UpdateHistoryRenderingMode();
        UpdateHistoryPerformanceDiagnosticsAvailability();
    }

    private void StartHistoryPerformanceCapture_Click(object sender, RoutedEventArgs e) =>
        RunHistoryOperation(StartHistoryPerformanceCaptureAsync, "Start history diagnostics");

    private async Task StartHistoryPerformanceCaptureAsync()
    {
        if (!_perfSettings.HistoryPerformanceDiagnosticsEnabled
            || _repository.Repository is null
            || HistoryPerformanceDiagnostics.IsCaptureActive)
            return;

        var layout = Layout;
        var activeRows = _repository.History.Rows.Count;
        var hasMore = _repository.History.HasMore;
        var context = new HistoryPerformanceStartContext(
            _repository.History.IsReferenceScoped ? "scopedReference" : "main",
            activeRows,
            activeRows,
            100,
            "topo-order",
            hasMore,
            _perfSettings.ShowReflog,
            !string.IsNullOrWhiteSpace(_repository.History.FilterText),
            _repository.Branches.LocalBranches.Count,
            _repository.Branches.RemoteBranches.Count,
            _repository.Tags.Items.Count,
            _repository.Branches.LocalBranches.Count + _repository.Branches.RemoteBranches.Count + _repository.Tags.Items.Count,
            layout.GraphWidth,
            layout.ObservedMaxLaneCount,
            _perfSettings.ShowAuthorAvatars,
            _perfSettings.OnlineAvatarLookupEnabled,
            _perfSettings.HistoryRenderingMode,
            ActualWidth,
            ActualHeight,
            XamlRoot?.RasterizationScale);

        try
        {
            if (!await HistoryPerformanceDiagnostics.StartAsync(context, _activitySource, _avatarService))
                return;

            AttachHistoryPerformanceSubscriptions();
            StartHistoryPerformanceTimers();
            UpdateHistoryPerformanceDiagnosticsAvailability();
        }
        catch (Exception exception)
        {
            await _showError("Could not start History performance capture", exception.Message);
            UpdateHistoryPerformanceDiagnosticsAvailability();
        }
    }

    private void StopHistoryPerformanceCapture_Click(object sender, RoutedEventArgs e) =>
        RunHistoryOperation(
            () => StopHistoryPerformanceCaptureAsync(HistoryPerformanceStopReason.Manual, showResult: true),
            "Manual history capture stop");

    private void OpenHistoryPerformanceDiagnosticsFolder_Click(object sender, RoutedEventArgs e) =>
        RunHistoryOperation(OpenHistoryPerformanceDiagnosticsFolderAsync, "Open diagnostics folder");

    private async Task OpenHistoryPerformanceDiagnosticsFolderAsync()
    {
        try
        {
            Directory.CreateDirectory(HistoryPerformanceDiagnostics.DiagnosticsDirectory);
            await _desktopShell.OpenFolderAsync(HistoryPerformanceDiagnostics.DiagnosticsDirectory);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await _showError("Could not open History diagnostics folder", exception.Message);
        }
    }

    internal void AttachHistoryPerformanceSubscriptions()
    {
        if (_historyPerformanceSubscriptionsAttached) return;
        _historyPerformanceSubscriptionsAttached = true;
        HistoryList.ContainerContentChanging += HistoryList_PerformanceContainerContentChanging;
        HistoryList.SelectionChanged += HistoryList_PerformanceSelectionChanged;
        SizeChanged += HistoryView_PerformanceSizeChanged;
    }

    internal void DetachHistoryPerformanceSubscriptions()
    {
        if (!_historyPerformanceSubscriptionsAttached) return;
        _historyPerformanceSubscriptionsAttached = false;
        HistoryList.ContainerContentChanging -= HistoryList_PerformanceContainerContentChanging;
        HistoryList.SelectionChanged -= HistoryList_PerformanceSelectionChanged;
        SizeChanged -= HistoryView_PerformanceSizeChanged;
    }

    private static void HistoryList_PerformanceContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args) =>
        HistoryRenderDiagnostics.ContainerContentChanged(args.ItemIndex, args.InRecycleQueue);

    private static void HistoryList_PerformanceSelectionChanged(object sender, SelectionChangedEventArgs args) =>
        HistoryRenderDiagnostics.SelectionChanged();

    private static void HistoryView_PerformanceSizeChanged(object sender, SizeChangedEventArgs args) =>
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
        RunHistoryOperation(
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
            if (!_disposed) UpdateHistoryPerformanceDiagnosticsAvailability();
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
            _reportFailure(exception, "History capture stop");
            if (showResult && !_disposed)
                await _showError("Could not save History performance capture",
                    "The diagnostics capture could not be saved.");
            return;
        }

        if (_disposed) return;
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
        try { await _showDialog(dialog); }
        catch (Exception exception)
        {
            _reportFailure(exception, "History capture result notification");
        }
    }

    internal void HandleHistoryPerformanceSettingsChanged()
    {
        HistoryRenderDiagnostics.SettingsChanged();
        UpdateHistoryRenderingMode();
        if (!_perfSettings.HistoryPerformanceDiagnosticsEnabled
            && HistoryPerformanceDiagnostics.IsCaptureActive)
        {
            RunHistoryOperation(
                () => StopHistoryPerformanceCaptureAsync(HistoryPerformanceStopReason.DiagnosticsDisabled, showResult: false),
                "Disabled history diagnostics");
        }

        UpdateHistoryPerformanceDiagnosticsAvailability();
    }

    internal void HandleHistoryPerformanceRepositoryChanged()
    {
        UpdateHistoryPerformanceDiagnosticsAvailability();
        if (!HistoryPerformanceDiagnostics.IsCaptureActive)
            return;

        HistoryRenderDiagnostics.RepositoryChanged();
        RunHistoryOperation(
            () => StopHistoryPerformanceCaptureAsync(
                _repository.Repository is null
                    ? HistoryPerformanceStopReason.RepositoryClosed
                    : HistoryPerformanceStopReason.RepositoryChanged,
                showResult: false),
            "Repository history capture stop");
    }

    internal Task StopHistoryPerformanceCaptureOnShutdown()
    {
        if (!HistoryPerformanceDiagnostics.IsCaptureBusy) return Task.CompletedTask;
        DetachHistoryPerformanceSubscriptions();
        StopHistoryPerformanceTimers();
        return HistoryPerformanceDiagnostics.StopAsync(HistoryPerformanceStopReason.ApplicationShutdown);
    }

    internal void UpdateHistoryRenderingMode()
    {
        if (HistoryList is null)
            return;

        var resourceKey = _perfSettings.HistoryRenderingMode switch
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

        var enabled = _perfSettings.HistoryPerformanceDiagnosticsEnabled;
        var active = HistoryPerformanceDiagnostics.IsCaptureActive;
        var busy = HistoryPerformanceDiagnostics.IsCaptureBusy;
        HistoryPerformanceControls.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        StartHistoryPerformanceCaptureMenuItem.IsEnabled =
            enabled && !busy && !_disposed && _repository.Repository is not null;
        StopHistoryPerformanceCaptureMenuItem.IsEnabled = enabled && active;
        OpenHistoryPerformanceDiagnosticsFolderMenuItem.IsEnabled = enabled;
        UpdateHistoryPerformanceStatus();
    }
}
