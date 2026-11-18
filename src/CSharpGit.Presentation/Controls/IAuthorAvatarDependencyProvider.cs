using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.Controls;

// A narrow visual-owner contract, resolved through the current visual tree.
// Kept independent of MainPage so that HistoryView can own its avatars directly.
internal interface IAuthorAvatarDependencyProvider
{
    IAuthorAvatarService AvatarService { get; }
    IAppSettingsService AppSettings { get; }
}
