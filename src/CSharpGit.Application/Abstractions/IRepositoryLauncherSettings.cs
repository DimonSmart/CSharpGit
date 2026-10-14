namespace CSharpGit.Application.Abstractions;

/// <summary>Default repository location, recent repositories, pinned items and groups.</summary>
public interface IRepositoryLauncherSettings : IAppSettingsChangeSource
{
    string DefaultRepositoriesDirectory { get; }
    IReadOnlyList<RecentRepositorySettings> RecentRepositories { get; }
    IReadOnlyList<RepositoryGroupSettings> RepositoryGroups { get; }
    bool RecentRepositoriesExpanded { get; }
    RepositoryLauncherSettingsSnapshot RepositoryLauncherSnapshot { get; }

    Task SetDefaultRepositoriesDirectoryAsync(string directory, CancellationToken cancellationToken = default);
    Task SetRecentRepositoriesExpandedAsync(bool expanded, CancellationToken cancellationToken = default);
    Task RecordRecentRepositoryAsync(string path, string displayName, string? lastBranchName, CancellationToken cancellationToken = default);
    Task UpdateRecentRepositoryBranchAsync(string path, string? lastBranchName, CancellationToken cancellationToken = default);
    Task RemoveRecentRepositoryAsync(string path, CancellationToken cancellationToken = default);
    Task<RepositoryGroupSettings> CreateRepositoryGroupAsync(string name, CancellationToken cancellationToken = default);
    Task RenameRepositoryGroupAsync(string groupId, string name, CancellationToken cancellationToken = default);
    Task DeleteRepositoryGroupAsync(string groupId, CancellationToken cancellationToken = default);
    Task MoveRepositoryGroupAsync(string groupId, int newIndex, CancellationToken cancellationToken = default);
    Task SetRepositoryGroupExpandedAsync(string groupId, bool expanded, CancellationToken cancellationToken = default);
    Task MoveRecentRepositoryToGroupAsync(string path, string groupId, CancellationToken cancellationToken = default);
    Task SetRecentRepositoryPinnedAsync(string path, bool pinned, CancellationToken cancellationToken = default);
    Task MovePinnedRepositoryAsync(string path, int newIndex, CancellationToken cancellationToken = default);
}
