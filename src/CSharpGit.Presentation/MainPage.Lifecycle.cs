using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private int _shutdownStarted;

    public bool RequiresCloseConfirmation => _viewModel.HasUnappliedCommitMessage;

    private void MainPage_Loaded(object sender, RoutedEventArgs args)
    {
        InitializeCommitDetailsSurface();
        InitializeConfirmationDialogs();
        InitializeBranchRename();
        InitializeBranchDragDrop();
    }

    public void BeginShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;

        Loaded -= RunDesktopCheckWhenRequested;
        DetachRepositoryTreeStateTracking();
        ShutdownGitConsole();
        ShutdownRecentRepositories();
        ShutdownRepositoryChangeMonitoring();
        ShutdownWorktreeSupport();
        _settingsWindowController.Shutdown();

        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.PropertyChanged -= ChangesViewModel_PropertyChanged;
        _viewModel.PropertyChanged -= ConfirmationDialogs_PropertyChanged;
        _viewModel.PropertyChanged -= FileOpeningViewModel_PropertyChanged;
        _viewModel.PropertyChanged -= RepositoryFilesViewModel_PropertyChanged;
        _viewModel.PropertyChanged -= RepositoryMaintenanceViewModel_PropertyChanged;
        _viewModel.History.CollectionChanged -= MainHistory_CollectionChanged;
        _viewModel.Changes.CollectionChanged -= WorkingTreeChangesCollectionChanged;

        _referenceHistoryCts?.Cancel();
        _referenceHistoryCts?.Dispose();
        _referenceHistoryCts = null;
        _viewModel.Dispose();
    }
}
