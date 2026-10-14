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
    int? PinnedOrder = null,
    string? GroupId = null);

public sealed record RepositoryGroupSettings(string Id, string Name, int Order, bool IsExpanded);

public sealed record RepositoryLauncherSettingsSnapshot(
    IReadOnlyList<RepositoryGroupSettings> Groups,
    IReadOnlyList<RecentRepositorySettings> Repositories);

/// <summary>Compatibility aggregate; consumers requiring a single area should depend on a facet.</summary>
public interface IAppSettingsService :
    IAppearanceSettings,
    IGitWorkflowSettings,
    IDiagnosticsSettings,
    IRepositoryLauncherSettings
{
}
