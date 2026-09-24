using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private IAuthorAvatarService _authorAvatarService = null!;
    private IAppSettingsService _authorAvatarSettings = null!;

    private void ConfigureAuthorAvatars(DependencyObject root)
    {
        if (root is AuthorAvatar avatar)
            avatar.Configure(_authorAvatarService, _authorAvatarSettings);

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
            ConfigureAuthorAvatars(VisualTreeHelper.GetChild(root, index));
    }
}
