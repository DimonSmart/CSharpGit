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
        RepositorySelectorOpenFolderText.Text = _desktopShellService.OpenFolderDescription;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            RepositorySelectorOpenFolderButton, _desktopShellService.OpenFolderDescription);
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

        var snapshot = _recentRepositorySettings.RepositoryLauncherSnapshot;
        var quickRepositories = RecentRepositoriesProjectionBuilder.BuildQuickList(
            snapshot.Groups,
            snapshot.Repositories,
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

    private void RepositorySelectorList_ItemClick(object sender, ItemClickEventArgs args) =>
        RunPageUiOperation(() => RepositorySelectorList_ItemClickCoreAsync(sender, args), "RepositorySelectorList_ItemClick");

    private async Task RepositorySelectorList_ItemClickCoreAsync(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not RepositorySelectorEntry entry) return;

        RepositorySelectorFlyout.Hide();
        if (entry.IsCurrent) return;

        await TrySwitchRepositoryAsync(entry.Path);
    }

    private void RepositorySelectorOpenFolder_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => RepositorySelectorOpenFolder_ClickCoreAsync(sender, args), "RepositorySelectorOpenFolder_Click");

    private async Task RepositorySelectorOpenFolder_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        var path = _viewModel.Repository?.WorkingDirectory;
        RepositorySelectorFlyout.Hide();
        if (path is not null && Directory.Exists(path))
            await OpenFolderInDesktopShellAsync(path, "Could not open repository folder");
    }

    private void RepositorySelectorCopyPath_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => RepositorySelectorCopyPath_ClickCoreAsync(sender, args), "RepositorySelectorCopyPath_Click");

    private async Task RepositorySelectorCopyPath_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        var path = _viewModel.Repository?.WorkingDirectory;
        RepositorySelectorFlyout.Hide();
        if (path is not null)
            await CopyTextAsync(path);
    }

    private void RepositorySelectorOpenRepository_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => RepositorySelectorOpenRepository_ClickCoreAsync(sender, args), "RepositorySelectorOpenRepository_Click");

    private async Task RepositorySelectorOpenRepository_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await OpenRepositoryPickerAsync();
    }

    private void RepositorySelectorCloneRepository_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => RepositorySelectorCloneRepository_ClickCoreAsync(sender, args), "RepositorySelectorCloneRepository_Click");

    private async Task RepositorySelectorCloneRepository_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await ShowCloneRepositoryAsync();
    }

    private void RepositorySelectorCreateRepository_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => RepositorySelectorCreateRepository_ClickCoreAsync(sender, args), "RepositorySelectorCreateRepository_Click");

    private async Task RepositorySelectorCreateRepository_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await ShowCreateRepositoryAsync();
    }

    private void RepositorySelectorRepositories_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => RepositorySelectorRepositories_ClickCoreAsync(sender, args), "RepositorySelectorRepositories_Click");

    private async Task RepositorySelectorRepositories_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await TryCloseRepositoryAsync();
    }

    private void RepositorySelectorCloseRepository_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(() => RepositorySelectorCloseRepository_ClickCoreAsync(sender, args), "RepositorySelectorCloseRepository_Click");

    private async Task RepositorySelectorCloseRepository_ClickCoreAsync(object sender, RoutedEventArgs args)
    {
        RepositorySelectorFlyout.Hide();
        await TryCloseRepositoryAsync();
    }

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
        bool discardDraft)
    {
        if (IsShuttingDown || !_viewModel.CanChangeRepository) return false;

        bool opened;
        try
        {
            opened = await _viewModel.OpenRepositoryPathAsync(path);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (!opened || IsShuttingDown) return false;

        if (discardDraft)
            _viewModel.CommitCreation.CommitMessage = string.Empty;

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
        UpdateRepositorySelectorPresentation();
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
