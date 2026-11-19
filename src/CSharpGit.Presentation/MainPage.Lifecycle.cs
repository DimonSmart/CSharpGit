using Microsoft.UI.Xaml;
using CSharpGit.Presentation.Views;

namespace CSharpGit.Presentation;

public sealed partial class MainPage : Windowing.IWindowContentLifetime
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
        InitializeCommitDetailsSurface();
        InitializeConfirmationDialogs();
        InitializeBranchRename();
        InitializeBranchDragDrop();
        RepositoryToolbar.Configure(HandleRepositoryToolbarAction);
        RepositoryHeader.Configure(_viewModel, _recentRepositorySettings, _desktopShellService,
            new RepositorySelectorActions(
                TrySwitchRepositoryAsync,
                OpenRepositoryPickerAsync,
                ShowCloneRepositoryAsync,
                ShowCreateRepositoryAsync,
                TryCloseRepositoryAsync,
                path => OpenFolderInDesktopShellAsync(path, "Could not open repository folder"),
                CopyTextAsync,
                ShowErrorAsync));
    }

    // Close admission is decided by WindowCloseOrchestrator; the page only shuts down UI requests.
    Task Windowing.IWindowContentLifetime.PrepareForShutdownAsync(CancellationToken cancellationToken) =>
        PrepareForShutdown();

    void Windowing.IWindowContentLifetime.CompleteShutdown() => BeginShutdown();

    public Task PrepareForShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return Task.CompletedTask;
        _pageDialogLifetime.Cancel();
        ShutdownRepositoryChangeMonitoring();
        // Stop page-owned cancelable previews before waiting for diagnostics.
        // Do not cancel Git mutations: close admission already protects them.
        CancelRepositoryFilePreview();
        CancelCommitImageDiff(clearSurface: false);
        CancelWorkingTreeImageDiff();
        _cloneRepositoryViewModel.Cancel();
        return HistoryView.StopHistoryPerformanceCaptureOnShutdown();
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
        WorkingTreeCommitEditor.StashRequested -= CreateStash_Click;
        HistoryView.Shutdown();
        ShutdownGitConsole();
        StartScreenHost.ShutdownRecentRepositories();
        RepositoryHeader.Shutdown();
        RepositoryToolbar.Shutdown();
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
        // Window DI scope owns disposal of resolved feature models.
        _viewModel.WorkingTree.PropertyChanged -= WorkingTreeConfirmation_PropertyChanged;
        _repositoryFilesViewModel.PropertyChanged -= RepositoryFilesViewModel_PropertyChanged;

        _viewModel.PropertyChanged -= RepositoryMaintenanceViewModel_PropertyChanged;
        _repositoryMaintenance.PropertyChanged -= RepositoryMaintenance_PropertyChanged;


    }
}
