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

    private sealed class TestNode
    {
        public List<TestNode> Children { get; } = [];
        public IReadOnlyList<RepositoryTreeGuideSegmentKind> HierarchyGuideSegments { get; set; } = [];
    }
}
