namespace CSharpGit.Application.Tests;

public sealed class WorkingTreeToolbarNavigationContractTests
{
    [Fact]
    public void WorkingTreeNavigationLivesOnMainToolbarInsteadOfRepositoryTree()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var treeNode = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeNode.cs"));

        Assert.Contains("x:Name=\"CommitNavigationButton\"", xaml);
        Assert.Contains("x:Name=\"CommitNavigationText\"", xaml);
        Assert.Contains("Click=\"CommitNavigation_Click\"", xaml);
        Assert.DoesNotContain("Content=\"Back to history\"", xaml);
        Assert.DoesNotContain("ShowHistory_Click", xaml + page);

        Assert.Contains("$\"Commit ({_viewModel.Changes.Count})\"", page);
        Assert.Contains("\"Back to history\"", page);
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
