using System.Collections.ObjectModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

internal sealed record RepositorySelectorEntry(
    string Path,
    string Branch,
    bool IsCurrent)
{
    public string CurrentGlyph => IsCurrent ? "\uE73E" : string.Empty;

    public string ToolTip => $"{Path}\nBranch: {Branch}";

    public string AccessibleName =>
        IsCurrent
            ? $"Current repository {Path}, branch {Branch}"
            : $"{Path}, branch {Branch}";
}

public sealed partial class MainPage
{
    private const int QuickRepositoryLimit = 8;
    private readonly ObservableCollection<RepositorySelectorEntry> _repositorySelectorItems = [];

    private void InitializeRepositorySwitching()
    {
        RepositorySelectorList.ItemsSource = _repositorySelectorItems;
        RepositorySelectorOpenFolderButton.Content = _desktopShellService.OpenFolderDescription;
        UpdateRepositorySelectorPresentation();
    }

    private void UpdateRepositorySelectorPresentation()
    {
        var repository = _viewModel.Repository;
        if (repository is null)
        {
            RepositorySelectorName.Text = string.Empty;
            ToolTipService.SetToolTip(RepositorySelectorButton, null);
            return;
        }

        var path = repository.WorkingDirectory;
        RepositorySelectorName.Text = RepositoryDisplayName(path);
        ToolTipService.SetToolTip(RepositorySelectorButton, path);
    }

    private void RepositorySelectorFlyout_Opening(object sender, object args)
    {
        _repositorySelectorItems.Clear();

        var currentPath = _viewModel.Repository?.WorkingDirectory;
        if (currentPath is not null)
        {
            var currentBranch = _viewModel.IsDetachedHead
                ? "detached HEAD"
                : RepositoryBranchDisplay(_viewModel.CurrentBranchName);
            _repositorySelectorItems.Add(new RepositorySelectorEntry(
                currentPath,
                currentBranch,
                true));
        }

        var quickRepositories = RecentRepositoriesProjectionBuilder.BuildQuickList(
            _recentRepositorySettings.RecentRepositories,
            currentPath,
            QuickRepositoryLimit);

        foreach (var repository in quickRepositories)
        {
            _repositorySelectorItems.Add(new RepositorySelectorEntry(
                repository.Path,
                RepositoryBranchDisplay(repository.LastBranchName),
                false));
        }

        var canChangeRepository = _viewModel.CanChangeRepository;
        RepositorySelectorList.IsEnabled = canChangeRepository;
        RepositorySelectorOpenFolderButton.IsEnabled =
            currentPath is not null && Directory.Exists(currentPath);
        RepositorySelectorCopyPathButton.IsEnabled = currentPath is not null;
        RepositorySelectorOpenButton.IsEnabled = canChangeRepository;
        RepositorySelectorCloneButton.IsEnabled = canChangeRepository;
        RepositorySelectorCreateButton.IsEnabled = canChangeRepository;
        RepositorySelectorRepositoriesButton.IsEnabled = canChangeRepository;
        RepositorySelectorCloseButton.IsEnabled = canChangeRepository;
    }

    private async void RepositorySelectorList_ItemClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not RepositorySelectorEntry entry) return;

        RepositorySelectorFlyout.Hide();
        if (entry.IsCurrent) return;

        await TrySwitchRepositoryAsync(entry.Path);
    }

    private async void RepositorySelectorOpenFolder_Click(object sender, RoutedEventArgs args)
    {
        var path = _viewModel.Repository?.WorkingDirectory;
        RepositorySelectorFlyout.Hide();
        if (path is not null && Directory.Exists(path))
            await OpenFolderInDesktopShellAsync(path, "Could not open repository folder");
    }

    private async void RepositorySelectorCopyPath_Click(object sender, RoutedEventArgs args)
    {
        var path = _viewModel.Repository?.WorkingDirectory;
        RepositorySelectorFlyout.Hide();
        if (path is not null)
            await CopyTextAsync(path);
    }

    private async void RepositorySelectorOpenRepository_Click(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await OpenRepositoryPickerAsync();
    }

    private async void RepositorySelectorCloneRepository_Click(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await ShowCloneRepositoryAsync();
    }

    private async void RepositorySelectorCreateRepository_Click(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await ShowCreateRepositoryAsync();
    }

    private async void RepositorySelectorRepositories_Click(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await TryCloseRepositoryAsync();
    }

    private async void RepositorySelectorCloseRepository_Click(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await TryCloseRepositoryAsync();
    }

    private async void OpenRepository_Click(object sender, RoutedEventArgs args) =>
        await OpenRepositoryPickerAsync();

    private async void Repositories_Click(object sender, RoutedEventArgs args) =>
        await TryCloseRepositoryAsync();

    private async void CloseRepository_Click(object sender, RoutedEventArgs args) =>
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

        var discardDraft = _viewModel.Repository is not null
            && _viewModel.HasUnappliedCommitMessage;
        if (discardDraft
            && !await ConfirmDiscardCommitMessageAsync(closing: false))
            return false;

        return await SwitchRepositoryCoreAsync(path, discardDraft);
    }

    private async Task<bool> SwitchRepositoryCoreAsync(
        string path,
        bool discardDraft)
    {
        if (!_viewModel.CanChangeRepository) return false;

        bool opened;
        try
        {
            opened = await _viewModel.OpenRepositoryPathAsync(path);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (!opened) return false;

        if (discardDraft)
            _viewModel.CommitMessage = string.Empty;

        RefreshPresentationCollections();
        UpdateRepositorySelectorPresentation();
        return true;
    }

    private async Task<bool> TryCloseRepositoryAsync()
    {
        if (_repositoryCloningWorkflowActive || _repositoryCreationWorkflowActive)
            return false;
        if (_viewModel.Repository is null) return true;
        if (!_viewModel.CanChangeRepository) return false;

        var discardDraft = _viewModel.HasUnappliedCommitMessage;
        if (discardDraft
            && !await ConfirmDiscardCommitMessageAsync(closing: true))
            return false;

        if (!_viewModel.CloseRepository())
            return false;

        if (discardDraft)
            _viewModel.CommitMessage = string.Empty;

        RefreshPresentationCollections();
        UpdateRepositorySelectorPresentation();
        return true;
    }

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

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
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
        await dialog.ShowAsync();
    }

    private static string RepositoryBranchDisplay(string? branchName) =>
        string.IsNullOrWhiteSpace(branchName) ? "—" : branchName;

    private static string RepositoryDisplayName(string path)
    {
        var displayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrWhiteSpace(displayName) ? path : displayName;
    }
}
