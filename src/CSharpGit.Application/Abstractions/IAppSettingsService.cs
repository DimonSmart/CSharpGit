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

public enum GitConsoleAutoOpenMode
{
    OnErrors,
    Always,
    Never
}

public sealed record RecentRepositorySettings(
    string Path,
    string DisplayName,
    DateTimeOffset LastOpenedUtc,
    string? LastBranchName);

public interface IAppSettingsService
{
    ApplicationThemeMode ThemeMode { get; }
    CommitTimeDisplayMode CommitTimeDisplayMode { get; }
    bool LoggingEnabled { get; }
    ApplicationLogLevel LogLevel { get; }
    GitConsoleAutoOpenMode GitConsoleAutoOpenMode => GitConsoleAutoOpenMode.OnErrors;
    IReadOnlyList<RecentRepositorySettings> RecentRepositories { get; }
    event EventHandler? Changed;

    Task SetThemeModeAsync(
        ApplicationThemeMode mode,
        CancellationToken cancellationToken = default);

    Task SetCommitTimeDisplayModeAsync(
        CommitTimeDisplayMode mode,
        CancellationToken cancellationToken = default);

    Task SetLoggingSettingsAsync(
        bool enabled,
        ApplicationLogLevel level,
        CancellationToken cancellationToken = default);

    Task SetGitConsoleAutoOpenModeAsync(
        GitConsoleAutoOpenMode mode,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task RecordRecentRepositoryAsync(
        string path,
        string displayName,
        string? lastBranchName,
        CancellationToken cancellationToken = default);

    Task RemoveRecentRepositoryAsync(
        string path,
        CancellationToken cancellationToken = default);
}
