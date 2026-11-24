namespace CSharpGit.Application.Tests;

public sealed class RepositoryFilesViewOwnershipContractTests
{
    [Fact]
    public void RepositoryFilesSurfaceOwnsItsInteractionsAndDisposesSubscriptions()
    {
        var root = FindRoot();
        var p = Path.Combine(root, "src", "CSharpGit.Presentation");
        var interactions = File.ReadAllText(Path.Combine(p, "Controls", "RepositoryFilesSurface.Interactions.cs"));
        var page = File.ReadAllText(Path.Combine(p, "Composition/WindowWorkspaceComposition.Files.cs"));
        var life = File.ReadAllText(Path.Combine(p, "Composition/WindowWorkspaceComposition.Lifetime.cs"));
        Assert.Contains("_repositoryFilesSurface!.Configure(", page, StringComparison.Ordinal);
        Assert.Contains("_repositoryFilesSurface?.ShutdownInteractions();", life, StringComparison.Ordinal);
        Assert.Contains("RepositoryFilesTree_SelectionChanged", interactions, StringComparison.Ordinal);
        Assert.Contains("RepositoryFilesTree_RightTapped", interactions, StringComparison.Ordinal);
        Assert.Contains("RepositoryContentResults_SelectionChanged", interactions, StringComparison.Ordinal);
        Assert.Contains("_repositoryFilesViewModel.PropertyChanged += RepositoryFilesViewModel_PropertyChanged", interactions, StringComparison.Ordinal);
        Assert.Contains("_repositoryFilesViewModel.PropertyChanged -= RepositoryFilesViewModel_PropertyChanged", interactions, StringComparison.Ordinal);
        Assert.DoesNotContain("RepositoryFilesTree_SelectionChanged(", page, StringComparison.Ordinal);
        Assert.DoesNotContain("_repositoryFilesSelectionRouter", page, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(selectedNode, _repositoryFilesViewModel.SelectedTreeNode)", interactions, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CSharpGit.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("CSharpGit root not found");
    }
}
