using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class TreeHierarchyGuideBuilderTests
{
    [Fact]
    public void ApplyBuildsSegmentsAcrossNestedSiblingGroups()
    {
        var firstFolder = new Node("first", [new Node("a"), new Node("b")]);
        var lastFolder = new Node("last", [new Node("c"), new Node("d")]);
        var root = new Node("root", [firstFolder, new Node("middle"), lastFolder]);

        Apply([root]);

        Assert.Empty(root.Segments);
        Assert.Equal([RepositoryTreeGuideSegmentKind.Branch], firstFolder.Segments);
        Assert.Equal([RepositoryTreeGuideSegmentKind.Branch], root.Children[1].Segments);
        Assert.Equal([RepositoryTreeGuideSegmentKind.Last], lastFolder.Segments);
        Assert.Equal(
            [RepositoryTreeGuideSegmentKind.Continue, RepositoryTreeGuideSegmentKind.Branch],
            firstFolder.Children[0].Segments);
        Assert.Equal(
            [RepositoryTreeGuideSegmentKind.Continue, RepositoryTreeGuideSegmentKind.Last],
            firstFolder.Children[1].Segments);
        Assert.Equal(
            [RepositoryTreeGuideSegmentKind.Empty, RepositoryTreeGuideSegmentKind.Branch],
            lastFolder.Children[0].Segments);
        Assert.Equal(
            [RepositoryTreeGuideSegmentKind.Empty, RepositoryTreeGuideSegmentKind.Last],
            lastFolder.Children[1].Segments);
    }

    [Fact]
    public void ApplyUsesVisibleSiblingSetAfterFiltering()
    {
        var visibleRoot = new Node("root", [new Node("kept")]);

        Apply([visibleRoot]);

        Assert.Equal([RepositoryTreeGuideSegmentKind.Last], visibleRoot.Children[0].Segments);
    }

    private static void Apply(IReadOnlyList<Node> roots) =>
        TreeHierarchyGuideBuilder.Apply(
            roots,
            node => node.Children,
            (node, segments) => node.Segments = segments);

    private sealed class Node(string name, IReadOnlyList<Node>? children = null)
    {
        internal string Name { get; } = name;
        internal IReadOnlyList<Node> Children { get; } = children ?? [];
        internal IReadOnlyList<RepositoryTreeGuideSegmentKind> Segments { get; set; } = [];
    }
}
