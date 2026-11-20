using System.Collections.ObjectModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private void OpenRepository_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => OpenRepository_ClickCoreAsync(sender, args), "OpenRepository_Click");

    private async Task OpenRepository_ClickCoreAsync(object sender, RoutedEventArgs args) =>
        await OpenRepositoryPickerAsync();

    private void Repositories_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => Repositories_ClickCoreAsync(sender, args), "Repositories_Click");

    private async Task Repositories_ClickCoreAsync(object sender, RoutedEventArgs args) =>
        await TryCloseRepositoryAsync();

    private void CloseRepository_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => CloseRepository_ClickCoreAsync(sender, args), "CloseRepository_Click");

    private async Task CloseRepository_ClickCoreAsync(object sender, RoutedEventArgs args) =>
        await TryCloseRepositoryAsync();

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

    private static string RepositoryBranchDisplay(string? branchName) =>
        string.IsNullOrWhiteSpace(branchName) ? "—" : branchName;

    private static string RepositoryDisplayName(string path)
    {
        var displayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrWhiteSpace(displayName) ? path : displayName;
    }
}
