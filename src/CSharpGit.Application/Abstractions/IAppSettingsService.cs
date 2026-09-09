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

public sealed record RecentRepositorySettings(
    string Path,
    string DisplayName,
    DateTimeOffset LastOpenedUtc,
    string? LastBranchName);

public interface IAppSettingsService
{
    CommitTimeDisplayMode CommitTimeDisplayMode { get; }
    bool LoggingEnabled { get; }
    ApplicationLogLevel LogLevel { get; }
    IReadOnlyList<RecentRepositorySettings> RecentRepositories { get; }
    event EventHandler? Changed;

    Task SetCommitTimeDisplayModeAsync(
        CommitTimeDisplayMode mode,
        CancellationToken cancellationToken = default);

    Task SetLoggingSettingsAsync(
        bool enabled,
        ApplicationLogLevel level,
        CancellationToken cancellationToken = default);

    Task RecordRecentRepositoryAsync(
        string path,
        string displayName,
        string? lastBranchName,
        CancellationToken cancellationToken = default);

    Task RemoveRecentRepositoryAsync(
        string path,
        CancellationToken cancellationToken = default);
}
