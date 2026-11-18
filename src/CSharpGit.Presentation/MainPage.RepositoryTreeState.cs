using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    // Phase-3 Git-reference menus still use the TreeView through this narrow UI adapter.
    internal TreeView RepositoryTree => RepositoryExplorer.TreeControl;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        InitializeHistoryInfiniteScroll();
        InitializeLoadingOverlays();
    }

    private void SynchronizeRepositoryTree() => RepositoryExplorer.ReconcileRepository();

    private void SynchronizeWorktreePresentation() => RepositoryExplorer.ReconcileWorktrees();

    private void DetachRepositoryTreeStateTracking()
    {
        RepositoryExplorer.Shutdown();
        DetachLoadingOverlays();
    }
}
