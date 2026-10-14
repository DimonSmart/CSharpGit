namespace CSharpGit.Application.Abstractions;

/// <summary>Appearance, commit timestamp and history display preferences.</summary>
public interface IAppearanceSettings : IAppSettingsChangeSource
{
    ApplicationThemeMode ThemeMode { get; }
    CommitTimeDisplayMode CommitTimeDisplayMode { get; }
    HistoryRenderingMode HistoryRenderingMode { get; }
    bool ShowReflog { get; }
    bool ShowAuthorAvatars { get; }
    bool OnlineAvatarLookupEnabled { get; }

    Task SetThemeModeAsync(ApplicationThemeMode mode, CancellationToken cancellationToken = default);
    Task SetCommitTimeDisplayModeAsync(CommitTimeDisplayMode mode, CancellationToken cancellationToken = default);
    Task SetHistoryRenderingModeAsync(HistoryRenderingMode mode, CancellationToken cancellationToken = default);
    Task SetShowReflogAsync(bool value, CancellationToken cancellationToken = default);
    Task SetShowAuthorAvatarsAsync(bool value, CancellationToken cancellationToken = default);
    Task SetOnlineAvatarLookupEnabledAsync(bool value, CancellationToken cancellationToken = default);
}
