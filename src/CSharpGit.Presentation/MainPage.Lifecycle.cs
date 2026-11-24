using Microsoft.UI.Xaml;
using CSharpGit.Presentation.Views;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly Windowing.WindowWorkspaceLifetime _workspaceLifetime = new();
    private bool IsShuttingDown => _workspaceLifetime.IsShuttingDown;

    internal CSharpGit.Presentation.ViewModels.CloneRepositoryViewModel DesktopCloneForCheck => _cloneRepositoryViewModel;
    internal CSharpGit.Presentation.ViewModels.CreateRepositoryViewModel DesktopCreateForCheck => _createRepositoryViewModel;
    internal CSharpGit.Presentation.ViewModels.RepositoryMaintenanceViewModel DesktopMaintenanceForCheck => _repositoryMaintenance;

    private void MainPage_Loaded(object sender, RoutedEventArgs args)
    {
        Loaded -= MainPage_Loaded;
        _commitDetailsLayout.Initialize();
        _confirmationController.Initialize();
        _branchRename.Initialize();
        _branchDragDrop.Initialize();
        RepositoryToolbar.Configure(_toolbarActions.Handle);
        RepositoryHeader.Configure(_viewModel, _recentRepositorySettings, _desktopShellService,
            new RepositorySelectorActions(
                _repositorySwitch.TrySwitchAsync,
                OpenRepositoryPickerAsync,
                () => _cloneWorkflow.ShowAsync(),
                () => _creationWorkflow.ShowAsync(),
                _repositorySwitch.TryCloseAsync,
                path => OpenFolderInDesktopShellAsync(path, "Could not open repository folder"),
                Windowing.WindowUiOperations.CopyTextAsync,
                _ui.ShowErrorAsync));
    }

    // This method registers ownership; the lifetime executes and observes cleanup.
    private void ConfigureWorkspaceLifetime()
    {
        _workspaceLifetime.Configure(
            [
                () => _pageDialogs.Shutdown(),
                () => _repositoryRefresh.ShutdownRepositoryChangeMonitoring(),
                () => _repositoryFilePreview.Cancel(),
                () => _commitImageDiff.Cancel(clearSurface: false),
                () => _workingTreeImageDiff.Cancel(),
                () => _cloneRepositoryViewModel.Cancel(),
            ],
            () => HistoryView.StopHistoryPerformanceCaptureOnShutdown(),
            [
            () => _pageDialogs.Shutdown(),
            () => _cloneRepositoryViewModel.Cancel(),
            () => Loaded -= RunDesktopCheckWhenRequested,
            () => Loaded -= MainPage_Loaded,
            () => _keyboardShortcuts.Shutdown(),
            () => _menu.Shutdown(),
            () => _rebaseAuthor.Shutdown(),
            () => _presentationRefresh.Shutdown(),
            () => DetachRepositoryTreeStateTracking(),
            () => _stashInteractions.Shutdown(),
            () => _commitActions.Shutdown(),
            () => HistoryView.Shutdown(),
            () => GitConsoleHost.Shutdown(),
            () => _feedback.ShutdownGitCommandFeedback(),
            () => StartScreenHost.ShutdownRecentRepositories(),
            () => RepositoryHeader.Shutdown(),
            () => _branchDragDrop.Shutdown(),
            () => _branchRename.Shutdown(),
            () => _commitDetailsLayout.Shutdown(),
            () => RepositoryToolbar.Shutdown(),
            () => _repositoryRefresh.ShutdownRepositoryChangeMonitoring(),
            () => HistoryPane.ShutdownChanges(),
            () => WorkingTreePane.ShutdownWorkingTreeDiffSurface(),
            () => _worktrees.Shutdown(),
            () => _settingsWindowController.Shutdown(),
            () => _editCommitMessage.ShutdownIdleContinuation(),
            () => _commitImageDiff.ShutdownIdleContinuation(),
            () => _repositoryFilePreview.ShutdownIdleContinuation(),
            () => _workspaceNavigation.Shutdown(),
            () => _confirmationController.Shutdown(),
            () => _fileActionInteractions.Shutdown(),
            () => _repositoryFilesSurface?.ShutdownInteractions(),
            () => _maintenanceController.Shutdown(),
            ]);
    }

    public Task PrepareForShutdown() => _workspaceLifetime.PrepareAsync();

    public void BeginShutdown() => _workspaceLifetime.Complete();
}
