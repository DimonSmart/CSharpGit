using System.ComponentModel;
using System.Runtime.CompilerServices;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.Controls;

internal sealed class GitCommandConsoleItem : INotifyPropertyChanged
{
    private GitCommandStatus _status;
    private TimeSpan _duration;
    private int? _exitCode;

    public GitCommandConsoleItem(GitCommandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        Id = activity.Id;
        StartedAt = activity.StartedAt;
        CommandKind = activity.CommandKind;
        CommandText = activity.DisplayCommand;
        StartedText = activity.StartedAt.ToLocalTime().ToString("HH:mm:ss");
        _status = activity.Status;
        _duration = activity.Duration;
        _exitCode = activity.ExitCode;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }
    public DateTimeOffset StartedAt { get; }
    public GitCommandKind CommandKind { get; }
    public string StartedText { get; }
    public string CommandText { get; }
    public GitCommandStatus Status => _status;
    public TimeSpan Duration => _duration;
    public int? ExitCode => _exitCode;

    public string StatusGlyph => _status switch
    {
        GitCommandStatus.Running => "◌",
        GitCommandStatus.Succeeded => "✓",
        GitCommandStatus.Failed => "✕",
        GitCommandStatus.Cancelled => "○",
        _ => string.Empty
    };

    public string DurationText => _status switch
    {
        GitCommandStatus.Cancelled => "cancelled",
        _ => FormatDuration(_duration)
    };

    public void Apply(GitCommandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.Id != Id)
            throw new ArgumentException("The activity id does not match this presentation item.", nameof(activity));

        var statusChanged = _status != activity.Status;
        var durationChanged = _duration != activity.Duration;
        var exitCodeChanged = _exitCode != activity.ExitCode;

        _status = activity.Status;
        _duration = activity.Duration;
        _exitCode = activity.ExitCode;

        if (statusChanged)
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusGlyph));
        }
        if (statusChanged || durationChanged)
        {
            OnPropertyChanged(nameof(Duration));
            OnPropertyChanged(nameof(DurationText));
        }
        if (exitCodeChanged)
            OnPropertyChanged(nameof(ExitCode));
    }

    public void UpdateRunningDuration(DateTimeOffset now)
    {
        if (_status != GitCommandStatus.Running) return;
        var duration = now - StartedAt;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        if (_duration == duration) return;
        _duration = duration;
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(DurationText));
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMilliseconds < 1000)
            return $"{Math.Max(0, duration.TotalMilliseconds):0} ms";
        if (duration.TotalSeconds < 60)
            return $"{duration.TotalSeconds:0.##} s";

        var totalSeconds = Math.Max(0, (int)Math.Round(duration.TotalSeconds));
        return $"{totalSeconds / 60}m {totalSeconds % 60}s";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
