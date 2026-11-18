using System.Globalization;
using CSharpGit.Application.Abstractions;
using CSharpGit.Infrastructure;
using CSharpGit.Presentation.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace CSharpGit.Presentation.Views;

/// <summary>
/// Owns console layout, subscription, filtered streaming and keyboard interaction.
/// The modal progress dialog is a separate phase-3 consumer via narrow callbacks.
/// </summary>
public sealed partial class GitConsoleHostView : UserControl
{
    private const double DefaultConsoleHeight = 280;
    private const VirtualKey ConsoleShortcutKey = (VirtualKey)0xC0;
    private IGitCommandActivitySource? _activitySource;
    private IAppSettingsService? _settings;
    private Func<GitCommandActivityChangedEventArgs, bool>? _routeFeedback;
    private Action? _renderFeedback;
    private Func<bool>? _modalVisible;
    private UIElement? _shortcutRoot;
    private bool _configured;
    private bool _shutdown;
    private bool _open;
    private double _lastHeight = LoadSavedHeight();
    private double? _dragPointerY;
    private double _dragInitialHeight;

    public GitConsoleHostView() => InitializeComponent();

    internal StatusBarView StatusBar => CommandStatus;
    internal bool IsOpen => _open;

    internal void Configure(IGitCommandActivitySource source,
        IAppSettingsService settings, UIElement shortcutRoot,
        Func<GitCommandActivityChangedEventArgs, bool> routeFeedback,
        Action renderFeedback, Func<bool> modalVisible)
    {
        if (_configured || _shutdown) return;
        _configured = true;
        _activitySource = source ?? throw new ArgumentNullException(nameof(source));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _shortcutRoot = shortcutRoot ?? throw new ArgumentNullException(nameof(shortcutRoot));
        _routeFeedback = routeFeedback ?? throw new ArgumentNullException(nameof(routeFeedback));
        _renderFeedback = renderFeedback ?? throw new ArgumentNullException(nameof(renderFeedback));
        _modalVisible = modalVisible ?? throw new ArgumentNullException(nameof(modalVisible));
        ConsoleView.SetActivityResolver(source.Get);
        ConsoleView.CloseRequested += ConsoleView_CloseRequested;
        ConsoleView.FilterChanged += ConsoleView_FilterChanged;
        CommandStatus.GitCommandStatusClicked += CommandStatus_Clicked;
        source.Changed += ActivitySource_Changed;
        shortcutRoot.KeyDown += ShortcutRoot_KeyDown;
        UpdateStatus();
    }

    private void ConsoleView_CloseRequested(object? sender, EventArgs args) => Close();
    private void ConsoleView_FilterChanged(object? sender, EventArgs args) => Rebuild();
    private void CommandStatus_Clicked(object? sender, EventArgs args) => Toggle(fromStatus: true);

    private void ActivitySource_Changed(object? sender, GitCommandActivityChangedEventArgs e)
    {
        if (_shutdown || !_configured) return;
        var compactServed = _routeFeedback?.Invoke(e) == true;
        if (e.ChangeKind == GitCommandActivityChangeKind.Output)
        {
            if (!_open || e.OutputStream is not { } stream) return;
            ConsoleView.QueueOutput(e.ActivityId, stream, e.OutputChunk ?? string.Empty, e.OutputRequiresResync);
            return;
        }

        if (DispatcherQueue.HasThreadAccess) ApplyActivity(e, compactServed);
        else DispatcherQueue.TryEnqueue(() => ApplyActivity(e, compactServed));
    }

