using System.Collections.ObjectModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private void OpenRepository_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(OpenRepositoryPickerAsync, "OpenRepository_Click");

    private void Repositories_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(async () => { await TryCloseRepositoryAsync(); }, "Repositories_Click");

    private void CloseRepository_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(async () => { await TryCloseRepositoryAsync(); }, "CloseRepository_Click");

    private Task<bool> TrySwitchRepositoryAsync(string path) =>
        _repositorySwitch.TrySwitchAsync(path);

    private Task<bool> SwitchRepositoryCoreAsync(
        string path, bool discardDraft,
        OpenRepositoryViewModel.UserGitOperationLease? continuation = null) =>
        _repositorySwitch.SwitchCoreAsync(path, discardDraft, continuation);

    private Task<bool> TryCloseRepositoryAsync() => _repositorySwitch.TryCloseAsync();

    internal Task<bool> ConfirmedSwitchForDesktopCheckAsync(string path) =>
        _repositorySwitch.ConfirmedSwitchForDesktopCheckAsync(path);

    internal bool ConfirmedCloseForDesktopCheck() =>
        _repositorySwitch.ConfirmedCloseForDesktopCheck();

    private Task<bool> ConfirmDiscardCommitMessageAsync(bool closing) =>
        _repositorySwitch.ConfirmDiscardAsync(closing);

    private Task ShowUnavailableRepositoryAsync(string path) =>
        _repositorySwitch.ShowUnavailableAsync(path);

    private async Task OpenFolderInDesktopShellAsync(string path, string errorTitle)
    {
        try
        {
            await _desktopShellService.OpenFolderAsync(path);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(errorTitle, exception.Message);
        }
    }

}
