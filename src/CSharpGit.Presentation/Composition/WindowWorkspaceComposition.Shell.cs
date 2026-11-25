using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

internal sealed partial class WindowWorkspaceComposition
{
    private readonly Workflows.RepositoryPresentationRefreshController _presentationRefresh;

    public bool IsRefreshRequired => _repositoryRefresh.IsRefreshRequired;

    internal Action CapturePendingMonitorDeliveryForDesktopCheck() =>
        _repositoryRefresh.CapturePendingMonitorDeliveryForDesktopCheck();

    private Task<ContentDialogResult> ShowPageDialogAsync(ContentDialog dialog) =>
        _pageDialogs.ShowAsync(dialog);

    private Task ShowInteractiveRebaseEditorAsync() =>
        _pageDialogs.ShowInteractiveRebaseEditorAsync();

    private ContentDialog GetPageDialog(string key) => _pageDialogs.GetDialog(key);

    internal Task OpenInitialRepositoryAsync() => _viewModel.OpenRepositoryAsyncForDesktopCheck();

    internal TreeView RepositoryTree => RepositoryExplorer.TreeControl;

    private Workflows.CommitLoadingOverlayController? _loadingOverlay;

    private void InitializeLoadingOverlays()
    {
        _loadingOverlay ??= new Workflows.CommitLoadingOverlayController(
            _viewModel, ChangedFilesTree, CompactDiffViewer, RootLayout);
        _loadingOverlay.Initialize();
    }

    private void DetachLoadingOverlays() => _loadingOverlay?.Shutdown();

    // Native resize notifications use the layout owner's callback directly.
    internal Action CommitDetailsLayoutRequested => _commitDetailsLayout.QueueLayout;

    private void SynchronizeRepositoryTree() => RepositoryExplorer.ReconcileRepository();

    private void SynchronizeWorktreePresentation() => RepositoryExplorer.ReconcileWorktrees();

    private void DetachRepositoryTreeStateTracking()
    {
        RepositoryExplorer.Shutdown();
        DetachLoadingOverlays();
    }
}
