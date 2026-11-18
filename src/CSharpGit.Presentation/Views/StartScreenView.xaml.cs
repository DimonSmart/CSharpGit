using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Views;

/// <summary>
/// Owns the repository launcher and its subscriptions for the lifetime of this window.
/// The callbacks only cross into repository/dialog workflows that remain in phase 3.
/// </summary>
public sealed partial class StartScreenView : UserControl
{
    private OpenRepositoryViewModel _viewModel = null!;
    private StartScreenActions _actions = null!;
    private string _openFolderDescription = string.Empty;

    public StartScreenView() => InitializeComponent();

    internal void Configure(OpenRepositoryViewModel viewModel, IAppSettingsService settings,
        IRepositoryImageService imageService, string openFolderDescription, StartScreenActions actions)
    {
        if (_recentRepositoriesView is not null)
            throw new InvalidOperationException("The start screen is already configured.");

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _recentRepositorySettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _repositoryImageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        _openFolderDescription = openFolderDescription;
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        InitializeRecentRepositories();
    }

    private void OpenRepository_Click(object sender, RoutedEventArgs e) =>
        RunStartScreenOperation(_actions.OpenRepository, "Open repository");

    private void CloneRepository_Click(object sender, RoutedEventArgs e) =>
        RunStartScreenOperation(_actions.CloneRepository, "Clone repository");

    private void CreateRepository_Click(object sender, RoutedEventArgs e) =>
        RunStartScreenOperation(_actions.CreateRepository, "Create repository");

    private void Settings_Click(object sender, RoutedEventArgs e) => _actions.OpenSettings();

    private void About_Click(object sender, RoutedEventArgs e) =>
        RunStartScreenOperation(_actions.ShowAbout, "About CSharpGit");

    private void RunStartScreenOperation(Func<Task> work, string operation)
    {
        if (_recentRepositoriesShutdown) return;
        _ = RunAsync();

        async Task RunAsync()
        {
            try { await work(); }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Start screen {operation}: {exception}");
            }
        }
    }

    private IAppSettingsService _recentRepositorySettings = null!;
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
            item => _actions.CopyPath(item.Path),
            _openFolderDescription,
            _actions.OpenRepository,
            _actions.CloneRepository,
            _actions.CreateRepository,
            () => _viewModel.CanChangeRepository,
            DispatcherQueue);
        _recentRepositoriesView = new RecentRepositoriesView
        {
            DataContext = _recentRepositoriesViewModel
        };
        RecentRepositoriesHost.Content = _recentRepositoriesView;

        _recentRepositoryWasBusy = _viewModel.IsBusy;
        _viewModel.PropertyChanged += RecentRepositoryHost_PropertyChanged;
        _recentRepositorySettings.Changed += RecentRepositorySettings_Changed;
        UpdateStartScreenVisibility();
    }

    private void RecentRepositoryHost_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_recentRepositoriesShutdown) return;

        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.Repository))
        {
            UpdateStartScreenVisibility();
        }

        if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.CurrentBranchName))
            RunStartScreenOperation(UpdateRecentRepositoryBranchMetadataAsync, "Recent repository branch metadata");

        if (eventArgs.PropertyName is nameof(OpenRepositoryViewModel.IsBusy)
            or nameof(OpenRepositoryViewModel.CanChangeRepository)
            or nameof(OpenRepositoryViewModel.Repository))
            _recentRepositoriesViewModel?.RefreshRepositoryActionAvailability();

        if (eventArgs.PropertyName != nameof(OpenRepositoryViewModel.IsBusy)) return;

        var becameIdle = _recentRepositoryWasBusy && !_viewModel.IsBusy;
        _recentRepositoryWasBusy = _viewModel.IsBusy;
        if (becameIdle)
            RunStartScreenOperation(RecordOpenedRepositoryAsync, "Record recent repository");
    }

    private void RecentRepositorySettings_Changed(object? sender, EventArgs e)
    {
        if (_recentRepositoriesShutdown) return;

        if (DispatcherQueue.HasThreadAccess)
        {
            UpdateStartScreenVisibility();
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_recentRepositoriesShutdown)
                UpdateStartScreenVisibility();
        });
    }

    private void UpdateStartScreenVisibility()
    {
        if (_recentRepositoriesShutdown || _recentRepositoriesView is null) return;

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
            await _actions.ShowUnavailableRepository(item.Path);
            return;
        }

        await _actions.SwitchRepository(item.Path);
    }

    private async Task OpenRecentRepositoryFolderAsync(RecentRepositoryItem item)
    {
        if (!Directory.Exists(item.Path))
        {
            await _actions.ShowUnavailableRepository(item.Path);
            return;
        }

        await _actions.OpenFolder(item.Path);
    }

    private async Task UpdateRecentRepositoryBranchMetadataAsync()
    {
        if (_recentRepositoriesShutdown || _viewModel.Repository is not { } repository) return;

        var isDetachedHead = _viewModel.IsDetachedHead;
        var branchName = isDetachedHead
            ? null
            : _viewModel.Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent)?.Name
                ?? _viewModel.CurrentBranchName;
        if (!isDetachedHead && string.IsNullOrWhiteSpace(branchName)) return;

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
        var branchName = _viewModel.IsDetachedHead
            ? null
            : _viewModel.Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent)?.Name
                ?? _viewModel.CurrentBranchName;

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

    internal void ShutdownRecentRepositories()
    {
        if (_recentRepositoriesShutdown) return;
        _recentRepositoriesShutdown = true;
        _viewModel.PropertyChanged -= RecentRepositoryHost_PropertyChanged;
        _recentRepositorySettings.Changed -= RecentRepositorySettings_Changed;
        _recentRepositoriesViewModel?.Dispose();
    }

    private static bool PathsEqual(string left, string right) =>
        RecentRepositoriesProjectionBuilder.PathsEqual(left, right);
}
