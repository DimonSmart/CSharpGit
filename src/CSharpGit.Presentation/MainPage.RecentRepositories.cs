using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private RecentRepositoryFolderPicker _recentRepositoryFolderPicker = null!;

    private async Task OpenRepositoryPickerAsync()
    {
        if (_repositoryCloningWorkflowActive
            || _repositoryCreationWorkflowActive
            || !_viewModel.CanChangeRepository)
            return;

        string? path;
        try
        {
            path = await _recentRepositoryFolderPicker.PickFolderAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Could not open folder picker",
                Content = $"The system folder picker could not be opened:\n\n{exception.Message}",
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
            return;
        }

        if (path is not null)
            await TrySwitchRepositoryAsync(path);
    }
}
