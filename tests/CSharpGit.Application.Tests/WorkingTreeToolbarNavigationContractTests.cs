namespace CSharpGit.Application.Tests;

public sealed class WorkingTreeToolbarNavigationContractTests
{
    [Fact]
    public void WorkingTreeNavigationLivesOnMainToolbarInsteadOfRepositoryTree()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "RepositoryToolbarView.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Composition/WindowWorkspaceComposition.cs"));
        var treeNode = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeNode.cs"));

        Assert.Contains("x:Name=\"CommitNavigationButton\"", xaml);
        Assert.Contains("x:Name=\"CommitNavigationText\"", xaml);
        Assert.Contains("Click=\"CommitNavigation_Click\"", xaml);
        Assert.DoesNotContain("Content=\"Back to history\"", xaml);
        Assert.DoesNotContain("ShowHistory_Click", xaml + page);

        var navigation = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Workflows", "WorkspaceNavigationController.cs"));
        var toolbar = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "RepositoryToolbarView.xaml.cs"));
        Assert.Contains("_toolbar.SetCommitNavigationText(", navigation);
        Assert.Contains("\"Back to history\"", toolbar);
        Assert.Contains("$\"Commit ({changes})\"", toolbar);
        Assert.DoesNotContain("RepositoryTreeNodeKind.WorkingTree", page);
        Assert.DoesNotContain("WorkingTree,", treeNode);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
