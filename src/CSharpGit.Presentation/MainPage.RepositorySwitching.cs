using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private const int QuickRepositoryLimit = 8;

    private void InitializeRepositorySwitching() =>
        UpdateRepositorySelectorPresentation();

    private void UpdateRepositorySelectorPresentation()
    {
        var repository = _viewModel.Repository;
        if (repository is null)
        {
            RepositorySelectorName.Text = string.Empty;
            RepositorySelectorPath.Text = string.Empty;
            ToolTipService.SetToolTip(RepositorySelectorButton, null);
            return;
        }

        var path = repository.WorkingDirectory;
        RepositorySelectorName.Text = RepositoryDisplayName(path);
        RepositorySelectorPath.Text = path;
        ToolTipService.SetToolTip(RepositorySelectorButton, path);
    }

    private void RepositorySelectorFlyout_Opening(object sender, object args)
    {
        RepositorySelectorFlyout.Items.Clear();

        var currentPath = _viewModel.Repository?.WorkingDirectory;
        RepositorySelectorFlyout.Items.Add(new MenuFlyoutItem
        {
            Text = "Current repository",
            IsEnabled = false
        });

        if (currentPath is not null)
        {
            var current = new MenuFlyoutItem
            {
                Text = RepositoryDisplayName(currentPath),
                IsEnabled = false,
                Icon = new FontIcon { Glyph = "\uE73E" }
            };
            ToolTipService.SetToolTip(current, currentPath);
            RepositorySelectorFlyout.Items.Add(current);
        }

        var quickRepositories = RecentRepositoriesProjectionBuilder.BuildQuickList(
            _recentRepositorySettings.RecentRepositories,
            currentPath,
            QuickRepositoryLimit);

        foreach (var repository in quickRepositories)
        {
            var item = new MenuFlyoutItem
            {
                Text = repository.DisplayName,
                IsEnabled = _viewModel.CanChangeRepository
            };
            ToolTipService.SetToolTip(item, repository.Path);
            item.Click += async (_, _) => await TrySwitchRepositoryAsync(repository.Path);
            RepositorySelectorFlyout.Items.Add(item);
        }

        RepositorySelectorFlyout.Items.Add(new MenuFlyoutSeparator());
        RepositorySelectorFlyout.Items.Add(CreateRepositoryActionItem(
            "Open repository…",
            async () => await OpenRepositoryPickerAsync()));
        RepositorySelectorFlyout.Items.Add(CreateRepositoryActionItem(
            "Create repository…",
            ShowCreateRepositoryAsync));
        RepositorySelectorFlyout.Items.Add(CreateRepositoryActionItem(
            "Repositories…",
            async () => await TryCloseRepositoryAsync()));
        RepositorySelectorFlyout.Items.Add(new MenuFlyoutSeparator());
        RepositorySelectorFlyout.Items.Add(CreateRepositoryActionItem(
            "Close repository",
            async () => await TryCloseRepositoryAsync()));
    }

    private MenuFlyoutItem CreateRepositoryActionItem(
        string text,
        Func<Task> action)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            IsEnabled = _viewModel.CanChangeRepository
        };
        item.Click += async (_, _) => await action();
        return item;
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
        if (!_viewModel.CanChangeRepository) return false;

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

    private static string RepositoryDisplayName(string path)
    {
        var displayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrWhiteSpace(displayName) ? path : displayName;
    }
}
