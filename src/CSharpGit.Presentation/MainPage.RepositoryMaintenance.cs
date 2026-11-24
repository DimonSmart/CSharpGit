using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Previewing;
using CSharpGit.Presentation.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly RepositoryMaintenanceViewModel _repositoryMaintenance = null!;
    private readonly Workflows.RepositoryMaintenanceController _maintenanceController = null!;
    private SettingsWindowController _settingsWindowController = null!;
    private readonly Dialogs.AboutUpdateDialogController _aboutUpdate = null!;
    private IGitCommandActivitySource _gitCommandActivitySource = null!;
    private IAppSettingsService _gitConsoleSettings = null!;
    private Dialogs.GitCommandFeedbackController _feedback = null!;
    private readonly Workflows.RepositoryManualRefreshController _manualRefresh = null!;
    private readonly Workflows.RepositoryPushController _repositoryPush = null!;
    private readonly Workflows.RepositoryConfirmationController _confirmationController = null!;
    private readonly Workflows.BranchRenameController _branchRename = null!;
    private readonly Workflows.WorktreeController _worktrees = null!;
    private WorktreesViewModel _worktreesViewModel = null!;
    private readonly Workflows.RepositorySwitchController _repositorySwitch = null!;
    private readonly Workflows.HistoryRewriteController _historyRewrite = null!;
    private readonly Workflows.BranchDragDropController _branchDragDrop = null!;
    private readonly Workflows.StashDialogController _stashDialogs = null!;
    private readonly Workflows.StashInteractionController _stashInteractions = null!;
    private readonly Workflows.TagWorkflowController _tagWorkflows = null!;
    private readonly Workflows.InteractiveRebaseAuthorController _rebaseAuthor = null!;
    private readonly Workflows.SubmoduleWorkflowController _submodules = null!;
    private readonly Workflows.BranchDeletionController _branchDeletion = null!;
    private readonly Workflows.EditCommitMessageController _editCommitMessage = null!;
    private readonly Workflows.CommitActionController _commitActions = null!;
    private readonly Workflows.RepositoryReferenceMenuController _referenceMenus = null!;
    private readonly CloneRepositoryViewModel _cloneRepositoryViewModel = null!;
    private readonly CreateRepositoryViewModel _createRepositoryViewModel = null!;
    private readonly Workflows.RepositoryCloneWorkflow _cloneWorkflow = null!;
    private readonly Workflows.RepositoryCreationWorkflow _creationWorkflow = null!;
    private readonly Workflows.CommitDetailsLayoutController _commitDetailsLayout = null!;
    private bool _repositoryCloningWorkflowActive => _cloneWorkflow?.IsActive == true;
    private bool _repositoryCreationWorkflowActive => _creationWorkflow?.IsActive == true;
    private readonly Workflows.RecentRepositoryPickerWorkflow _repositoryPicker = null!;
    private readonly IDesktopShellService _desktopShellService = null!;
    private IAppSettingsService _recentRepositorySettings = null!;
    private IRepositoryImageService _repositoryImageService = null!;
    private IAuthorAvatarService _authorAvatarService = null!;
    private IAppSettingsService _authorAvatarSettings = null!;

    private readonly IRepositoryFileVersionService _fileVersionService = null!;
    private readonly IExternalGitToolService _externalGitToolService = null!;
    private readonly FileActionsViewModel _fileActions = null!;
    private readonly ImageDiffPreviewService _imageDiffService = null!;
    private readonly Workflows.CommitImageDiffWorkflow _commitImageDiff = null!;
    private readonly Workflows.FileActionAvailabilityController _fileActionButtons = null!;
    private readonly Workflows.FileActionInteractionController _fileActionInteractions = null!;

    private MainPage(
        OpenRepositoryViewModel viewModel,
        IWorkingTreeStatusReader workingTreeStatusReader,
        IRepositoryFileVersionService fileVersionService,
        IExternalGitToolService externalGitToolService,
        FileActionsViewModel fileActions,
        WorkspaceNavigator workspaceNavigator)
        : this(viewModel, workingTreeStatusReader, workspaceNavigator)
    {
        _fileVersionService = fileVersionService ?? throw new ArgumentNullException(nameof(fileVersionService));
        _externalGitToolService = externalGitToolService ?? throw new ArgumentNullException(nameof(externalGitToolService));
        _fileActions = fileActions ?? throw new ArgumentNullException(nameof(fileActions));
        _fileActions.Attach(_viewModel);
        _imageDiffService = new ImageDiffPreviewService(
            _fileVersionService, _fileActions.PathResolver, SharedImageMetadataReader.Instance);
        _commitImageDiff = new Workflows.CommitImageDiffWorkflow(
            _viewModel, _fileActions, _imageDiffService, CommitImageDiffHost,
            () => IsShuttingDown,
            _diffPresentation.RenderCommit,
            ReportUiFailure);
        _fileActionButtons = new Workflows.FileActionAvailabilityController(
            _viewModel, _fileActions,
            new Workflows.FileActionAvailabilityController.Controls(
                CommitOpenOriginalButton, CommitOpenChangedButton,
                CommitExternalDiffButton, CommitRevealButton),
            new Workflows.FileActionAvailabilityController.Controls(
                WorkingTreeOpenOriginalButton, WorkingTreeOpenChangedButton,
                WorkingTreeExternalDiffButton, WorkingTreeRevealButton));
        _fileActionInteractions = new Workflows.FileActionInteractionController(
            _viewModel, _fileActions, _fileActionButtons, ChangedFilesTree,
            HistoryPane, WorkingTreePane,
            () => XamlRoot, ShowPageDialogAsync, ShowErrorAsync, ReportUiFailure,
            () => _settingsWindowController.Open(SettingsSection.GitTools),
            () => IsShuttingDown,
            (operation, description) => RunPageUiOperation(operation, description));
        _fileActionInteractions.Initialize();
    }

    private void AddMenuItem(MenuFlyout flyout, string text, bool enabled, Func<Task> action) =>
        AddMenuItem(flyout, text, () => enabled, action);

    private void AddMenuItem(MenuFlyout flyout, string text, Func<bool> canExecute, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = canExecute() };

        void UpdateAvailability(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(OpenRepositoryViewModel.IsBusy)
                or nameof(OpenRepositoryViewModel.Repository)
                or nameof(OpenRepositoryViewModel.CanChangeRepository))
                item.IsEnabled = canExecute();
        }

        void Detach(object? sender, object args)
        {
            _viewModel.PropertyChanged -= UpdateAvailability;
            flyout.Closed -= Detach;
        }

        _viewModel.PropertyChanged += UpdateAvailability;
        flyout.Closed += Detach;
        item.Click += (_, _) => RunPageUiOperation(action, "Menu action", showError: true);
        flyout.Items.Add(item);
    }

    internal bool IsRepositoryMaintenanceInProgress => _repositoryMaintenance.IsRunning;
    internal bool IsApplicationUpdateInProgress => _aboutUpdate.IsRunning;
    internal bool IsHistoryRewriteInProgress => _viewModel.RepositoryHistoryRewrite.IsInProgress;

    internal MainPage(
        MainPageRepositoryDependencies repositoryDependencies,
        MainPageGitDependencies gitDependencies,
        MainPageUiDependencies uiDependencies,
        MainPageUpdateDependencies updateDependencies)
        : this(
            repositoryDependencies.ViewModel,
            gitDependencies.WorkingTreeStatusReader,
            gitDependencies.FileVersionService,
            gitDependencies.ExternalGitToolService,
            repositoryDependencies.FileActions,
            repositoryDependencies.RepositoryFiles,
            repositoryDependencies.WorkspaceNavigator)
    {
        var createRepositoryViewModel = repositoryDependencies.CreateRepository;
        var cloneRepositoryViewModel = repositoryDependencies.CloneRepository;
        var desktopShellService = gitDependencies.DesktopShell;
        var submoduleService = gitDependencies.SubmoduleService;
        var repositoryMaintenance = repositoryDependencies.Maintenance;
        var appSettings = uiDependencies.AppSettings;
        var gitCommandActivitySource = uiDependencies.GitCommandActivitySource;
        var worktreesViewModel = repositoryDependencies.Worktrees;
        var repositoryImageService = uiDependencies.RepositoryImageService;
        var authorAvatarService = uiDependencies.AuthorAvatarService;
        var recentRepositoryFolderPicker = uiDependencies.RecentRepositoryFolderPicker;
        var settingsWindowController = uiDependencies.SettingsWindowController;
        var applicationVersionProvider = updateDependencies.ApplicationVersionProvider;
        var updateCheckService = updateDependencies.UpdateCheckService;
        var applicationUpdateInstaller = updateDependencies.ApplicationUpdateInstaller;
        var systemUriLauncher = updateDependencies.SystemUriLauncher;
        var updateActivityState = updateDependencies.UpdateActivityState;
        _repositorySwitch = new Workflows.RepositorySwitchController(
            _viewModel,
            () => _repositoryCloningWorkflowActive,
            () => _repositoryCreationWorkflowActive,
            () => IsShuttingDown,
            () => XamlRoot, ShowPageDialogAsync,
            RefreshPresentationCollections, RepositoryHeader.Refresh);
        _commitDetailsLayout = new Workflows.CommitDetailsLayoutController(
            DetailsScroller, HistoryPane, CommitDetailsContent,
            () => XamlRoot,
            () => Microsoft.UI.Xaml.Application.Current is App app
                ? app.MainWindowClientWidth : 0);
        _historyRewrite = new Workflows.HistoryRewriteController(
            _viewModel, _repositoryFilesViewModel, _repositoryFilePreview,
            () => IsShuttingDown, () => XamlRoot, ShowPageDialogAsync,
            hash => _commitActions.RestoreSelectionAsync(hash),
            UpdateRepositoryFilesModeSurface, _workspaceNavigation.UpdateHistoryPresentation,
            () => { HistoryPane.Visibility = Visibility.Visible; WorkingTreePane.Visibility = Visibility.Collapsed; });
        _stashDialogs = new Workflows.StashDialogController(
            _viewModel, () => XamlRoot, ShowPageDialogAsync,
            () => IsShuttingDown, ExecuteCommandAsync);
        _stashInteractions = new Workflows.StashInteractionController(
            _viewModel, _stashDialogs, HistoryPane, WorkingTreeCommitEditor,
            (operation, description) => RunPageUiOperation(operation, description));
        _stashInteractions.Initialize();
        _tagWorkflows = new Workflows.TagWorkflowController(
            _viewModel, () => XamlRoot, ShowPageDialogAsync,
            () => IsShuttingDown, ShowErrorAsync, ShowInformationAsync,
            ReportUiFailure,
            (operation, description) => RunPageUiOperation(operation, description));
        _rebaseAuthor = new Workflows.InteractiveRebaseAuthorController(
            _viewModel, _pageDialogs.GetDialog("InteractiveRebaseDialog"),
            () => IsShuttingDown,
            (operation, description) => RunPageUiOperation(operation, description),
            ReportUiFailure);
        _rebaseAuthor.Initialize();
        _editCommitMessage = new Workflows.EditCommitMessageController(
            _viewModel, () => XamlRoot, ShowPageDialogAsync,
            () => IsShuttingDown,
            result => _commitActions.HandleExecutionResultAsync(result),
            hash => _commitActions.RestoreSelectionAsync(hash));
        _commitActions = new Workflows.CommitActionController(
            _viewModel, HistoryView, _editCommitMessage, _tagWorkflows,
            () => XamlRoot, ShowPageDialogAsync, ShowErrorAsync,
            () => IsShuttingDown, ShowInteractiveRebaseEditorAsync,
            (operation, description) => RunPageUiOperation(operation, description));
        _commitActions.Initialize();
        _branchDeletion = new Workflows.BranchDeletionController(
            _viewModel, () => XamlRoot, ShowPageDialogAsync,
            () => IsShuttingDown, ShowErrorAsync, ShowInformationAsync);
        _createRepositoryViewModel = createRepositoryViewModel
            ?? throw new ArgumentNullException(nameof(createRepositoryViewModel));
        _cloneRepositoryViewModel = cloneRepositoryViewModel
            ?? throw new ArgumentNullException(nameof(cloneRepositoryViewModel));
        _desktopShellService = desktopShellService ?? throw new ArgumentNullException(nameof(desktopShellService));
        _repositoryMaintenance = repositoryMaintenance
            ?? throw new ArgumentNullException(nameof(repositoryMaintenance));
        _submodules = new Workflows.SubmoduleWorkflowController(
            submoduleService ?? throw new ArgumentNullException(nameof(submoduleService)),
            _viewModel, _repositoryFilesViewModel,
            () => XamlRoot, ShowPageDialogAsync, () => IsShuttingDown,
            ShowErrorAsync, (operation, description) => RunPageUiOperation(operation, description),
            AddMenuItem, CopyTextAsync);
        _recentRepositorySettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _gitConsoleSettings = appSettings;
        _gitCommandActivitySource = gitCommandActivitySource ?? throw new ArgumentNullException(nameof(gitCommandActivitySource));
        _worktreesViewModel = worktreesViewModel ?? throw new ArgumentNullException(nameof(worktreesViewModel));
        _worktrees = new Workflows.WorktreeController(
            _viewModel, _worktreesViewModel, _desktopShellService,
            () => XamlRoot, ShowPageDialogAsync, () => IsShuttingDown,
            SynchronizeWorktreePresentation, ShowErrorAsync,
            OpenFolderInDesktopShellAsync, CopyTextAsync,
            AddMenuItem, AddMenuItem);
        _branchRename = new Workflows.BranchRenameController(
            _viewModel, _worktreesViewModel, RepositoryTree,
            () => XamlRoot,
            ShowPageDialogAsync,
            () => IsShuttingDown,
            (operation, description) => RunPageUiOperation(operation, description));
        _branchDragDrop = new Workflows.BranchDragDropController(
            RepositoryTree,
            _branchRename.CanRenameBranch,
            _branchRename.ExecuteAsync,
            () => IsShuttingDown,
            (operation, description) => RunPageUiOperation(operation, description));
        _referenceMenus = new Workflows.RepositoryReferenceMenuController(
            _viewModel, _worktreesViewModel, _worktrees, _branchRename, _branchDeletion,
            _tagWorkflows, _stashDialogs, _commitActions,
            _workspaceNavigation.ShowReferenceHistoryAsync, CopyTextAsync, ExecuteCommandAsync,
            () => XamlRoot, ShowPageDialogAsync, () => IsShuttingDown,
            (operation, description) => RunPageUiOperation(operation, description));
        _repositoryImageService = repositoryImageService ?? throw new ArgumentNullException(nameof(repositoryImageService));
        _authorAvatarService = authorAvatarService ?? throw new ArgumentNullException(nameof(authorAvatarService));
        _authorAvatarSettings = appSettings;
        CommitDetailsContent.ConfigureAuthorAvatar(_authorAvatarService, _authorAvatarSettings);
        _repositoryPicker = new Workflows.RecentRepositoryPickerWorkflow(
            recentRepositoryFolderPicker ?? throw new ArgumentNullException(nameof(recentRepositoryFolderPicker)),
            () => !_repositoryCloningWorkflowActive && !_repositoryCreationWorkflowActive
                  && _viewModel.CanChangeRepository && !IsShuttingDown,
            _repositorySwitch.TrySwitchAsync,
            ShowPageDialogAsync);
        _settingsWindowController = settingsWindowController ?? throw new ArgumentNullException(nameof(settingsWindowController));
        _settingsWindowController.ConfigureWindowContext(
            () => _viewModel.Repository, _viewModel.TryBeginGitSettingsOperation);
        ConfigureRepositoryFilesActions();
        _aboutUpdate = new Dialogs.AboutUpdateDialogController(
            applicationVersionProvider ?? throw new ArgumentNullException(nameof(applicationVersionProvider)),
            updateCheckService ?? throw new ArgumentNullException(nameof(updateCheckService)),
            applicationUpdateInstaller ?? throw new ArgumentNullException(nameof(applicationUpdateInstaller)),
            systemUriLauncher ?? throw new ArgumentNullException(nameof(systemUriLauncher)),
            updateActivityState ?? throw new ArgumentNullException(nameof(updateActivityState)),
            () => XamlRoot,
            ShowPageDialogAsync,
            (operation, description) => RunPageUiOperation(operation, description),
            ReportUiFailure,
            () => IsShuttingDown,
            () => IsHistoryRewriteInProgress,
            () => IsRepositoryMaintenanceInProgress,
            () => _viewModel.CommitCreation.HasUnappliedCommitMessage,
            ConfirmCloseAsync,
            () =>
            {
                if (Microsoft.UI.Xaml.Application.Current is App app)
                    app.CompleteApplicationUpdateRestart(this);
            });

        RepositoryExplorer.Configure(_viewModel, _worktreesViewModel, _workspaceNavigator, _worktrees, _referenceMenus);
        RepositoryExplorer.NavigateRequested += (node, version) =>
            RunPageUiOperation(() => _workspaceNavigation.SelectRepositoryNodeAsync(node, version), "Repository tree navigation");
        RepositoryExplorer.CheckoutRequested += (branch, version) =>
            RunPageUiOperation(() => _workspaceNavigation.SwitchBranchAsync(branch, version), "Switch branch");

        _repositoryMaintenance.Attach(_viewModel, () => _worktreesViewModel.RefreshAsync(throwOnError: true));
        _maintenanceController = new Workflows.RepositoryMaintenanceController(
            _viewModel, _repositoryMaintenance,
            () => XamlRoot, () => IsShuttingDown,
            RepositoryToolbar.SetOptimizeAvailability,
            () => GitConsoleHost.Open(null, manualOpen: true),
            ReportUiFailure);
        _maintenanceController.Initialize();
        _repositoryRefresh.InitializeRepositoryChangeMonitoring();
        _confirmationController = new Workflows.RepositoryConfirmationController(
            _viewModel,
            () => XamlRoot,
            ShowPageDialogAsync,
            (operation, description) => RunPageUiOperation(operation, description),
            ExecuteCommandAsync,
            () => IsShuttingDown);
        _manualRefresh = new Workflows.RepositoryManualRefreshController(
            _viewModel,
            () => IsShuttingDown,
            () => _repositoryRefresh.IsRefreshInProgress,
            _repositoryRefresh.SetRefreshInProgress,
            RefreshPresentationCollections,
            ReportUiFailure,
            ShowErrorAsync);
        _repositoryPush = new Workflows.RepositoryPushController(
            _viewModel, () => XamlRoot, ShowPageDialogAsync,
            ShowErrorAsync, RefreshPresentationCollections,
            () => IsShuttingDown);
        StartScreenHost.Configure(_viewModel, _recentRepositorySettings, _repositoryImageService,
            _desktopShellService.OpenFolderDescription,
            new StartScreenActions(
                OpenRepositoryPickerAsync,
                () => _cloneWorkflow.ShowAsync(),
                () => _creationWorkflow.ShowAsync(),
                _repositorySwitch.TrySwitchAsync,
                _repositorySwitch.ShowUnavailableAsync,
                path => OpenFolderInDesktopShellAsync(path, "Could not open repository folder"),
                CopyTextAsync,
                () => _settingsWindowController.Open(SettingsSection.General),
                _aboutUpdate.ShowAboutAsync));
        HistoryView.Configure(_viewModel.History, _workspaceNavigator,
            () => _viewModel.IsBusy, _authorAvatarService, _authorAvatarSettings);
        HistoryView.ConfigurePerformance(
            _viewModel,
            _recentRepositorySettings,
            _gitCommandActivitySource,
            _authorAvatarService,
            _desktopShellService,
            ShowErrorAsync,
            async dialog => { await ShowPageDialogAsync(dialog); },
            ReportUiFailure);
        _feedback = new Dialogs.GitCommandFeedbackController(
            _gitCommandActivitySource, _gitConsoleSettings,
            FloatingGitCommandFeedbackView, GitCommandFeedbackModalHost,
            RootLayout, GitConsoleHost, DispatcherQueue, () => IsShuttingDown);
        _cloneWorkflow = new Workflows.RepositoryCloneWorkflow(
            _viewModel, _cloneRepositoryViewModel,
            () => _repositoryCreationWorkflowActive,
            () => IsShuttingDown,
            () => _repositorySwitch.ConfirmDiscardAsync(closing: false),
            _repositorySwitch.SwitchCoreAsync,
            () => XamlRoot,
            ShowPageDialogAsync,
            (operation, description) => RunPageUiOperation(operation, description),
            _feedback);
        _creationWorkflow = new Workflows.RepositoryCreationWorkflow(
            _viewModel, _createRepositoryViewModel,
            () => _repositoryCloningWorkflowActive,
            () => IsShuttingDown,
            () => _repositorySwitch.ConfirmDiscardAsync(closing: false),
            _repositorySwitch.SwitchCoreAsync,
            () => XamlRoot,
            ShowPageDialogAsync,
            (operation, description) => RunPageUiOperation(operation, description));
        _feedback.InitializeGitCommandFeedback();
        GitConsoleHost.Configure(
            _gitCommandActivitySource, _gitConsoleSettings, RootLayout,
            _feedback.RouteGitCommandFeedback,
            _feedback.RenderGitCommandFeedback,
            () => GitCommandFeedbackModalHost.Visibility == Visibility.Visible);
        _worktrees.Initialize();
    }

    internal Task<bool> ConfirmedSwitchForDesktopCheckAsync(string path) =>
        _repositorySwitch.ConfirmedSwitchForDesktopCheckAsync(path);

    internal bool ConfirmedCloseForDesktopCheck() =>
        _repositorySwitch.ConfirmedCloseForDesktopCheck();

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

    private Task OpenRepositoryPickerAsync() => _repositoryPicker.OpenAsync();

}
