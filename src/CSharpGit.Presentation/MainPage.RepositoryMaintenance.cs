using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Views;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly RepositoryMaintenanceViewModel _repositoryMaintenance = null!;
    private readonly IDesktopShellService _desktopShellService = null!;
    private IAppSettingsService _recentRepositorySettings = null!;
    private IRepositoryImageService _repositoryImageService = null!;

    internal bool IsRepositoryMaintenanceInProgress => _repositoryMaintenance.IsRunning;

    public MainPage(
        OpenRepositoryViewModel viewModel,
        CreateRepositoryViewModel createRepositoryViewModel,
        CloneRepositoryViewModel cloneRepositoryViewModel,
        IWorkingTreeStatusReader workingTreeStatusReader,
        IDesktopShellService desktopShellService,
        IRepositoryFileVersionService fileVersionService,
        IExternalGitToolService externalGitToolService,
        RepositoryFilesViewModel repositoryFilesViewModel,
        FileActionsViewModel fileActions,
        RepositoryMaintenanceViewModel repositoryMaintenance,
        IAppSettingsService appSettings,
        IGitCommandActivitySource gitCommandActivitySource,
        WorktreesViewModel worktreesViewModel,
        IRepositoryImageService repositoryImageService,
        IAuthorAvatarService authorAvatarService,
        RecentRepositoryFolderPicker recentRepositoryFolderPicker,
        SettingsWindowController settingsWindowController,
        IApplicationVersionProvider applicationVersionProvider,
        IUpdateCheckService updateCheckService,
        IApplicationUpdateInstaller applicationUpdateInstaller,
        ISystemUriLauncher systemUriLauncher,
        ApplicationUpdateActivityState updateActivityState)
        : this(
            viewModel,
            workingTreeStatusReader,
            fileVersionService,
            externalGitToolService,
            fileActions,
            repositoryFilesViewModel)
    {
        InstallRepositoryHistoryRewriteMenus();
        _createRepositoryViewModel = createRepositoryViewModel
            ?? throw new ArgumentNullException(nameof(createRepositoryViewModel));
        _cloneRepositoryViewModel = cloneRepositoryViewModel
            ?? throw new ArgumentNullException(nameof(cloneRepositoryViewModel));
        _desktopShellService = desktopShellService ?? throw new ArgumentNullException(nameof(desktopShellService));
        _repositoryMaintenance = repositoryMaintenance
            ?? throw new ArgumentNullException(nameof(repositoryMaintenance));
        _recentRepositorySettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _gitConsoleSettings = appSettings;
        _gitCommandActivitySource = gitCommandActivitySource ?? throw new ArgumentNullException(nameof(gitCommandActivitySource));
        _worktreesViewModel = worktreesViewModel ?? throw new ArgumentNullException(nameof(worktreesViewModel));
        _repositoryImageService = repositoryImageService ?? throw new ArgumentNullException(nameof(repositoryImageService));
        _authorAvatarService = authorAvatarService ?? throw new ArgumentNullException(nameof(authorAvatarService));
        _authorAvatarSettings = appSettings;
        CommitDetailsContent.ConfigureAuthorAvatar(_authorAvatarService, _authorAvatarSettings);
        _recentRepositoryFolderPicker = recentRepositoryFolderPicker ?? throw new ArgumentNullException(nameof(recentRepositoryFolderPicker));
        _settingsWindowController = settingsWindowController ?? throw new ArgumentNullException(nameof(settingsWindowController));
        _applicationVersionProvider = applicationVersionProvider ?? throw new ArgumentNullException(nameof(applicationVersionProvider));
        _updateCheckService = updateCheckService ?? throw new ArgumentNullException(nameof(updateCheckService));
        _applicationUpdateInstaller = applicationUpdateInstaller ?? throw new ArgumentNullException(nameof(applicationUpdateInstaller));
        _systemUriLauncher = systemUriLauncher ?? throw new ArgumentNullException(nameof(systemUriLauncher));
        _applicationUpdateActivity = updateActivityState ?? throw new ArgumentNullException(nameof(updateActivityState));

        RepositoryExplorer.Configure(_viewModel, _worktreesViewModel, _workspaceNavigator);
        RepositoryExplorer.NavigateRequested += (node, version) =>
            RunPageUiOperation(() => SelectRepositoryNodeAsync(node, version), "Repository tree navigation");
        RepositoryExplorer.CheckoutRequested += (branch, version) =>
            RunPageUiOperation(() => SwitchRepositoryTreeBranchAsync(branch, version), "Switch branch");
        RepositoryExplorer.ReferenceMenuRequested += RepositoryTreeBranchDeletion_RightTapped;

        _repositoryMaintenance.Attach(_viewModel, () => _worktreesViewModel.RefreshAsync(throwOnError: true));
        _repositoryMaintenance.PropertyChanged += RepositoryMaintenance_PropertyChanged;
        _viewModel.PropertyChanged += RepositoryMaintenanceViewModel_PropertyChanged;
        UpdateOptimizeRepositoryAvailability();
        InitializeRepositoryChangeMonitoring();
        StartScreenHost.Configure(_viewModel, _recentRepositorySettings, _repositoryImageService,
            _desktopShellService.OpenFolderDescription,
            new StartScreenActions(
                OpenRepositoryPickerAsync,
                ShowCloneRepositoryAsync,
                ShowCreateRepositoryAsync,
                TrySwitchRepositoryAsync,
                ShowUnavailableRepositoryAsync,
                path => OpenFolderInDesktopShellAsync(path, "Could not open repository folder"),
                CopyTextAsync,
                () => OpenSettingsWindow(SettingsSection.General),
                ShowAboutAsync));
        _recentRepositorySettings.Changed += StartScreenSettings_Changed;
        InitializeHistoryPerformanceDiagnostics();
        InitializeGitConsole();
        InitializeWorktreeSupport();
    }

    private void RepositoryMaintenance_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(RepositoryMaintenanceViewModel.IsRunning)
            or nameof(RepositoryMaintenanceViewModel.CanStart))
            UpdateOptimizeRepositoryAvailability();
    }

    private void RepositoryMaintenanceViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(OpenRepositoryViewModel.Repository))
            _repositoryMaintenance.Invalidate();

        if (args.PropertyName is nameof(OpenRepositoryViewModel.Repository)
            or nameof(OpenRepositoryViewModel.IsBusy)
            or nameof(RepositoryOperationsViewModel.CurrentOperation))
            UpdateOptimizeRepositoryAvailability();
    }

    private void UpdateOptimizeRepositoryAvailability() =>
        RepositoryToolbar.SetOptimizeAvailability(_repositoryMaintenance.CanStart);

    private void OptimizeRepository_Click(object sender, RoutedEventArgs e) =>
        RunPageUiOperation(OptimizeRepositoryAsync, "Optimize repository");

    private async Task OptimizeRepositoryAsync()
    {
        if (!_repositoryMaintenance.CanStart
            || _viewModel.Repository is not { } repository)
            return;

        if (XamlRoot is not { } root) return;
        var dialog = new RepositoryMaintenanceDialog(
            root, () => OpenGitConsole(null, manualOpen: true));

        try
        {
            if (!await _repositoryMaintenance.PrepareAsync(repository))
            {
                if (!ReferenceEquals(repository, _viewModel.Repository) || IsShuttingDown)
                    return;

                if (_repositoryMaintenance.PreparationFailure is { } failure)
                    await dialog.ShowMessageAsync("Could not read repository statistics", failure.Message);
                else
                    await dialog.ShowMessageAsync("Optimization not started",
                        "Repository state changed before optimization could start.");
                return;
            }

            if (!ReferenceEquals(repository, _viewModel.Repository) || IsShuttingDown)
                return;

            await dialog.ShowAsync(_repositoryMaintenance, repository);
        }
        catch (OperationCanceledException) when (IsShuttingDown)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ReportUiFailure(exception, "Repository optimization");
            if (!IsShuttingDown && ReferenceEquals(repository, _viewModel.Repository))
            {
                try { await dialog.ShowMessageAsync("Optimization not started", exception.Message); }
                catch (Exception notificationError)
                {
                    ReportUiFailure(notificationError, "Repository optimization error dialog");
                }
            }
        }
    }
}