    private void ApplyActivity(GitCommandActivityChangedEventArgs e, bool compactServed)
    {
        if (_shutdown || !_configured || e.Activity is not { } activity) return;
        if (_open)
        {
            if (e.ChangeKind == GitCommandActivityChangeKind.Started)
                ConsoleView.ApplyStarted(activity, e.EvictedActivityId);
            else
                ConsoleView.ApplyLifecycle(activity, e.EvictedActivityId);
        }

        if (ShouldUpdateStatus(e)) UpdateStatus();
        if (activity.CommandKind != GitCommandKind.User || _settings is null) return;

        _renderFeedback?.Invoke();
        var shouldOpen =
            (_settings.GitConsoleAutoOpenMode == GitConsoleAutoOpenMode.Always
                && e.ChangeKind == GitCommandActivityChangeKind.Started)
            || (_settings.GitConsoleAutoOpenMode == GitConsoleAutoOpenMode.OnErrors
                && activity.Status == GitCommandStatus.Failed);
        if (shouldOpen && !compactServed)
            Open(activity.Id, manualOpen: false);
    }

    private bool ShouldUpdateStatus(GitCommandActivityChangedEventArgs e)
    {
        if (_activitySource is null) return false;
        if (ConsoleView.Filter == GitCommandFilter.AllCommands) return true;
        if (e.CommandKind == GitCommandKind.User || e.EvictedCommandKind == GitCommandKind.User) return true;
        return _activitySource.GetLatest(GitCommandFilter.UserCommands) is null;
    }

    private void UpdateStatus()
    {
        if (_shutdown || _activitySource is null) return;
        var activity = _activitySource.GetLatest(ConsoleView.Filter)
            ?? _activitySource.GetLatest(GitCommandFilter.AllCommands);
        StatusBar.SetGitCommandStatus(
            activity is null ? "No Git commands" : StatusSummary(activity),
            activity?.DisplayCommand ?? "No Git commands recorded in this session.");
    }

    private void Rebuild(Guid? preferredSelection = null)
    {
        if (_shutdown || !_open || _activitySource is null) return;
        ConsoleView.SetActivities(
            _activitySource.GetSnapshot(ConsoleView.Filter), preferredSelection);
        UpdateStatus();
    }

    private void Toggle(bool fromStatus)
    {
        if (_open) { Close(); return; }
        if (_activitySource is null) return;
        Guid? id = null;
        if (fromStatus)
        {
            id = (_activitySource.GetLatest(ConsoleView.Filter)
                ?? _activitySource.GetLatest(GitCommandFilter.AllCommands))?.Id;
        }
        Open(id, manualOpen: true);
    }

    internal void Open(Guid? preferredSelection, bool manualOpen)
    {
        if (_shutdown || !_configured || _activitySource is null) return;
        if (_open)
        {
            if (preferredSelection is { } alreadyOpenId)
                ConsoleView.SelectActivity(alreadyOpenId);
            return;
        }

        if (manualOpen && preferredSelection is null)
        {
            var user = _activitySource.GetLatest(GitCommandFilter.UserCommands);
            var latest = user ?? _activitySource.GetLatest(GitCommandFilter.AllCommands);
            preferredSelection = latest?.Id;
            if (user is null && latest is not null)
                ConsoleView.SetFilter(GitCommandFilter.AllCommands);
        }
        else if (preferredSelection is { } selectedId
                 && _activitySource.Get(selectedId) is { CommandKind: GitCommandKind.Internal }
                 && ConsoleView.Filter == GitCommandFilter.UserCommands)
        {
            ConsoleView.SetFilter(GitCommandFilter.AllCommands);
        }

        _open = true;
        HostLayout.RowDefinitions[0].Height = new GridLength(6);
        HostLayout.RowDefinitions[1].Height = new GridLength(Math.Clamp(_lastHeight, 140, 600));
        HeightDragHandle.Visibility = Visibility.Visible;
        ConsoleView.Visibility = Visibility.Visible;
        ConsoleView.SetActive(true);
        Rebuild(preferredSelection);
        _renderFeedback?.Invoke();
    }

    internal void Close()
    {
        if (!_open) return;
        if (HostLayout.RowDefinitions[1].ActualHeight >= 140)
            _lastHeight = HostLayout.RowDefinitions[1].ActualHeight;
        _open = false;
        ConsoleView.SetActive(false);
        ConsoleView.Visibility = Visibility.Collapsed;
        HeightDragHandle.Visibility = Visibility.Collapsed;
        HostLayout.RowDefinitions[0].Height = new GridLength(0);
        HostLayout.RowDefinitions[1].Height = new GridLength(0);
        SaveHeight(_lastHeight);
        _renderFeedback?.Invoke();
    }

