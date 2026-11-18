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

    private async Task<bool> TrySwitchRepositoryAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_repositoryCloningWorkflowActive
            || _repositoryCreationWorkflowActive
            || !_viewModel.CanChangeRepository)
            return false;

        var currentPath = _viewModel.Repository?.WorkingDirectory;
        if (currentPath is not null
            && RecentRepositoriesProjectionBuilder.PathsEqual(currentPath, path))
            return true;

        if (!Directory.Exists(path))
        {
            await ShowUnavailableRepositoryAsync(path);
            return false;
        }

        var originalRepository = _viewModel.Repository;
        var discardDraft = originalRepository is not null
            && _viewModel.CommitCreation.HasUnappliedCommitMessage;
        if (discardDraft
            && !await ConfirmDiscardCommitMessageAsync(closing: false))
            return false;

        if (IsShuttingDown || !ReferenceEquals(originalRepository, _viewModel.Repository)
            || !_viewModel.CanChangeRepository)
            return false;
        return await SwitchRepositoryCoreAsync(path, discardDraft);
    }

    private async Task<bool> SwitchRepositoryCoreAsync(
        string path,
        bool discardDraft,
        OpenRepositoryViewModel.UserGitOperationLease? continuation = null)
    {
        if (IsShuttingDown
            || (continuation is null ? !_viewModel.CanChangeRepository
                : !continuation.IsOwnedBy(_viewModel))) return false;

        bool opened;
        try
        {
            opened = continuation is null
                ? await _viewModel.OpenRepositoryPathAsync(path)
                : await _viewModel.OpenRepositoryPathFromLifecycleAsync(path, continuation);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (!opened || IsShuttingDown) return false;

        if (discardDraft)
            _viewModel.CommitCreation.CommitMessage = string.Empty;

        RefreshPresentationCollections();
        RepositoryHeader.Refresh();
        return true;
    }

    private async Task<bool> TryCloseRepositoryAsync()
    {
        if (_repositoryCloningWorkflowActive || _repositoryCreationWorkflowActive)
            return false;
        if (_viewModel.Repository is null) return true;
        if (!_viewModel.CanChangeRepository) return false;

        var originalRepository = _viewModel.Repository;
        var discardDraft = _viewModel.CommitCreation.HasUnappliedCommitMessage;
        if (discardDraft
            && !await ConfirmDiscardCommitMessageAsync(closing: true))
            return false;

        return CloseRepositoryCore(originalRepository, discardDraft);
    }

    // The UI confirmation dialog is owned by TryCloseRepositoryAsync. The
    // core applies the already-approved choice and can be tested separately.
    private bool CloseRepositoryCore(CSharpGit.Domain.Repository expectedRepository, bool discardDraft)
    {
        if (IsShuttingDown || !ReferenceEquals(expectedRepository, _viewModel.Repository)
            || !_viewModel.CanChangeRepository || !_viewModel.CloseRepository())
            return false;

        if (discardDraft)
            _viewModel.CommitCreation.CommitMessage = string.Empty;

        RefreshPresentationCollections();
        RepositoryHeader.Refresh();
        return true;
    }

    internal Task<bool> ConfirmedSwitchForDesktopCheckAsync(string path) =>
        SwitchRepositoryCoreAsync(path, discardDraft: true);

    internal bool ConfirmedCloseForDesktopCheck() =>
        _viewModel.Repository is { } repository && CloseRepositoryCore(repository, discardDraft: true);

    private async Task<bool> ConfirmDiscardCommitMessageAsync(bool closing)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Discard commit message?",
            Content = closing
                ? "Closing this repository will discard the current commit message."
                : "Opening another repository will discard the current commit message.",
            PrimaryButtonText = closing
                ? "Discard and close"
                : "Discard and continue",
            CloseButtonText = "Keep editing",
            DefaultButton = ContentDialogButton.Close
        };

        return await ShowPageDialogAsync(dialog) == ContentDialogResult.Primary;
    }

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

    private async Task ShowUnavailableRepositoryAsync(string path)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Repository folder not found",
            Content = $"The folder for this recent repository no longer exists:\n\n{path}\n\nYou can remove it from Recent repositories with the remove button.",
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close
        };
        await ShowPageDialogAsync(dialog);
    }

    private static string RepositoryBranchDisplay(string? branchName) =>
        string.IsNullOrWhiteSpace(branchName) ? "—" : branchName;

    private static string RepositoryDisplayName(string path)
    {
        var displayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrWhiteSpace(displayName) ? path : displayName;
    }
}
