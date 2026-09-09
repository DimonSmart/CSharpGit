namespace CSharpGit.Application.Abstractions;

public enum CommitTimeDisplayMode
{
    Smart,
    Relative,
    Absolute
}

public enum ApplicationLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public interface IAppSettingsService
{
    CommitTimeDisplayMode CommitTimeDisplayMode { get; }
    bool LoggingEnabled { get; }
    ApplicationLogLevel LogLevel { get; }
    event EventHandler? Changed;

    Task SetCommitTimeDisplayModeAsync(
        CommitTimeDisplayMode mode,
        CancellationToken cancellationToken = default);

    Task SetLoggingSettingsAsync(
        bool enabled,
        ApplicationLogLevel level,
        CancellationToken cancellationToken = default);
}
