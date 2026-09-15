namespace CSharpGit.Desktop.Tests;

public sealed class PrimaryWorktreeUiContractTests
{
    [Fact]
    public void RepositoryTreeKeepsPrimaryAndCurrentAsIndependentPresentationStates()
    {
        var root = FindRepositoryRoot();
        var worktreeInfo = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Domain", "WorktreeInfo.cs"));
        var parser = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitWorktreeParser.cs"));
        var descriptor = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeDescriptor.cs"));
        var node = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeNode.cs"));
        var repositoryTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "RepositoryTree.xaml"));
        var worktreesPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Worktrees.cs"));

        Assert.Contains("public bool IsPrimary { get; init; }", worktreeInfo, StringComparison.Ordinal);
        Assert.Contains("IsPrimary = result.Count == 0", parser, StringComparison.Ordinal);
        Assert.Contains(".OrderByDescending(worktree => worktree.IsPrimary)", descriptor, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderByDescending(worktree => worktree.IsCurrent)", descriptor, StringComparison.Ordinal);

        Assert.Contains("PrimaryWorktreeBadgeVisibility", node, StringComparison.Ordinal);
        Assert.Contains("Kind == RepositoryTreeNodeKind.Worktree && Value is WorktreeInfo { IsPrimary: true }", node, StringComparison.Ordinal);
        Assert.Contains("Text=\"PRIMARY\"", repositoryTree, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(repositoryTree, "Visibility=\"{Binding PrimaryWorktreeBadgeVisibility}\""));
        Assert.DoesNotContain("Value.IsPrimary", repositoryTree, StringComparison.Ordinal);
        Assert.DoesNotContain("BooleanToVisibilityConverter", repositoryTree, StringComparison.Ordinal);
        Assert.Contains("Background=\"{ThemeResource ControlFillColorDefaultBrush}\"", repositoryTree, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{ThemeResource ControlStrokeColorDefaultBrush}\"", repositoryTree, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"CURRENT\"", repositoryTree, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("!worktree.IsPrimary && !worktree.IsCurrent && !worktree.IsLocked", worktreesPage, StringComparison.Ordinal);
        Assert.Contains("worktree.IsPrimary || worktree.IsCurrent", worktreesPage, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
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
