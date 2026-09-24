using CSharpGit.Application.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace CSharpGit.Presentation.Controls;

public sealed partial class CommitDetailsView : UserControl
{
    public CommitDetailsView() => InitializeComponent();

    internal void ConfigureAuthorAvatar(
        IAuthorAvatarService avatarService,
        IAppSettingsService settings) =>
        CommitAuthorAvatar.Configure(avatarService, settings);

    private async void CopyText_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string text } button || string.IsNullOrEmpty(text)) return;

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);

        if (button.Content is not FontIcon icon) return;
        var originalGlyph = icon.Glyph;
        icon.Glyph = "\uE73E";
        try
        {
            await Task.Delay(900);
        }
        finally
        {
            if (ReferenceEquals(button.Content, icon)) icon.Glyph = originalGlyph;
        }
    }
}
