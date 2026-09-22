using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryTreeCurrentBranchPresentationTests
{
    [Fact]
    public void CurrentBranchAndWorktreePreserveHierarchyTopology()
    {
        var root = new TestNode();
        var current = new TestNode();
        var sibling = new TestNode();
        root.Children.Add(current);
        root.Children.Add(sibling);

        TreeHierarchyGuideBuilder.Apply(
            new[] { root },
            node => node.Children,
            (node, segments) => node.HierarchyGuideSegments = segments);

        Assert.Equal(new[] { RepositoryTreeGuideSegmentKind.Branch }, current.HierarchyGuideSegments);
        Assert.Equal(new[] { RepositoryTreeGuideSegmentKind.Last }, sibling.HierarchyGuideSegments);
    }

    [Fact]
    public void CurrentPresentationSourceScopesAccentAndDoesNotRewriteGuides()
    {
        var node = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CSharpGit.Presentation",
            "ViewModels",
            "RepositoryTreeNode.cs"));

        Assert.Contains("IsCurrentLocalBranch => Kind == RepositoryTreeNodeKind.LocalBranch && IsCurrent", node, StringComparison.Ordinal);
        Assert.Contains("IsCurrentWorktree => Kind == RepositoryTreeNodeKind.Worktree && IsCurrent", node, StringComparison.Ordinal);
        Assert.Contains("CurrentBranchNameAccentVisibility => IsCurrentLocalBranch", node, StringComparison.Ordinal);
        Assert.Contains("CurrentWorktreeAccentVisibility => IsCurrentWorktree", node, StringComparison.Ordinal);
        Assert.Contains("if (HierarchyGuideSegments.SequenceEqual(segments)) return;", node, StringComparison.Ordinal);
        Assert.Contains("HierarchyGuideSegments = segments;", node, StringComparison.Ordinal);
        Assert.DoesNotContain("adjustedSegments", node, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryTreeUsesNameOnlyAccentAndKeepsStockTreeViewSelection()
    {
        var repositoryTree = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CSharpGit.Presentation",
            "Styles",
            "RepositoryTree.xaml"));

        Assert.Contains("BasedOn=\"{StaticResource DenseTreeItemStyle}\"", repositoryTree, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentBranchAccentVisibility", repositoryTree, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding CurrentWorktreeAccentVisibility}\"", repositoryTree, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(repositoryTree, "Visibility=\"{Binding CurrentBranchNameAccentVisibility}\""));
        Assert.DoesNotContain("TextForeground=\"{ThemeResource AccentFillColorDefaultBrush}\"", repositoryTree, StringComparison.Ordinal);
        Assert.Contains("<Border Height=\"1\"", repositoryTree, StringComparison.Ordinal);
        Assert.Contains("Background=\"{ThemeResource AccentFillColorDefaultBrush}\"", repositoryTree, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScaleTransform ScaleX=\"1.04\" ScaleY=\"1.08\" />", repositoryTree, StringComparison.Ordinal);
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

    private sealed class TestNode
    {
        public List<TestNode> Children { get; } = [];
        public IReadOnlyList<RepositoryTreeGuideSegmentKind> HierarchyGuideSegments { get; set; } = [];
    }
}
