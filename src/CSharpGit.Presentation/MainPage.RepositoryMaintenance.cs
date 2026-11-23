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
    private readonly Workflows.BranchDeletionController _branchDeletion = null!;
    private readonly Workflows.EditCommitMessageController _editCommitMessage = null!;
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

    internal bool IsRepositoryMaintenanceInProgress => _repositoryMaintenance.IsRunning;
    internal bool IsApplicationUpdateInProgress => _aboutUpdate.IsRunning;
    internal bool IsHistoryRewriteInProgress => _viewModel.RepositoryHistoryRewrite.IsInProgress;

    public MainPage(
        OpenRepositoryViewModel viewModel,
        CreateRepositoryViewModel createRepositoryViewModel,
        CloneRepositoryViewModel cloneRepositoryViewModel,
        IWorkingTreeStatusReader workingTreeStatusReader,
        IDesktopShellService desktopShellService,
        IRepositoryFileVersionService fileVersionService,
        IExternalGitToolService externalGitToolService,
        RepositoryFilesViewModel repositoryFilesViewModel,
        ISubmoduleService submoduleService,
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
        ApplicationUpdateActivityState updateActivityState,
        WorkspaceNavigator workspaceNavigator)
        : this(
            viewModel,
            workingTreeStatusReader,
            fileVersionService,
            externalGitToolService,
            fileActions,
            repositoryFilesViewModel,
            workspaceNavigator)
    {
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
            RestoreCommitActionSelectionAsync,
            UpdateRepositoryFilesModeSurface, UpdateHistoryScopePresentation,
            () => { HistoryPane.Visibility = Visibility.Visible; WorkingTreePane.Visibility = Visibility.Collapsed; });
        _stashDialogs = new Workflows.StashDialogController(
            _viewModel, () => XamlRoot, ShowPageDialogAsync,
            () => IsShuttingDown, ExecuteCommandAsync);
        _editCommitMessage = new Workflows.EditCommitMessageController(
            _viewModel, () => XamlRoot, ShowPageDialogAsync,
            () => IsShuttingDown, HandleCommitActionExecutionResultAsync,
            RestoreCommitActionSelectionAsync);
        _branchDeletion = new Workflows.BranchDeletionController(
            _viewModel, () => XamlRoot, ShowPageDialogAsync,
            () => IsShuttingDown, ShowErrorAsync, ShowInformationAsync);
        WorkingTreeCommitEditor.StashRequested += CreateStash_Click;
        _createRepositoryViewModel = createRepositoryViewModel
            ?? throw new ArgumentNullException(nameof(createRepositoryViewModel));
        _cloneRepositoryViewModel = cloneRepositoryViewModel
            ?? throw new ArgumentNullException(nameof(cloneRepositoryViewModel));
        _desktopShellService = desktopShellService ?? throw new ArgumentNullException(nameof(desktopShellService));
        _repositoryMaintenance = repositoryMaintenance
            ?? throw new ArgumentNullException(nameof(repositoryMaintenance));
        _submoduleService = submoduleService ?? throw new ArgumentNullException(nameof(submoduleService));
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
        _repositoryImageService = repositoryImageService ?? throw new ArgumentNullException(nameof(repositoryImageService));
        _authorAvatarService = authorAvatarService ?? throw new ArgumentNullException(nameof(authorAvatarService));
        _authorAvatarSettings = appSettings;
        CommitDetailsContent.ConfigureAuthorAvatar(_authorAvatarService, _authorAvatarSettings);
        _repositoryPicker = new Workflows.RecentRepositoryPickerWorkflow(
            recentRepositoryFolderPicker ?? throw new ArgumentNullException(nameof(recentRepositoryFolderPicker)),
            () => !_repositoryCloningWorkflowActive && !_repositoryCreationWorkflowActive
                  && _viewModel.CanChangeRepository && !IsShuttingDown,
            TrySwitchRepositoryAsync,
            ShowPageDialogAsync);
        _settingsWindowController = settingsWindowController ?? throw new ArgumentNullException(nameof(settingsWindowController));
        _settingsWindowController.ConfigureWindowContext(
            () => _viewModel.Repository, _viewModel.TryBeginGitSettingsOperation);
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

        RepositoryExplorer.Configure(_viewModel, _worktreesViewModel, _workspaceNavigator);
        RepositoryExplorer.NavigateRequested += (node, version) =>
            RunPageUiOperation(() => SelectRepositoryNodeAsync(node, version), "Repository tree navigation");
        RepositoryExplorer.CheckoutRequested += (branch, version) =>
            RunPageUiOperation(() => SwitchRepositoryTreeBranchAsync(branch, version), "Switch branch");
        RepositoryExplorer.ReferenceMenuRequested += RepositoryTreeBranchDeletion_RightTapped;

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
                TrySwitchRepositoryAsync,
                ShowUnavailableRepositoryAsync,
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
            () => ConfirmDiscardCommitMessageAsync(closing: false),
            SwitchRepositoryCoreAsync,
            () => XamlRoot,
            ShowPageDialogAsync,
            (operation, description) => RunPageUiOperation(operation, description),
            _feedback);
        _creationWorkflow = new Workflows.RepositoryCreationWorkflow(
            _viewModel, _createRepositoryViewModel,
            () => _repositoryCloningWorkflowActive,
            () => IsShuttingDown,
            () => ConfirmDiscardCommitMessageAsync(closing: false),
            SwitchRepositoryCoreAsync,
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

    private Task OpenRepositoryPickerAsync() => _repositoryPicker.OpenAsync();

}
