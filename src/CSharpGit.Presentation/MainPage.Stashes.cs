using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private async void CreateStash_Click(object sender, RoutedEventArgs e)
    {
        await ShowCreateStashDialogAsync();
    }

    private async Task ShowCreateStashDialogAsync()
    {
        if (!_viewModel.CanCreateStash) return;

        var message = new TextBox
        {
            Header = "Message",
            PlaceholderText = "Optional stash message"
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Create stash",
            Content = message,
            PrimaryButtonText = "Create stash",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await _viewModel.CreateStashAsync(message.Text);
    }
}
