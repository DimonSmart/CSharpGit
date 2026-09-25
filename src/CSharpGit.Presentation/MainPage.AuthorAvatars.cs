using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private IAuthorAvatarService _authorAvatarService = null!;
    private IAppSettingsService _authorAvatarSettings = null!;
}
