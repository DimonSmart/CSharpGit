using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private IAppSettingsService? _recentRepositorySettings;
    private RecentRepositoryFolderPicker? _recentRepositoryFolderPicker;
    private RecentRepositoriesView? _recentRepositoriesView;
    private RecentRepositoriesViewModel? _recentRepositoriesViewModel;
    private FrameworkElement? _emptyStartScreen;
    private bool _recentRepositoryWasBusy;
    private string? _lastRecordedRecentRepositoryPath;

    internal void InitializeRecentRepositories(
        IAppSettingsService settings,
        RecentRepositoryFolderPicker folderPicker)
    {
        if (_recentRepositoriesView is not null) return;

        _recentRepositorySettings = settings;
        _recentRepositoryFolderPicker = folderPicker;
        _emptyStartScreen = RootLayout.Children.Count > 0
            ? RootLayout.Children[0] as FrameworkElement
            : null;
        if (_emptyStartScreen is null)
            throw new InvalidOperationException("The empty repository start screen was not found.");

        _recentRepositoriesViewModel = new RecentRepositoriesViewModel(
            settings,
            OpenRecentRepositoryAsync,
            OpenRepositoryPickerAsync);
        _recentRepositoriesView = new RecentRepositoriesView
        {
            DataContext = _recentRepositoriesViewModel
        };

        // Keep the existing error InfoBar above the launcher so repository-open
        // failures stay visible without changing the current empty start screen.
        RootLayout.Children.Insert(1, _recentRepositoriesView);

        _recentRepositoryWasBusy = _viewModel.IsBusy;
        _viewModel.PropertyChanged += RecentRepositoryHost_PropertyChanged;
        settings.Changed += RecentRepositorySettings_Changed;
        Unloaded += RecentRepositories_Unloaded;
        UpdateStartScreenVisibility();
    }

    private async void RecentRepositoryHost_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.Repository))
            UpdateStartScreenVisibility();

        if (eventArgs.PropertyName != nameof(OpenRepositoryViewModel.IsBusy)) return;

        var becameIdle = _recentRepositoryWasBusy && !_viewModel.IsBusy;
        _recentRepositoryWasBusy = _viewModel.IsBusy;
        if (becameIdle) await RecordOpenedRepositoryAsync();
    }

    private void RecentRepositorySettings_Changed(object? sender, EventArgs e) => UpdateStartScreenVisibility();

    private void UpdateStartScreenVisibility()
    {
        if (_recentRepositoriesView is null || _emptyStartScreen is null || _recentRepositorySettings is null) return;

        var repositoryOpen = _viewModel.Repository is not null;
        var hasRecentRepositories = _recentRepositorySettings.RecentRepositories.Count > 0;
        _recentRepositoriesView.Visibility = !repositoryOpen && hasRecentRepositories
            ? Visibility.Visible
            : Visibility.Collapsed;
        _emptyStartScreen.Visibility = !repositoryOpen && !hasRecentRepositories
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task OpenRecentRepositoryAsync(RecentRepositoryItem item)
    {
        if (!Directory.Exists(item.Path))
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Repository folder not found",
                Content = $"The folder for this recent repository no longer exists:\n\n{item.Path}\n\nYou can remove it from Recent repositories with the remove button.",
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
            return;
        }

        if (_recentRepositoryFolderPicker is null) return;
        var command = (AsyncCommand)_viewModel.OpenRepositoryCommand;
        if (!command.CanExecute(null)) return;

        _recentRepositoryFolderPicker.QueuePath(item.Path);
        await command.ExecuteAsync();
    }

    private async Task OpenRepositoryPickerAsync()
    {
        var command = (AsyncCommand)_viewModel.OpenRepositoryCommand;
        if (command.CanExecute(null)) await command.ExecuteAsync();
    }

    private async Task RecordOpenedRepositoryAsync()
    {
        if (_recentRepositorySettings is null || _viewModel.Repository is not { } repository) return;
        if (_lastRecordedRecentRepositoryPath is not null
            && PathsEqual(_lastRecordedRecentRepositoryPath, repository.WorkingDirectory))
            return;

        _lastRecordedRecentRepositoryPath = repository.WorkingDirectory;
        var displayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(repository.WorkingDirectory));
        if (string.IsNullOrWhiteSpace(displayName)) displayName = repository.WorkingDirectory;
        var branchName = _viewModel.LocalBranches.FirstOrDefault(branch => branch.IsCurrent)?.Name;

        try
        {
            await _recentRepositorySettings.RecordRecentRepositoryAsync(
                repository.WorkingDirectory,
                displayName,
                branchName);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Could not persist recent repository: {exception}");
        }
    }

    private void RecentRepositories_Unloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.PropertyChanged -= RecentRepositoryHost_PropertyChanged;
        if (_recentRepositorySettings is not null)
            _recentRepositorySettings.Changed -= RecentRepositorySettings_Changed;
        _recentRepositoriesViewModel?.Dispose();
        Unloaded -= RecentRepositories_Unloaded;
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }
}
