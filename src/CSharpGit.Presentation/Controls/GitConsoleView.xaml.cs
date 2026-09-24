using System.Text;
using CSharpGit.Application.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace CSharpGit.Presentation.Controls;

public sealed partial class GitConsoleView : UserControl
{
    internal const int OutputThrottleMilliseconds = 75;

    private readonly GitCommandConsoleState _state = new();
    private readonly GitOutputDisplayBuffer _standardOutputDisplay = new();
    private readonly GitOutputDisplayBuffer _standardErrorDisplay = new();
    private readonly object _outputSync = new();
    private readonly StringBuilder _pendingStandardOutput = new();
    private readonly StringBuilder _pendingStandardError = new();
    private readonly System.Threading.Timer _outputTimer;
    private readonly DispatcherTimer _durationTimer;

    private Func<Guid, GitCommandActivity?>? _activityResolver;
    private Guid? _selectedActivityId;
    private Guid? _pendingActivityId;
    private bool _pendingStandardOutputResync;
    private bool _pendingStandardErrorResync;
    private bool _outputFlushScheduled;
    private bool _updatingFilter;
    private int _scrollRequestVersion;
    private volatile bool _active;

    public GitConsoleView()
    {
        InitializeComponent();
        CommandList.ItemsSource = _state.Items;
        FilterComboBox.ItemsSource = new[] { "User commands", "All commands" };
        FilterComboBox.SelectedIndex = 0;

        _outputTimer = new System.Threading.Timer(OutputTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
        _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _durationTimer.Tick += DurationTimer_Tick;

        UpdateEmptyState();
        ShowDetails(null);
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? FilterChanged;

    public GitCommandFilter Filter => FilterComboBox.SelectedIndex == 1
        ? GitCommandFilter.AllCommands
        : GitCommandFilter.UserCommands;

    public Guid? SelectedActivityId
    {
        get
        {
            lock (_outputSync)
                return _selectedActivityId;
        }
    }

    public GitCommandActivity? SelectedActivity =>
        SelectedActivityId is { } id ? _activityResolver?.Invoke(id) : null;

    public void SetActivityResolver(Func<Guid, GitCommandActivity?> activityResolver)
    {
        ArgumentNullException.ThrowIfNull(activityResolver);
        _activityResolver = activityResolver;
    }

    public void SetActive(bool active)
    {
        _active = active;
        if (!active)
        {
            lock (_outputSync)
            {
                _outputTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _outputFlushScheduled = false;
                ClearPendingOutputLocked();
            }
            _durationTimer.Stop();
            return;
        }

        UpdateDurationTimer();
    }

    public void SetFilter(GitCommandFilter filter)
    {
        var index = filter == GitCommandFilter.AllCommands ? 1 : 0;
        if (FilterComboBox.SelectedIndex == index) return;
        _updatingFilter = true;
        FilterComboBox.SelectedIndex = index;
        _updatingFilter = false;
    }

    public void SetActivities(IReadOnlyList<GitCommandActivity> activities, Guid? preferredSelection = null)
    {
        var selectedId = preferredSelection ?? SelectedActivityId;
        CancelPendingOutput();
        InvalidateScheduledScroll();
        _state.Reset(Filter, activities);

        var selectedItem = _state.ResolveSelection(selectedId);
        CommandList.SelectedItem = selectedItem;
        if (selectedItem is null)
            ApplySelection(null);

        UpdateEmptyState();
        UpdateDurationTimer();
        if (selectedItem is not null)
            ScheduleScrollToSelectedActivity(selectedItem.Id);
    }

    public void ApplyStarted(GitCommandActivity activity, Guid? evictedActivityId)
    {
        var selectedId = SelectedActivityId;
        _state.ApplyStarted(activity, evictedActivityId);
        RestoreSelectionAfterIncrementalChange(selectedId);
        UpdateEmptyState();
        UpdateDurationTimer();
    }

    public void ApplyLifecycle(GitCommandActivity activity, Guid? evictedActivityId = null)
    {
        if (SelectedActivityId == activity.Id)
            FlushPendingOutputImmediately(activity.Id);

        var selectedId = SelectedActivityId;
        _state.ApplyLifecycle(activity, evictedActivityId);
        RestoreSelectionAfterIncrementalChange(selectedId);

        if (SelectedActivityId == activity.Id)
            UpdateSelectedMetadata(activity);

        UpdateEmptyState();
        UpdateDurationTimer();
    }

    public void QueueOutput(
        Guid activityId,
        GitOutputStream stream,
        string chunk,
        bool requiresResync)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        lock (_outputSync)
        {
            if (!_active || _selectedActivityId != activityId) return;

            if (_pendingActivityId != activityId)
            {
                ClearPendingOutputLocked();
                _pendingActivityId = activityId;
            }

            if (stream == GitOutputStream.StandardOutput)
            {
                if (chunk.Length > 0) _pendingStandardOutput.Append(chunk);
                _pendingStandardOutputResync |= requiresResync;
            }
            else
            {
                if (chunk.Length > 0) _pendingStandardError.Append(chunk);
                _pendingStandardErrorResync |= requiresResync;
            }

            if (_outputFlushScheduled) return;
            _outputFlushScheduled = true;
            _outputTimer.Change(OutputThrottleMilliseconds, Timeout.Infinite);
        }
    }

    public bool SelectActivity(Guid id)
    {
        var item = _state.Find(id);
        if (item is null) return false;
        if (CommandList.SelectedItem is not GitCommandConsoleItem selected || selected.Id != id)
            CommandList.SelectedItem = item;
        ScheduleScrollToSelectedActivity(id);
        return true;
    }

    private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFilter) return;
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CommandList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        InvalidateScheduledScroll();
        ApplySelection(CommandList.SelectedItem as GitCommandConsoleItem);
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        if (CommandList.SelectedItem is GitCommandConsoleItem item)
            CopyText(item.CommandText);
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedActivityId is not { } id || _activityResolver?.Invoke(id) is not { } activity) return;
        CopyText(GitConsoleClipboardText.Build(activity));
    }

    private void ApplySelection(GitCommandConsoleItem? item)
    {
        lock (_outputSync)
        {
            _selectedActivityId = item?.Id;
            _outputTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _outputFlushScheduled = false;
            ClearPendingOutputLocked();
        }

        var activity = item is null ? null : _activityResolver?.Invoke(item.Id);
        ShowDetails(activity);
    }

    private void RestoreSelectionAfterIncrementalChange(Guid? selectedId)
    {
        var selectedItem = _state.ResolveSelection(selectedId);
        if (CommandList.SelectedItem is GitCommandConsoleItem current &&
            selectedItem is not null &&
            current.Id == selectedItem.Id)
            return;

        CommandList.SelectedItem = selectedItem;
        if (selectedItem is null)
            ApplySelection(null);
    }

    private void ScheduleScrollToSelectedActivity(Guid expectedActivityId)
    {
        var requestVersion = ++_scrollRequestVersion;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (requestVersion != _scrollRequestVersion ||
                SelectedActivityId != expectedActivityId ||
                CommandList.SelectedItem is not GitCommandConsoleItem selected ||
                selected.Id != expectedActivityId)
                return;

            var item = _state.Find(expectedActivityId);
            if (item is not null)
                CommandList.ScrollIntoView(item);
        });
    }

    private void InvalidateScheduledScroll() => _scrollRequestVersion++;

    private void OutputTimerElapsed(object? state)
    {
        if (!DispatcherQueue.TryEnqueue(FlushPendingOutputOnUiThread))
        {
            lock (_outputSync)
                _outputFlushScheduled = false;
        }
    }

    private void FlushPendingOutputImmediately(Guid activityId)
    {
        lock (_outputSync)
        {
            if (_pendingActivityId != activityId) return;
            _outputTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        FlushPendingOutputOnUiThread();
    }

    private void FlushPendingOutputOnUiThread()
    {
        PendingOutput pending;
        lock (_outputSync)
        {
            _outputFlushScheduled = false;
            if (!_active || _pendingActivityId is not { } activityId || _selectedActivityId != activityId)
            {
                ClearPendingOutputLocked();
                return;
            }

            pending = new PendingOutput(
                activityId,
                _pendingStandardOutput.ToString(),
                _pendingStandardError.ToString(),
                _pendingStandardOutputResync,
                _pendingStandardErrorResync);
            ClearPendingOutputLocked();
        }

        if (SelectedActivityId != pending.ActivityId) return;

        var snapshot = (pending.StandardOutputResync || pending.StandardErrorResync)
            ? _activityResolver?.Invoke(pending.ActivityId)
            : null;

        if (pending.StandardOutputResync)
        {
            if (snapshot is not null)
                ResetOutputText(StandardOutputText, _standardOutputDisplay, snapshot.StandardOutput);
        }
        else
        {
            ApplyOutputChunk(StandardOutputText, _standardOutputDisplay, pending.StandardOutput);
        }

        if (pending.StandardErrorResync)
        {
            if (snapshot is not null)
                ResetOutputText(StandardErrorText, _standardErrorDisplay, snapshot.StandardError);
        }
        else
        {
            ApplyOutputChunk(StandardErrorText, _standardErrorDisplay, pending.StandardError);
        }
    }

    private void CancelPendingOutput()
    {
        lock (_outputSync)
        {
            _outputTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _outputFlushScheduled = false;
            ClearPendingOutputLocked();
        }
    }

    private void ClearPendingOutputLocked()
    {
        _pendingActivityId = null;
        _pendingStandardOutput.Clear();
        _pendingStandardError.Clear();
        _pendingStandardOutputResync = false;
        _pendingStandardErrorResync = false;
    }

    private static void ApplyOutputChunk(TextBox textBox, GitOutputDisplayBuffer display, string chunk)
    {
        if (chunk.Length == 0) return;
        var delta = display.Append(chunk);
        if (!delta.HasChange) return;

        if (delta.ReplaceFrom is { } replaceFrom)
        {
            textBox.Select(replaceFrom, textBox.Text.Length - replaceFrom);
            textBox.SelectedText = delta.Text;
            return;
        }

        textBox.Select(textBox.Text.Length, 0);
        textBox.SelectedText = delta.Text;
    }

    private static void ResetOutputText(TextBox textBox, GitOutputDisplayBuffer display, string rawText) =>
        textBox.Text = display.Reset(rawText);

    private void ShowDetails(GitCommandActivity? activity)
    {
        var hasActivity = activity is not null;
        CommandText.Text = activity?.DisplayCommand ?? string.Empty;
        WorkingDirectoryText.Text = activity?.WorkingDirectory ?? string.Empty;
        StartedText.Text = activity is null ? string.Empty : activity.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        DurationText.Text = activity is null ? string.Empty : FormatDuration(activity.Duration);
        ExitCodeText.Text = activity?.ExitCode?.ToString() ?? (activity?.Status == GitCommandStatus.Running ? "running" : string.Empty);
        ResetOutputText(StandardOutputText, _standardOutputDisplay, activity?.StandardOutput ?? string.Empty);
        ResetOutputText(StandardErrorText, _standardErrorDisplay, activity?.StandardError ?? string.Empty);
        StandardOutputText.IsEnabled = hasActivity;
        StandardErrorText.IsEnabled = hasActivity;
    }

    private void UpdateSelectedMetadata(GitCommandActivity activity)
    {
        DurationText.Text = FormatDuration(activity.Duration);
        ExitCodeText.Text = activity.ExitCode?.ToString() ?? (activity.Status == GitCommandStatus.Running ? "running" : string.Empty);
    }

    private void DurationTimer_Tick(object? sender, object e)
    {
        if (!_active)
        {
            _durationTimer.Stop();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var item in _state.Items)
            item.UpdateRunningDuration(now);

        if (CommandList.SelectedItem is GitCommandConsoleItem { Status: GitCommandStatus.Running } selected)
            DurationText.Text = FormatDuration(selected.Duration);

        UpdateDurationTimer();
    }

    private void UpdateDurationTimer()
    {
        var shouldRun = _active && _state.Items.Any(item => item.Status == GitCommandStatus.Running);
        if (shouldRun)
        {
            if (!_durationTimer.IsEnabled) _durationTimer.Start();
        }
        else if (_durationTimer.IsEnabled)
        {
            _durationTimer.Stop();
        }
    }

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = _state.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CommandList.Visibility = _state.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    internal static string FormatDuration(TimeSpan duration) =>
        GitCommandConsoleItem.FormatDuration(duration);

    private readonly record struct PendingOutput(
        Guid ActivityId,
        string StandardOutput,
        string StandardError,
        bool StandardOutputResync,
        bool StandardErrorResync);
}
