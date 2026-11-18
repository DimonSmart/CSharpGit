using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation;

// Temporary phase-2 adapter: HistoryView will own the provider in phase 2.5.
public sealed partial class MainPage : Controls.IAuthorAvatarDependencyProvider
{
    IAuthorAvatarService Controls.IAuthorAvatarDependencyProvider.AvatarService => _authorAvatarService;
    IAppSettingsService Controls.IAuthorAvatarDependencyProvider.AppSettings => _authorAvatarSettings;

    private IAuthorAvatarService _authorAvatarService = null!;
    private IAppSettingsService _authorAvatarSettings = null!;
}
