using System.Collections.ObjectModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
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
