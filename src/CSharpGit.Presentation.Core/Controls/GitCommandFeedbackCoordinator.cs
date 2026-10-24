using System.Text;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.Controls;

internal enum GitCommandFeedbackOwner
{
    Floating,
    EmbeddedClone
}

internal sealed record GitCommandFeedbackSnapshot(
    Guid ActivityId,
    GitCommandFeedbackOwner Owner,
    string DisplayCommand,
    GitCommandStatus Status,
    int? ExitCode,
    TimeSpan Duration,
    string PreviewText,
    bool PreviewTruncated,
    bool IsVisible,
    bool CanOpenExactConsoleActivity)
{
    public string StatusText => Status switch
    {
        GitCommandStatus.Running => "Running…",
        GitCommandStatus.Succeeded => "Completed successfully",
        GitCommandStatus.Failed => "Failed",
        GitCommandStatus.Cancelled => "Cancelled",
        _ => string.Empty
    };
}

/// <summary>
/// Window-owned, thread-safe projection of the existing sanitized activity source.
/// The UI calls Capture at a fixed cadence; output events never enqueue a UI callback.
/// Clock injection makes show delay and auto-close testable without real timers.
/// </summary>
internal sealed class GitCommandFeedbackCoordinator
{
    internal const int ShowDelayMilliseconds = 150;
    internal const int DefaultSuccessDurationSeconds = 5;
    internal const int MaximumSuccessDurationSeconds = 30;
    internal const int MaximumPreviewBytes = 32 * 1024;

    private readonly object _gate = new();
    private readonly IGitCommandActivitySource _source;
    private readonly Func<DateTimeOffset> _clock;
    private readonly GitOutputDisplayBuffer _preview = new();

    private GitCommandActivity? _current;
    private GitCommandFeedbackOwner _owner;
    private bool _enabled;
    private bool _autoClose;
    private int _successDurationSeconds = DefaultSuccessDurationSeconds;
    private bool _visible;
    private bool _dismissed;
    private bool _hovered;
    private bool _focused;
    private bool _truncated;
    private bool _listedInConsole;
    private DateTimeOffset _showAt;
    private DateTimeOffset _nextReconciliationAt;
    private DateTimeOffset _lastCloseTick;
    private TimeSpan _closeRemaining = TimeSpan.FromSeconds(DefaultSuccessDurationSeconds);

