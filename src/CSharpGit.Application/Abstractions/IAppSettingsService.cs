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

public enum HistoryRenderingMode
{
    SubjectOnly,
    TextColumns,
    TextAndGraph,
    TextGraphAndReferences,
    Full
}

public sealed record RecentRepositorySettings(
    string Path,
    string DisplayName,
    DateTimeOffset LastOpenedUtc,
    string? LastBranchName,
    bool IsPinned = false,
    int? PinnedOrder = null);

public interface IAppSettingsService
{
    ApplicationThemeMode ThemeMode { get; }
    CommitTimeDisplayMode CommitTimeDisplayMode { get; }
    bool LoggingEnabled { get; }
    ApplicationLogLevel LogLevel { get; }
    GitConsoleAutoOpenMode GitConsoleAutoOpenMode { get; }
    bool ShowReflog { get; }
    bool AutoSetupRemoteOnPush { get; }
    PullStrategy DefaultPullStrategy { get; }
    bool ForcePullAutoStash { get; }
    bool ShowAuthorAvatars { get; }
    bool OnlineAvatarLookupEnabled { get; }
    bool HistoryPerformanceDiagnosticsEnabled { get; }
    HistoryRenderingMode HistoryRenderingMode { get; }
    string DefaultRepositoriesDirectory { get; }
    IReadOnlyList<RecentRepositorySettings> RecentRepositories { get; }

    /// <summary>
    /// Raised after a new settings state has been persisted successfully and published as committed state.
    /// The event is thread-agnostic and may be raised on any thread. It is not raised when persistence
    /// fails and need not be raised for a no-op update. Subscribers must re-read current values from
    /// this service instead of relying on event payload state.
    /// </summary>
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
        CancellationToken cancellationToken = default);

    Task SetShowReflogAsync(
        bool value,
        CancellationToken cancellationToken = default);

    Task SetAutoSetupRemoteOnPushAsync(
        bool value,
        CancellationToken cancellationToken = default);

    Task SetDefaultPullStrategyAsync(PullStrategy strategy, CancellationToken cancellationToken = default);
    Task SetForcePullAutoStashAsync(bool value, CancellationToken cancellationToken = default);

    Task SetShowAuthorAvatarsAsync(
        bool value,
        CancellationToken cancellationToken = default);

    Task SetOnlineAvatarLookupEnabledAsync(
        bool value,
        CancellationToken cancellationToken = default);

    Task SetHistoryPerformanceDiagnosticsEnabledAsync(
        bool value,
        CancellationToken cancellationToken = default);

    Task SetHistoryRenderingModeAsync(
        HistoryRenderingMode mode,
        CancellationToken cancellationToken = default);

    Task SetDefaultRepositoriesDirectoryAsync(
        string directory,
        CancellationToken cancellationToken = default);

    Task RecordRecentRepositoryAsync(
        string path,
        string displayName,
        string? lastBranchName,
        CancellationToken cancellationToken = default);

    Task UpdateRecentRepositoryBranchAsync(
        string path,
        string? lastBranchName,
        CancellationToken cancellationToken = default);

    Task SetRecentRepositoryPinnedAsync(
        string path,
        bool pinned,
        CancellationToken cancellationToken = default);

    Task MovePinnedRepositoryAsync(
        string path,
        int newIndex,
        CancellationToken cancellationToken = default);

    Task RemoveRecentRepositoryAsync(
        string path,
        CancellationToken cancellationToken = default);
}
