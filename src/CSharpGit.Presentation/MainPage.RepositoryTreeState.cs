using Microsoft.UI.Xaml.Controls;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly Workflows.RepositoryPresentationRefreshController _presentationRefresh;

    public bool IsRefreshRequired => _repositoryRefresh.IsRefreshRequired;

    internal Action CapturePendingMonitorDeliveryForDesktopCheck() =>
        _repositoryRefresh.CapturePendingMonitorDeliveryForDesktopCheck();

    private void QueueRepositoryPresentationRefresh(bool workingTreeChanged = false) =>
        _presentationRefresh.Queue(workingTreeChanged);

    private ContentDialog InteractiveRebaseDialog => _pageDialogs.GetDialog("InteractiveRebaseDialog");

    private Task<ContentDialogResult> ShowPageDialogAsync(ContentDialog dialog) =>
        _pageDialogs.ShowAsync(dialog);

    private Task ShowInteractiveRebaseEditorAsync() =>
        _pageDialogs.ShowInteractiveRebaseEditorAsync();

    private ContentDialog GetPageDialog(string key) => _pageDialogs.GetDialog(key);

    private static T? FindNamedDescendant<T>(Microsoft.UI.Xaml.DependencyObject root, string name)
        where T : Microsoft.UI.Xaml.FrameworkElement =>
        Dialogs.PageDialogController.FindNamedDescendant<T>(root, name);

    private bool CanRenameBranch() => _branchRename.CanRenameBranch();
    private Task RenameBranchAsync(GitBranch branch) => _branchRename.RenameAsync(branch);
    private Task ExecuteBranchRenameAsync(GitBranch branch, string name) =>
        _branchRename.ExecuteAsync(branch, name);

    private void CreateStash_Click(object sender, RoutedEventArgs args) =>
        RunPageUiOperation(_stashDialogs.ShowCreateAsync, "Create stash");

    private Task ShowCreateStashDialogAsync() => _stashDialogs.ShowCreateAsync();

    private Task ShowCreateSelectedStashDialogAsync(IReadOnlyCollection<WorkingTreeChange> changes) =>
        _stashDialogs.ShowCreateSelectedAsync(changes);

    private void DropSelectedStash_Click(object sender, RoutedEventArgs args)
    {
        if (_viewModel.Stashes.SelectedStash is { } stash)
            RunPageUiOperation(() => _stashDialogs.ConfirmDropAsync(stash), "Drop stash");
    }

    private Task ConfirmDropStashAsync(GitStash stash) => _stashDialogs.ConfirmDropAsync(stash);

    internal Task OpenInitialRepositoryAsync() => _viewModel.OpenRepositoryAsyncForDesktopCheck();

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
