namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryFilesUiContractTests
{
    [Fact]
    public void CommitDetailsExposeDistinctChangesAndRepositoryFilesSurfaces()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "HistoryWorkspaceView.xaml"));
        var filesSurface = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryFiles.cs"));
        var historyCode = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "HistoryWorkspaceView.xaml.cs"));
        var changesSurface = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "HistoryWorkspaceView.Changes.cs"));

        Assert.Contains("Header=\"{Binding CommitDetails.SelectedDetailsTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ChangesTab\" Header=\"Changes\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"FilesTab\" Header=\"Changes\"", xaml, StringComparison.Ordinal);
        Assert.Contains("new PivotItem { Header = \"Files\", Name = \"FilesTab\" }", filesSurface, StringComparison.Ordinal);
        Assert.Contains("DetailsTabs.SelectedItem = ChangesTab", historyCode, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(DetailsTabs.SelectedItem, ChangesTabControl)", changesSurface, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryFilesFeatureKeepsGitBehindSemanticApplicationBoundary()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var filesSurface = File.ReadAllText(Path.Combine(presentation, "MainPage.RepositoryFiles.cs"));
        var actions = File.ReadAllText(Path.Combine(presentation, "Workflows", "RepositoryFilesActionController.cs"));
        var previewSurface = File.ReadAllText(Path.Combine(presentation, "Workflows", "RepositoryFilePreviewController.cs"));
        var feature = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "RepositoryFilesViewModel.cs"));

        Assert.DoesNotContain("IRepositorySnapshotService", filesSurface, StringComparison.Ordinal);
        Assert.DoesNotContain("_repositorySnapshotService", filesSurface, StringComparison.Ordinal);
        Assert.Contains("IRepositorySnapshotService", feature, StringComparison.Ordinal);
        Assert.Contains("_repositorySnapshotService.ReadTreeAsync", feature, StringComparison.Ordinal);
        Assert.Contains("_repositorySnapshotService.SearchContentAsync", feature, StringComparison.Ordinal);
        Assert.Contains("_repositorySnapshotService.ResolveFileVersionAsync", feature, StringComparison.Ordinal);
        Assert.Contains("_files.ResolveFileVersionWithinAcceptedOperationAsync", actions, StringComparison.Ordinal);
        Assert.Contains("_repositoryFilesViewModel.ResolveFileVersionWithinAcceptedOperationAsync", previewSurface, StringComparison.Ordinal);
        Assert.Contains("MaterializeAsync", actions, StringComparison.Ordinal);
        Assert.Contains("OpenEditorAsync", actions, StringComparison.Ordinal);
        Assert.Contains("MaterializeAsync", previewSurface, StringComparison.Ordinal);
        Assert.Contains("lease.CancellationToken", previewSurface, StringComparison.Ordinal);
        Assert.DoesNotContain("git ls-tree", feature, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git grep", feature, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git show", previewSurface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git cat-file", previewSurface, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryFilesFeatureIsLazyBoundedAndRejectsStaleResults()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var filesSurface = File.ReadAllText(Path.Combine(presentation, "MainPage.RepositoryFiles.cs"));
        var interactions = File.ReadAllText(Path.Combine(presentation, "Controls", "RepositoryFilesSurface.Interactions.cs"));
        var feature = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "RepositoryFilesViewModel.cs"));
        var model = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "RepositorySnapshotTreeNode.cs"));

        Assert.Contains("SetActiveAsync(true)", interactions, StringComparison.Ordinal);
        Assert.Contains("SetActiveAsync(false)", interactions, StringComparison.Ordinal);
        Assert.Contains("ActivateRepositoryFilesAsync", interactions, StringComparison.Ordinal);
        Assert.Contains("_snapshotCache.TryGet", feature, StringComparison.Ordinal);
        Assert.Contains("_snapshotLoadedSuccessfully", feature, StringComparison.Ordinal);
        Assert.Contains("CancelRequests", feature, StringComparison.Ordinal);
        Assert.Contains("CanPublishSnapshot", feature, StringComparison.Ordinal);
        Assert.Contains("CanPublishContent", feature, StringComparison.Ordinal);
        Assert.Contains("generation == Volatile.Read", feature, StringComparison.Ordinal);
        Assert.Contains("RepositorySnapshotCache(int capacity = 12)", model, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryFilesTreeUsesFeatureOwnedStableItemsSourceAndIncrementalPublication()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var filesSurface = File.ReadAllText(Path.Combine(presentation, "MainPage.RepositoryFiles.cs"));
        var feature = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "RepositoryFilesViewModel.cs"));
        var changesSurface = File.ReadAllText(Path.Combine(presentation, "Views", "HistoryWorkspaceView.Changes.cs"));

        Assert.Contains("public ObservableCollection<RepositorySnapshotTreeNode> TreeRoots { get; } = []", feature, StringComparison.Ordinal);
        var visual = File.ReadAllText(Path.Combine(presentation, "Controls", "RepositoryFilesSurface.cs"));
        Assert.Contains("ItemsSource = viewModel.TreeRoots", visual, StringComparison.Ordinal);
        Assert.Contains("RepositorySnapshotTreeSynchronizer.Reconcile(TreeRoots, _snapshot, query)", feature, StringComparison.Ordinal);
        Assert.DoesNotContain("_repositoryFilesTree.ItemsSource = null", filesSurface, StringComparison.Ordinal);
        var changedFilesTree = File.ReadAllText(Path.Combine(presentation, "Workflows", "CommitChangedFileTreeController.cs"));
        Assert.Contains("ChangedFileTreeSynchronizer.Reconcile(_roots, entries)", changedFilesTree, StringComparison.Ordinal);
        Assert.DoesNotContain("_changedFileTreeRoots.Clear()", changesSurface, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentSearchIsExplicitAndNameSearchStaysLocal()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var filesSurface = File.ReadAllText(Path.Combine(presentation, "MainPage.RepositoryFiles.cs"));
        var feature = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "RepositoryFilesViewModel.cs"));
        var model = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "RepositorySnapshotTreeNode.cs"));

        Assert.Contains("args.Key != VirtualKey.Enter", interactions, StringComparison.Ordinal);
        Assert.Contains("_repositoryFilesViewModel.SearchContentAsync", interactions, StringComparison.Ordinal);
        Assert.Contains("RepositorySnapshotTreeSynchronizer.Reconcile(TreeRoots, _snapshot, query)", feature, StringComparison.Ordinal);
        Assert.Contains("entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase)", model, StringComparison.Ordinal);
        Assert.Contains("entry.Kind == RepositorySnapshotEntryKind.File", feature, StringComparison.Ordinal);
    }

    [Fact]
    public void FilesTreeReusesHierarchyVisualsAndPreviewIsASeparatePipeline()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var filesSurface = File.ReadAllText(Path.Combine(presentation, "MainPage.RepositoryFiles.cs"));
        var previewSurface = File.ReadAllText(Path.Combine(presentation, "Workflows", "RepositoryFilePreviewController.cs"));
        var treeStyles = File.ReadAllText(Path.Combine(presentation, "Styles", "RepositoryTree.xaml"));
        var previewHost = File.ReadAllText(Path.Combine(presentation, "Controls", "FilePreviewHost.cs"));
        var previewService = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "Previewing", "FilePreviewService.cs"));

        Assert.Contains("RepositoryFilesTreeItemTemplate", treeStyles, StringComparison.Ordinal);
        Assert.Contains("controls:RepositoryTreeGuides", treeStyles, StringComparison.Ordinal);
        var visual = File.ReadAllText(Path.Combine(presentation, "Controls", "RepositoryFilesSurface.cs"));
        Assert.Contains("new RepositoryFilesSurface(_repositoryFilesViewModel)", filesSurface, StringComparison.Ordinal);
        Assert.Contains("new GridSplitter", visual, StringComparison.Ordinal);
        Assert.Contains("new FilePreviewHost()", visual, StringComparison.Ordinal);
        Assert.Contains("_previewHost()", previewSurface, StringComparison.Ordinal);
        Assert.Contains("() => _repositoryFilesSurface?.PreviewHost", filesSurface, StringComparison.Ordinal);
        Assert.Contains("FilePreviewService", previewService, StringComparison.Ordinal);
        Assert.Contains("TextPreviewContent", previewHost, StringComparison.Ordinal);
        Assert.Contains("ImagePreviewContent", previewHost, StringComparison.Ordinal);
        Assert.Contains("BinaryPreviewContent", previewHost, StringComparison.Ordinal);
        Assert.Contains("RepositoryFilesTree_SelectionChanged", interactions, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(presentation, "MainPage.RepositoryFilePreview.cs")));
        Assert.Contains("RepositoryContentResults_SelectionChanged", interactions, StringComparison.Ordinal);
        Assert.Contains("SnapshotMatchesSelection", interactions, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryFilesSelectionUsesEventDeltaAndSuppressesReverseFeedback()
    {
        var root = FindRepositoryRoot();
        var files = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "RepositoryFilesSurface.Interactions.cs"));
        Assert.Contains("args.AddedItems.Cast<object?>()", files, StringComparison.Ordinal);
        Assert.Contains("args.RemovedItems.Cast<object?>()", files, StringComparison.Ordinal);
        Assert.Contains("_repositoryFilesSelectionRouter.Route(", files, StringComparison.Ordinal);
        Assert.Contains("_repositoryFilesHandlingUserSelection", files, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectTreeNode(sender.SelectedItem", files, StringComparison.Ordinal);
        Assert.Contains("ActivateRepositoryFilesAsync", files, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
