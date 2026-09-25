using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.Controls;

internal static class AuthorAvatarServiceContext
{
    private static IAuthorAvatarService? _avatarService;
    private static IAppSettingsService? _settings;

    internal static void Configure(
        IAuthorAvatarService avatarService,
        IAppSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(avatarService);
        ArgumentNullException.ThrowIfNull(settings);

        Volatile.Write(ref _avatarService, avatarService);
        Volatile.Write(ref _settings, settings);
    }

    internal static bool TryGet(
        out IAuthorAvatarService avatarService,
        out IAppSettingsService settings)
    {
        var service = Volatile.Read(ref _avatarService);
        var appSettings = Volatile.Read(ref _settings);
        if (service is null || appSettings is null)
        {
            avatarService = null!;
            settings = null!;
            return false;
        }

        avatarService = service;
        settings = appSettings;
        return true;
    }
}
