using Microsoft.UI.Xaml;
using CSharpGit.Presentation.Views;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private int _shutdownStarted;
    private int _shutdownFinalized;

    private bool IsShuttingDown =>
        Volatile.Read(ref _shutdownStarted) != 0;

    internal CSharpGit.Presentation.ViewModels.CloneRepositoryViewModel DesktopCloneForCheck => _cloneRepositoryViewModel;
    internal CSharpGit.Presentation.ViewModels.CreateRepositoryViewModel DesktopCreateForCheck => _createRepositoryViewModel;
    internal CSharpGit.Presentation.ViewModels.RepositoryMaintenanceViewModel DesktopMaintenanceForCheck => _repositoryMaintenance;

    private void MainPage_Loaded(object sender, RoutedEventArgs args)
    {
        _commitDetailsLayout.Initialize();
        _confirmationController.Initialize();
        _branchRename.Initialize();
        _branchDragDrop.Initialize();
        RepositoryToolbar.Configure(HandleRepositoryToolbarAction);
        RepositoryHeader.Configure(_viewModel, _recentRepositorySettings, _desktopShellService,
            new RepositorySelectorActions(
                TrySwitchRepositoryAsync,
                OpenRepositoryPickerAsync,
                () => _cloneWorkflow.ShowAsync(),
                () => _creationWorkflow.ShowAsync(),
                TryCloseRepositoryAsync,
                path => OpenFolderInDesktopShellAsync(path, "Could not open repository folder"),
                CopyTextAsync,
                ShowErrorAsync));
    }

    // Close admission is decided by WindowCloseOrchestrator; the page only shuts down UI requests.
    public Task PrepareForShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return Task.CompletedTask;
        _pageDialogs.Shutdown();
        _repositoryRefresh.ShutdownRepositoryChangeMonitoring();
        // Stop page-owned cancelable previews before waiting for diagnostics.
        // Do not cancel Git mutations: close admission already protects them.
        _repositoryFilePreview.Cancel();
        _commitImageDiff.Cancel(clearSurface: false);
        _workingTreeImageDiff.Cancel();
        _cloneRepositoryViewModel.Cancel();
        return HistoryView.StopHistoryPerformanceCaptureOnShutdown();
    }

    public void BeginShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownFinalized, 1) != 0) return;
        Volatile.Write(ref _shutdownStarted, 1);
        _pageDialogs.Shutdown();
        _cloneRepositoryViewModel.Cancel();
        Loaded -= RunDesktopCheckWhenRequested;
        _viewModel.WorkingTree.Changes.CollectionChanged -= RepositoryPresentationChanges_CollectionChanged;
        _viewModel.Branches.LocalBranches.CollectionChanged -= RepositoryPresentationLocalBranches_CollectionChanged;
        _viewModel.Branches.RemoteBranches.CollectionChanged -= RepositoryPresentationRemoteBranches_CollectionChanged;
        _viewModel.RepositorySync.Remotes.CollectionChanged -= RepositoryPresentationRemotes_CollectionChanged;
        _viewModel.Tags.Items.CollectionChanged -= RepositoryPresentationTags_CollectionChanged;
        _viewModel.Stashes.Items.CollectionChanged -= RepositoryPresentationStashes_CollectionChanged;
        DetachRepositoryTreeStateTracking();
        WorkingTreeCommitEditor.StashRequested -= CreateStash_Click;
        HistoryView.Shutdown();
        GitConsoleHost.Shutdown();
        _feedback.ShutdownGitCommandFeedback();
        StartScreenHost.ShutdownRecentRepositories();
        RepositoryHeader.Shutdown();
        _branchDragDrop.Shutdown();
        _branchRename.Shutdown();
        _commitDetailsLayout.Shutdown();
        RepositoryToolbar.Shutdown();
        _repositoryRefresh.ShutdownRepositoryChangeMonitoring();
        HistoryPane.ShutdownChanges();
        _unstagedViewport?.Dispose();
        _stagedViewport?.Dispose();
        _worktrees.Shutdown();
        _settingsWindowController.Shutdown();

        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.Stashes.PropertyChanged -= StashesViewModel_PropertyChanged;
        _viewModel.CommitDetails.PropertyChanged -= CommitDetailsViewModel_PropertyChanged;
        _viewModel.History.PropertyChanged -= HistoryViewModel_PropertyChanged;
        _viewModel.History.CommitLookupCompleted -= History_CommitLookupCompleted;
        _confirmationController.Shutdown();
        _viewModel.PropertyChanged -= FileOpeningViewModel_PropertyChanged;
        _viewModel.CommitDetails.PropertyChanged -= FileOpeningViewModel_PropertyChanged;
        _viewModel.History.PropertyChanged -= FileOpeningViewModel_PropertyChanged;
        _viewModel.WorkingTree.PropertyChanged -= WorkingTreeViewModel_PropertyChanged;
        _viewModel.WorkingTree.PropertyChanged -= WorkingTreeFileOpening_PropertyChanged;
        _fileActions.PropertyChanged -= FileActions_PropertyChanged;
        ChangedFilesTree.RightTapped -= ChangedFilesTree_RightTapped;
        UnstagedChangesTree.Expanding -= WorkingTreeTree_Expanding;
        UnstagedChangesTree.Collapsed -= WorkingTreeTree_Collapsed;
        StagedChangesTree.Expanding -= WorkingTreeTree_Expanding;
        StagedChangesTree.Collapsed -= WorkingTreeTree_Collapsed;
        ChangedFilesTree.DoubleTapped -= ChangedFilesTree_DoubleTapped;
        UnstagedChangesList.RightTapped -= WorkingTreeChanges_RightTapped;
        StagedChangesList.RightTapped -= WorkingTreeChanges_RightTapped;
        UnstagedChangesList.DoubleTapped -= WorkingTreeChanges_DoubleTapped;
        StagedChangesList.DoubleTapped -= WorkingTreeChanges_DoubleTapped;
        // Window DI scope owns disposal of resolved feature models.
        _repositoryFilesViewModel.PropertyChanged -= RepositoryFilesViewModel_PropertyChanged;

        _maintenanceController.Shutdown();


    }
}
