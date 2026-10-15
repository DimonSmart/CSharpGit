using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private int _shutdownStarted;
    private int _shutdownFinalized;

    private bool IsShuttingDown =>
        Volatile.Read(ref _shutdownStarted) != 0;

    public bool RequiresCloseConfirmation => _viewModel.CommitCreation.HasUnappliedCommitMessage;

    // Only operations with Git or filesystem side effects make shutdown unsafe.
    public bool IsUnsafeToClose =>
        _viewModel.IsMutating
        || _viewModel.IsChangingRepository
        || IsHistoryRewriteInProgress
        || IsRepositoryMaintenanceInProgress
        || IsApplicationUpdateInProgress
        || _cloneRepositoryViewModel.IsBusy
        || _createRepositoryViewModel.IsBusy;

    private void MainPage_Loaded(object sender, RoutedEventArgs args)
    {
        InitializeCommitDetailsSurface();
        InitializeConfirmationDialogs();
        InitializeBranchRename();
        InitializeBranchDragDrop();
        InitializeRepositorySwitching();
    }

    // Close admission is decided by App; this phase only shuts down UI requests.
    public Task PrepareForShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return Task.CompletedTask;
        _pageDialogLifetime.Cancel();
        return StopHistoryPerformanceCaptureOnShutdown();
    }

    public void BeginShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownFinalized, 1) != 0) return;
        Volatile.Write(ref _shutdownStarted, 1);
        _pageDialogLifetime.Cancel();
        _cloneRepositoryViewModel.Cancel();
        Loaded -= RunDesktopCheckWhenRequested;
        _viewModel.WorkingTree.Changes.CollectionChanged -= RepositoryPresentationChanges_CollectionChanged;
        _viewModel.Branches.LocalBranches.CollectionChanged -= RepositoryPresentationLocalBranches_CollectionChanged;
        _viewModel.Branches.RemoteBranches.CollectionChanged -= RepositoryPresentationRemoteBranches_CollectionChanged;
        _viewModel.RepositorySync.Remotes.CollectionChanged -= RepositoryPresentationRemotes_CollectionChanged;
        _viewModel.Tags.Items.CollectionChanged -= RepositoryPresentationTags_CollectionChanged;
        _viewModel.Stashes.Items.CollectionChanged -= RepositoryPresentationStashes_CollectionChanged;
        DetachRepositoryTreeStateTracking();
        ShutdownGitConsole();
        ShutdownRecentRepositories();
        ShutdownRepositoryChangeMonitoring();
        ShutdownWorktreeSupport();
        _settingsWindowController.Shutdown();

        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.Stashes.PropertyChanged -= StashesViewModel_PropertyChanged;
        _viewModel.CommitDetails.PropertyChanged -= CommitDetailsViewModel_PropertyChanged;
        _viewModel.History.PropertyChanged -= HistoryViewModel_PropertyChanged;
        _viewModel.History.CommitLookupCompleted -= History_CommitLookupCompleted;
        _viewModel.CommitDetails.PropertyChanged -= ChangesViewModel_PropertyChanged;
        _viewModel.CommitCreation.PropertyChanged -= CommitCreationViewModel_PropertyChanged;
        _viewModel.PropertyChanged -= FileOpeningViewModel_PropertyChanged;
        _viewModel.CommitDetails.PropertyChanged -= FileOpeningViewModel_PropertyChanged;
        _viewModel.History.PropertyChanged -= FileOpeningViewModel_PropertyChanged;
        _viewModel.WorkingTree.PropertyChanged -= WorkingTreeViewModel_PropertyChanged;
        _viewModel.WorkingTree.PropertyChanged -= WorkingTreeFileOpening_PropertyChanged;
        _fileActions.PropertyChanged -= FileActions_PropertyChanged;
        ChangedFilesTree.RightTapped -= ChangedFilesTree_RightTapped;
        ChangedFilesTree.DoubleTapped -= ChangedFilesTree_DoubleTapped;
        UnstagedChangesList.RightTapped -= WorkingTreeChanges_RightTapped;
        StagedChangesList.RightTapped -= WorkingTreeChanges_RightTapped;
        UnstagedChangesList.DoubleTapped -= WorkingTreeChanges_DoubleTapped;
        StagedChangesList.DoubleTapped -= WorkingTreeChanges_DoubleTapped;
        _fileActions.Dispose();
        _viewModel.WorkingTree.PropertyChanged -= WorkingTreeConfirmation_PropertyChanged;
        _repositoryFilesViewModel.PropertyChanged -= RepositoryFilesViewModel_PropertyChanged;
        _repositoryFilesViewModel.Dispose();
        _viewModel.PropertyChanged -= RepositoryMaintenanceViewModel_PropertyChanged;
        _repositoryMaintenance.PropertyChanged -= RepositoryMaintenance_PropertyChanged;
        _repositoryMaintenance.Dispose();
        _viewModel.History.Rows.CollectionChanged -= HistoryRows_CollectionChanged;
        _viewModel.Dispose();
    }
}