    public GitCommandFeedbackCoordinator(IGitCommandActivitySource source, Func<DateTimeOffset>? clock = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public void Configure(bool enabled, bool autoClose, int successDurationSeconds = DefaultSuccessDurationSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(successDurationSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(successDurationSeconds, MaximumSuccessDurationSeconds);
        lock (_gate)
        {
            if (!enabled)
            {
                _enabled = false;
                _current = null;
                _dismissed = true;
                _visible = false;
                _preview.Reset(string.Empty);
                return;
            }

            if (_autoClose != autoClose || _successDurationSeconds != successDurationSeconds)
            {
                _closeRemaining = TimeSpan.FromSeconds(successDurationSeconds);
                _lastCloseTick = _clock();
            }
            _enabled = enabled;
            _autoClose = autoClose;
            _successDurationSeconds = successDurationSeconds;
        }
    }

    /// <returns>Whether this invocation is actually routed to compact feedback.</returns>
    public bool Handle(GitCommandActivityChangedEventArgs change, bool cloneDialogActive)
    {
        lock (_gate)
        {
            if (change.EvictedActivityId == _current?.Id)
                _listedInConsole = false;
            if (!_enabled || change.CommandKind != GitCommandKind.User)
                return false;

            if (change.ChangeKind == GitCommandActivityChangeKind.Output)
            {
                if (_current?.Id != change.ActivityId || _dismissed)
                    return false;

                if (change.OutputRequiresResync)
                {
                    if (_source.Get(change.ActivityId) is { } stored)
                        ResetOutput(stored);
                }
                else if (change.OutputChunk is { Length: > 0 } chunk)
                    AppendOutput(chunk);
                return true;
            }

            if (change.Activity is not { } activity)
                return false;

            if (change.ChangeKind == GitCommandActivityChangeKind.Started)
            {
                if (_current?.Id == activity.Id)
                    return !_dismissed;
                if (cloneDialogActive && !IsClone(activity))
                    return false;

                if (_current is { Status: GitCommandStatus.Failed } &&
                    _visible && !_dismissed)
                    return false;

                if (_current is not null && activity.StartedAt < _current.StartedAt)
                    return false;

                Select(activity, cloneDialogActive && IsClone(activity)
                    ? GitCommandFeedbackOwner.EmbeddedClone
                    : GitCommandFeedbackOwner.Floating);
                return true;
            }

            if (_current?.Id == activity.Id && !_dismissed)
            {
                // Terminal state is irreversible even if a delayed event arrives.
                if (_current.Status != GitCommandStatus.Running)
                    return true;
                ApplyLifecycle(activity);
                return true;
            }

            // A new failure supersedes an unread old failure; other invisible
            // completions remain in the existing Git Console and error UI.
            if (activity.Status == GitCommandStatus.Failed &&
                (_current is null || activity.StartedAt >= _current.StartedAt))
            {
                Select(activity, cloneDialogActive && IsClone(activity)
                    ? GitCommandFeedbackOwner.EmbeddedClone
                    : GitCommandFeedbackOwner.Floating);
                return true;
            }

            return false;
        }
    }

    public GitCommandFeedbackSnapshot? Capture()
    {
        lock (_gate)
        {
            if (!_enabled || _dismissed)
                return null;

            var now = _clock();
            ReconcileTerminalState(now);
            if (_current is null)
                return null;
            if (!_visible && now >= _showAt)
            {
                _visible = true;
                _lastCloseTick = now;
                _closeRemaining = TimeSpan.FromSeconds(_successDurationSeconds);
            }

            if (_visible && _autoClose && _current.Status == GitCommandStatus.Succeeded)
            {
                if (!_hovered && !_focused)
                    _closeRemaining -= now - _lastCloseTick;
                _lastCloseTick = now;
                if (_closeRemaining <= TimeSpan.Zero)
                {
                    _visible = false;
                    _dismissed = true;
                }
            }
            else
                _lastCloseTick = now;

            var duration = _current.CompletedAt is null
                ? now - _current.StartedAt
                : _current.Duration;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
            var text = _preview.Text;
            if (text.Length == 0)
                text = _current.Status == GitCommandStatus.Running ? "No output yet" : "No output";
            if (_truncated)
                text = "Showing recent output. Open Git Console for details.\n" + text;

            return new GitCommandFeedbackSnapshot(
                _current.Id, _owner, _current.DisplayCommand,
                _current.Status, _current.ExitCode, duration,
                text, _truncated, _visible && !_dismissed,
                _listedInConsole);
        }
    }

    public bool IsServing(Guid activityId)
    {
        lock (_gate)
            return _enabled && !_dismissed && _current?.Id == activityId;
    }

    public void Interacting(bool hovered, bool focused)
    {
        lock (_gate)
        {
            if (!_hovered && !_focused || !hovered && !focused)
                _lastCloseTick = _clock();
            _hovered = hovered;
            _focused = focused;
        }
    }

    public void Dismiss(Guid id)
    {
        lock (_gate)
        {
            if (_current?.Id != id) return;
            _dismissed = true;
            _visible = false;
        }
    }

    // A delayed Started event or a missed terminal notification must not
    // leave Running visible after the authoritative command has completed.
    private void ReconcileTerminalState(DateTimeOffset now)
    {
        if (_current is not { Status: GitCommandStatus.Running } running
            || now < _nextReconciliationAt)
            return;

        _nextReconciliationAt = now.AddSeconds(1);
        var latest = _source.Get(running.Id);
        if (latest is null)
        {
            // An evicted activity has no trustworthy terminal status.
            _current = null;
            _visible = false;
            return;
        }

        if (latest.Status != GitCommandStatus.Running)
            ApplyLifecycle(latest);
    }

    private void ApplyLifecycle(GitCommandActivity activity)
    {
        _current = activity;
        ResetOutput(activity); // Includes the final sanitized stream fragments.
        if (activity.Status == GitCommandStatus.Succeeded)
        {
            _closeRemaining = TimeSpan.FromSeconds(_successDurationSeconds);
            _lastCloseTick = _clock();
        }
    }

    private static bool IsClone(GitCommandActivity activity) =>
        activity.Arguments.Count > 0 &&
        string.Equals(activity.Arguments[0], "clone", StringComparison.Ordinal);

    private void Select(GitCommandActivity activity, GitCommandFeedbackOwner owner)
    {
        _current = activity;
        _listedInConsole = _source.GetSnapshot(GitCommandFilter.AllCommands)
            .Any(item => item.Id == activity.Id);
        _owner = owner;
        _dismissed = false;
        _visible = false;
        _hovered = false;
        _focused = false;
        _truncated = false;
        _showAt = _clock().AddMilliseconds(ShowDelayMilliseconds);
        _nextReconciliationAt = _clock();
        _closeRemaining = TimeSpan.FromSeconds(_successDurationSeconds);
        _lastCloseTick = _clock();
        ResetOutput(activity);
    }

    private void ResetOutput(GitCommandActivity activity)
    {
        _preview.Reset(string.Empty);
        _truncated = false;
        AppendOutput(activity.StandardOutput);
        AppendOutput(activity.StandardError);
    }

    private void AppendOutput(string text)
    {
        if (text.Length == 0) return;
        // Most Git progress records terminate in CR, so replacement happens
        // before bounding the preview. Very large individual chunks are bounded too.
        _preview.Append(text);
        var displayed = _preview.Text;
        if (Encoding.UTF8.GetByteCount(displayed) <= MaximumPreviewBytes)
            return;

        var start = displayed.Length;
        var totalBytes = 0;
        // Walk backwards by Unicode scalars: O(32 KiB) rather than repeatedly
        // measuring every suffix of a potentially megabyte-sized capture.
        while (start > 0)
        {
            var candidate = start - 1;
            if (candidate > 0 && char.IsLowSurrogate(displayed[candidate]) &&
                char.IsHighSurrogate(displayed[candidate - 1]))
                candidate--;
            var count = Encoding.UTF8.GetByteCount(displayed.AsSpan(candidate, start - candidate));
            if (totalBytes + count > MaximumPreviewBytes) break;
            totalBytes += count;
            start = candidate;
        }

        var nextLine = displayed.IndexOf('\n', start);
        if (nextLine >= 0 && nextLine - start < MaximumPreviewBytes / 2)
            start = nextLine + 1;
        _preview.Reset(displayed[start..]);
        if (text.EndsWith('\r'))
            _preview.Append("\r");
        _truncated = true;
    }
}
