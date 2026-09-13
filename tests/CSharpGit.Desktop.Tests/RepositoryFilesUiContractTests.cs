namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryFilesUiContractTests
{
    [Fact]
    public void CommitDetailsExposeDistinctChangesAndRepositoryFilesSurfaces()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var filesSurface = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryFiles.cs"));
        var changesSurface = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs"));

        Assert.Contains("Header=\"Commit\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Changes\"", xaml, StringComparison.Ordinal);
        Assert.Contains("new PivotItem { Header = \"Files\", Name = \"FilesTab\" }", filesSurface, StringComparison.Ordinal);
        Assert.Contains("_changesTab.Name = \"ChangesTab\"", filesSurface, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(DetailsTabs.SelectedItem, ChangesTabControl)", changesSurface, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryFilesSurfaceKeepsGitBehindSemanticApplicationBoundary()
    {
        var root = FindRepositoryRoot();
        var filesSurface = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryFiles.cs"));

        Assert.Contains("IRepositorySnapshotService", filesSurface, StringComparison.Ordinal);
        Assert.Contains("ResolveFileVersionAsync", filesSurface, StringComparison.Ordinal);
        Assert.Contains("MaterializeAsync", filesSurface, StringComparison.Ordinal);
        Assert.Contains("OpenEditorAsync", filesSurface, StringComparison.Ordinal);
        Assert.DoesNotContain("git ls-tree", filesSurface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git grep", filesSurface, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResolveCommitAsync", filesSurface, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryFilesSurfaceIsLazyBoundedAndRejectsStaleResults()
    {
        var root = FindRepositoryRoot();
        var filesSurface = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryFiles.cs"));
        var model = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositorySnapshotTreeNode.cs"));

        Assert.Contains("IsRepositoryFilesActive", filesSurface, StringComparison.Ordinal);
        Assert.Contains("_repositorySnapshotCache.TryGet", filesSurface, StringComparison.Ordinal);
        Assert.Contains("CancelRepositoryFilesRequests", filesSurface, StringComparison.Ordinal);
        Assert.Contains("CanPublishRepositoryFiles", filesSurface, StringComparison.Ordinal);
        Assert.Contains("CanPublishRepositoryContent", filesSurface, StringComparison.Ordinal);
        Assert.Contains("generation == Volatile.Read", filesSurface, StringComparison.Ordinal);
        Assert.Contains("RepositorySnapshotCache(int capacity = 12)", model, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentSearchIsExplicitAndNameSearchStaysLocal()
    {
        var root = FindRepositoryRoot();
        var filesSurface = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryFiles.cs"));
        var model = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositorySnapshotTreeNode.cs"));

        Assert.Contains("args.Key != VirtualKey.Enter || _repositoryFilesSearchModeName != \"Content\"", filesSurface, StringComparison.Ordinal);
        Assert.Contains("RepositorySnapshotTreeNode.Build(_repositorySnapshot, query)", filesSurface, StringComparison.Ordinal);
        Assert.Contains("entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase)", model, StringComparison.Ordinal);
        Assert.Contains("entry.Kind == RepositorySnapshotEntryKind.File", filesSurface, StringComparison.Ordinal);
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
