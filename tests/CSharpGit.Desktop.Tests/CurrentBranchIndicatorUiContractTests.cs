namespace CSharpGit.Desktop.Tests;

public sealed class CurrentBranchIndicatorUiContractTests
{
    [Fact]
    public void RepositoryTreeUsesNameAccentForCurrentBranchWithoutSelectionCoupling()
    {
        var root = FindRepositoryRoot();
        var node = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeNode.cs"));
        var repositoryTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "RepositoryTree.xaml"));
        var mainPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));

        Assert.Contains("public string DisplayName => Kind == RepositoryTreeNodeKind.Worktree", node, StringComparison.Ordinal);
        Assert.Contains("WorktreePresentation.GetPrimaryLabel(worktree)", node, StringComparison.Ordinal);
        Assert.Contains(": Name;", node, StringComparison.Ordinal);
        Assert.Contains("IsCurrentLocalBranch => Kind == RepositoryTreeNodeKind.LocalBranch && IsCurrent", node, StringComparison.Ordinal);
        Assert.Contains("IsCurrentWorktree => Kind == RepositoryTreeNodeKind.Worktree && IsCurrent", node, StringComparison.Ordinal);
        Assert.Contains("NameFontWeight => IsCurrentLocalBranch || IsCurrentWorktree", node, StringComparison.Ordinal);
        Assert.Contains("FontWeights.Bold", node, StringComparison.Ordinal);
        Assert.Contains("CurrentBranchNameAccentVisibility => IsCurrentLocalBranch", node, StringComparison.Ordinal);
        Assert.Contains("CurrentWorktreeAccentVisibility => IsCurrentWorktree", node, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentLocalBranchIconVisibility", node, StringComparison.Ordinal);
        Assert.Contains("if (HierarchyGuideSegments.SequenceEqual(segments)) return;", node, StringComparison.Ordinal);
        Assert.Contains("HierarchyGuideSegments = segments;", node, StringComparison.Ordinal);
        Assert.DoesNotContain("adjustedSegments", node, StringComparison.Ordinal);
        Assert.DoesNotContain("✓", node, StringComparison.Ordinal);

        Assert.Contains("TextFontWeight=\"{Binding NameFontWeight}\"", repositoryTree, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(repositoryTree, "Visibility=\"{Binding CurrentBranchNameAccentVisibility}\""));
        Assert.Contains("Visibility=\"{Binding CurrentWorktreeAccentVisibility}\"", repositoryTree, StringComparison.Ordinal);
        Assert.DoesNotContain("TextForeground=\"{ThemeResource AccentFillColorDefaultBrush}\"", repositoryTree, StringComparison.Ordinal);
        Assert.Contains("<Border Height=\"1\"", repositoryTree, StringComparison.Ordinal);
        Assert.Contains("Background=\"{ThemeResource AccentFillColorDefaultBrush}\"", repositoryTree, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScaleTransform ScaleX=\"1.04\" ScaleY=\"1.08\" />", repositoryTree, StringComparison.Ordinal);
        Assert.DoesNotContain("Visibility=\"{Binding CurrentLocalBranchIconVisibility}\"", repositoryTree, StringComparison.Ordinal);

        Assert.Contains("SelectionMode=\"Single\"", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSelected=\"{Binding IsCurrent", repositoryTree, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem=\"{Binding IsCurrent", mainPage, StringComparison.Ordinal);
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
