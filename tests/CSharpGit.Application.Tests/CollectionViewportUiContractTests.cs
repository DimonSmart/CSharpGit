namespace CSharpGit.Application.Tests;

public sealed class CollectionViewportUiContractTests
{
    [Fact]
    public void AllChangingListSurfacesUseSharedViewportSynchronization()
    {
        var root = FindRepositoryRoot();
        var changes = Read(root, "MainPage.Changes.cs");
        var changedTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Workflows", "CommitChangedFileTreeController.cs"));
        var workingTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "WorkingTreeView.Selection.cs"));
        var page = Read(root, "MainPage.xaml.cs");
        var lifecycle = Read(root, "MainPage.Lifecycle.cs");
        var history = File.ReadAllText(Path.Combine(root,
            "src", "CSharpGit.Presentation", "Views", "HistoryView.xaml.cs"));
        var explorer = File.ReadAllText(Path.Combine(root,
            "src", "CSharpGit.Presentation", "Views", "RepositoryExplorerView.xaml.cs"));
        var controller = File.ReadAllText(Path.Combine(root,
            "src", "CSharpGit.Presentation", "Controls", "CollectionViewportController.cs"));
        var xaml = File.ReadAllText(Path.Combine(root,
            "src", "CSharpGit.Presentation", "Views", "HistoryWorkspaceView.xaml"));

        Assert.Contains("_viewport?.Observe", changedTree);
        Assert.Contains("CountVisibleChangedFileNodes", changedTree);
        Assert.Contains("ChangedFilesTree_Collapsed", changedTree);
        Assert.Contains("SelectedDetailsViewportKey()", changes);
        Assert.Contains("_tree.SelectedItem = node", changedTree);
        Assert.Contains("_unstagedViewport?.Observe", workingTree);
        Assert.Contains("_stagedViewport?.Observe", workingTree);
        Assert.Contains("CountVisibleWorkingTreeNodes", workingTree);
        Assert.Contains("WorkingTreeTree_Collapsed", workingTree);
        Assert.Contains("tree.ScrollIntoView(target)", workingTree);
        Assert.Contains("SynchronizeDetailsViewport()", page);
        Assert.Contains("x:Name=\"StashDetailsScroller\"", xaml);
        Assert.Contains("_commitSummaryViewport?.Dispose()", lifecycle);
        Assert.Contains("_stashSummaryViewport?.Dispose()", lifecycle);
        Assert.Contains("_viewport?.Dispose()", changedTree);
        Assert.Contains("_changesTree?.Shutdown()", lifecycle);
        Assert.Contains("HistoryRows_ViewportCollectionChanged", history);
        Assert.Contains("_historyViewport?.CancelPendingAdjustment()", history);
        Assert.Contains("ObserveTreeViewport()", explorer);
        Assert.Contains("DiffViewportResetGate", controller);
        var policy = File.ReadAllText(Path.Combine(root,
            "src", "CSharpGit.Presentation.Core", "Controls", "CollectionViewportState.cs"));
        Assert.Contains("ClampToContent", policy);
        Assert.Contains("DispatcherQueuePriority.Low", controller);
        Assert.Contains("CancelPendingAdjustment()", controller);
        Assert.Contains("_surface.UpdateLayout()", controller);
    }

    private static string Read(string root, string name) =>
        File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", name));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ??
            throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
