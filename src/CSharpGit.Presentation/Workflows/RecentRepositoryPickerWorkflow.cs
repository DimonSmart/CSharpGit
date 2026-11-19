using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

/// <summary>
/// Owns the folder-picker interaction and its error presentation. The opening
/// workflow is supplied as a narrow action, not as access to a page or service locator.
/// </summary>
internal sealed class RecentRepositoryPickerWorkflow(
    RecentRepositoryFolderPicker folderPicker,
    Func<bool> canChangeRepository,
    Func<string, Task<bool>> switchRepository,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog)
{
    internal async Task OpenAsync()
    {
        if (!canChangeRepository()) return;

        string? path;
        try
        {
            path = await folderPicker.PickFolderAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            await showDialog(new ContentDialog
            {
                Title = "Could not open folder picker",
                Content = $"The system folder picker could not be opened:\n\n{exception.Message}",
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close
            });
            return;
        }

        // The user may start cloning or change the active repository while
        // the native picker is open. Never apply a stale picker selection.
        if (!string.IsNullOrEmpty(path) && canChangeRepository())
            await switchRepository(path);
    }
}
