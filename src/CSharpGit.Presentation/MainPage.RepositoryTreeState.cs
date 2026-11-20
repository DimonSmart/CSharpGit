using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly Workflows.RepositoryPresentationRefreshController _presentationRefresh;

    public bool IsRefreshRequired => _repositoryRefresh.IsRefreshRequired;

    internal Action CapturePendingMonitorDeliveryForDesktopCheck() =>
        _repositoryRefresh.CapturePendingMonitorDeliveryForDesktopCheck();

    private void QueueRepositoryPresentationRefresh(bool workingTreeChanged = false) =>
        _presentationRefresh.Queue(workingTreeChanged);

    // Phase-3 Git-reference menus still use the TreeView through this narrow UI adapter.
    internal TreeView RepositoryTree => RepositoryExplorer.TreeControl;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        HistoryView.InitializeScroll();
        InitializeLoadingOverlays();
    }

    private Workflows.CommitLoadingOverlayController? _loadingOverlay;

    private void InitializeLoadingOverlays()
    {
        _loadingOverlay ??= new Workflows.CommitLoadingOverlayController(
            _viewModel, ChangedFilesTree, CompactDiffViewer, RootLayout);
        _loadingOverlay.Initialize();
    }

    private void DetachLoadingOverlays() => _loadingOverlay?.Shutdown();

    // Temporary shell adapter for native resize callbacks until the XAML shell is extracted.
    internal void QueueCommitDetailsLayout() => _commitDetailsLayout.QueueLayout();

    private void SynchronizeRepositoryTree() => RepositoryExplorer.ReconcileRepository();

    private void SynchronizeWorktreePresentation() => RepositoryExplorer.ReconcileWorktrees();

    private void DetachRepositoryTreeStateTracking()
    {
        RepositoryExplorer.Shutdown();
        DetachLoadingOverlays();
    }
}
