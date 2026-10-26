using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private IAppSettingsService _recentRepositorySettings = null!;
    private RecentRepositoryFolderPicker _recentRepositoryFolderPicker = null!;
    private IRepositoryImageService _repositoryImageService = null!;
    private RecentRepositoriesView? _recentRepositoriesView;
    private RecentRepositoriesViewModel? _recentRepositoriesViewModel;
    private bool _recentRepositoryWasBusy;
    private string? _lastRecordedRecentRepositoryPath;
    private bool _recentRepositoriesShutdown;

    private void InitializeRecentRepositories()
    {
        if (_recentRepositoriesView is not null) return;

        _recentRepositoriesShutdown = false;
        _recentRepositoriesViewModel = new RecentRepositoriesViewModel(
            _recentRepositorySettings,
            _recentRepositorySettings,
            _repositoryImageService,
            OpenRecentRepositoryAsync,
            OpenRecentRepositoryFolderAsync,
            item => CopyTextAsync(item.Path),
            _desktopShellService.OpenFolderDescription,
            OpenRepositoryPickerAsync,
            ShowCloneRepositoryAsync,
            ShowCreateRepositoryAsync,
            DispatcherQueue);
        _recentRepositoriesView = new RecentRepositoriesView
        {
            DataContext = _recentRepositoriesViewModel
        };
        RecentRepositoriesHost.Content = _recentRepositoriesView;

        _recentRepositoryWasBusy = _viewModel.IsBusy;
        _viewModel.PropertyChanged += RecentRepositoryHost_PropertyChanged;
        _recentRepositorySettings.Changed += RecentRepositorySettings_Changed;
        Unloaded += RecentRepositories_Unloaded;
        UpdateStartScreenVisibility();
    }

    private void RecentRepositoryHost_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (IsShuttingDown || _recentRepositoriesShutdown) return;

        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.Repository))
        {
            UpdateStartScreenVisibility();
            HandleHistoryPerformanceRepositoryChanged();
        }

        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.CurrentBranchName))
            RunPageUiOperation(UpdateRecentRepositoryBranchMetadataAsync, "Recent repository branch metadata");

        if (eventArgs.PropertyName != nameof(OpenRepositoryViewModel.IsBusy)) return;

        var becameIdle = _recentRepositoryWasBusy && !_viewModel.IsBusy;
        _recentRepositoryWasBusy = _viewModel.IsBusy;
        if (becameIdle)
            RunPageUiOperation(RecordOpenedRepositoryAsync, "Record recent repository");
    }

    private void RecentRepositorySettings_Changed(object? sender, EventArgs e)
    {
        if (IsShuttingDown || _recentRepositoriesShutdown) return;

        if (DispatcherQueue.HasThreadAccess)
        {
            HandleHistoryPerformanceSettingsChanged();
            UpdateStartScreenVisibility();
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsShuttingDown && !_recentRepositoriesShutdown)
            {
                HandleHistoryPerformanceSettingsChanged();
                UpdateStartScreenVisibility();
            }
        });
    }

    private void UpdateStartScreenVisibility()
    {
        if (IsShuttingDown || _recentRepositoriesShutdown || _recentRepositoriesView is null) return;

        var repositoryOpen = _viewModel.Repository is not null;
        var snapshot = _recentRepositorySettings.RepositoryLauncherSnapshot;
        var hasRecentRepositories = snapshot.Repositories.Count > 0 || snapshot.Groups.Count > 1;
        EmptyStartScreen.Visibility = !repositoryOpen && !hasRecentRepositories
            ? Visibility.Visible
            : Visibility.Collapsed;
        var showRecentRepositories = !repositoryOpen && hasRecentRepositories;
        var wasRecentRepositoriesVisible = RecentRepositoriesHost.Visibility == Visibility.Visible;
        RecentRepositoriesHost.Visibility = showRecentRepositories
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (showRecentRepositories && !wasRecentRepositoriesVisible)
            _recentRepositoriesView.FocusSearch();
    }

    private async Task OpenRecentRepositoryAsync(RecentRepositoryItem item)
    {
        if (!Directory.Exists(item.Path))
        {
            await ShowUnavailableRepositoryAsync(item.Path);
            return;
        }

        await TrySwitchRepositoryAsync(item.Path);
    }

    private async Task OpenRecentRepositoryFolderAsync(RecentRepositoryItem item)
    {
        if (!Directory.Exists(item.Path))
        {
            await ShowUnavailableRepositoryAsync(item.Path);
            return;
        }

        await OpenFolderInDesktopShellAsync(item.Path, "Could not open repository folder");
    }

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

    private async Task UpdateRecentRepositoryBranchMetadataAsync()
    {
        if (_recentRepositoriesShutdown || _viewModel.Repository is not { } repository) return;

        var branchName = _viewModel.IsDetachedHead ? null : _viewModel.CurrentBranchName;
        try
        {
            await _recentRepositorySettings.UpdateRecentRepositoryBranchAsync(
                repository.WorkingDirectory,
                branchName);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Could not persist recent repository branch metadata: {exception}");
        }
    }

    private async Task RecordOpenedRepositoryAsync()
    {
        if (_recentRepositoriesShutdown || _viewModel.Repository is not { } repository) return;
        if (_lastRecordedRecentRepositoryPath is not null
            && PathsEqual(_lastRecordedRecentRepositoryPath, repository.WorkingDirectory))
            return;

        _lastRecordedRecentRepositoryPath = repository.WorkingDirectory;
        var displayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(repository.WorkingDirectory));
        if (string.IsNullOrWhiteSpace(displayName)) displayName = repository.WorkingDirectory;
        var branchName = _viewModel.Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent)?.Name;

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

    private void RecentRepositories_Unloaded(object sender, RoutedEventArgs e) => ShutdownRecentRepositories();

    private void ShutdownRecentRepositories()
    {
        if (_recentRepositoriesShutdown) return;
        _recentRepositoriesShutdown = true;
        _viewModel.PropertyChanged -= RecentRepositoryHost_PropertyChanged;
        _recentRepositorySettings.Changed -= RecentRepositorySettings_Changed;
        _recentRepositoriesViewModel?.Dispose();
        Unloaded -= RecentRepositories_Unloaded;
    }

    private static bool PathsEqual(string left, string right) =>
        RecentRepositoriesProjectionBuilder.PathsEqual(left, right);
}