    private void ShortcutRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_shutdown || _modalVisible?.Invoke() == true
            || e.Key != ConsoleShortcutKey || !IsConsoleModifierDown()) return;
        Toggle(fromStatus: false);
        e.Handled = true;
    }

    private static bool IsConsoleModifierDown()
    {
        var control = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down)
            == CoreVirtualKeyStates.Down;
        if (control) return true;
        if (!OperatingSystem.IsMacOS()) return false;
        return (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.LeftWindows) & CoreVirtualKeyStates.Down)
                   == CoreVirtualKeyStates.Down
            || (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.RightWindows) & CoreVirtualKeyStates.Down)
                   == CoreVirtualKeyStates.Down;
    }

    private void HeightDragHandle_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!_open || _shortcutRoot is null) return;
        _dragPointerY = args.GetCurrentPoint(_shortcutRoot).Position.Y;
        _dragInitialHeight = HostLayout.RowDefinitions[1].ActualHeight;
        HeightDragHandle.CapturePointer(args.Pointer);
        args.Handled = true;
    }

    private void HeightDragHandle_PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_dragPointerY is not { } start || _shortcutRoot is null) return;
        var current = args.GetCurrentPoint(_shortcutRoot).Position.Y;
        HostLayout.RowDefinitions[1].Height =
            new GridLength(Math.Clamp(_dragInitialHeight + start - current, 140, 600));
        args.Handled = true;
    }

    private void HeightDragHandle_PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_dragPointerY is null) return;
        _dragPointerY = null;
        HeightDragHandle.ReleasePointerCapture(args.Pointer);
        SaveHeight(HostLayout.RowDefinitions[1].ActualHeight);
        args.Handled = true;
    }

    private void HeightDragHandle_PointerCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        if (_dragPointerY is null) return;
        _dragPointerY = null;
        SaveHeight(HostLayout.RowDefinitions[1].ActualHeight);
    }

    private static string SavedHeightPath =>
        Path.Combine(ApplicationDataPaths.Root, "CSharpGit", "console-height.txt");

    private static double LoadSavedHeight()
    {
        try
        {
            if (File.Exists(SavedHeightPath)
                && double.TryParse(File.ReadAllText(SavedHeightPath),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
                return Math.Clamp(height, 140, 600);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return DefaultConsoleHeight;
    }

    private static void SaveHeight(double height)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavedHeightPath)!);
            File.WriteAllText(SavedHeightPath,
                Math.Clamp(height, 140, 600).ToString(CultureInfo.InvariantCulture));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        if (_configured && _activitySource is not null)
            _activitySource.Changed -= ActivitySource_Changed;
        if (_shortcutRoot is not null)
            _shortcutRoot.KeyDown -= ShortcutRoot_KeyDown;
        ConsoleView.CloseRequested -= ConsoleView_CloseRequested;
        ConsoleView.FilterChanged -= ConsoleView_FilterChanged;
        CommandStatus.GitCommandStatusClicked -= CommandStatus_Clicked;
        ConsoleView.SetActive(false);
        StatusBar.Shutdown();
        _dragPointerY = null;
        _open = false;
        _renderFeedback = null;
        _routeFeedback = null;
        _modalVisible = null;
        _configured = false;
        _activitySource = null;
        _settings = null;
        _shortcutRoot = null;
    }

    private static string StatusSummary(GitCommandActivity activity) => activity.Status switch
    {
        GitCommandStatus.Running => $"◌ {activity.DisplayCommand}…",
        GitCommandStatus.Succeeded => $"✓ {activity.DisplayCommand} · {GitConsoleView.FormatDuration(activity.Duration)}",
        GitCommandStatus.Failed => $"✕ {activity.DisplayCommand} · exit {activity.ExitCode}",
        GitCommandStatus.Cancelled => $"○ {activity.DisplayCommand} · cancelled",
        _ => activity.DisplayCommand
    };
}
