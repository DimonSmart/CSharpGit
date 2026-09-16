using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class PathTreeBuilderTests
{
    [Fact]
    public void BuildsFoldersBeforeFilesWithDeterministicCaseSensitiveLeaves()
    {
        var roots = PathTreeBuilder.Build(
        [
            new Item("README.md"),
            new Item("src/case.cs"),
            new Item("src/Case.cs"),
            new Item("src/Application/a.cs"),
            new Item("src/Domain/c.cs")
        ],
        item => item.Path);

        Assert.Equal(new[] { "src", "README.md" }, roots.Select(node => node.DisplayName));
        var src = Assert.Single(roots, node => node.DisplayName == "src");
        Assert.Equal(new[] { "Application", "Domain", "Case.cs", "case.cs" }, src.Children.Select(node => node.DisplayName));
        Assert.Contains(src.Children, node => node.Path == "src/Case.cs");
        Assert.Contains(src.Children, node => node.Path == "src/case.cs");
    }

    [Fact]
    public void CollapsesSingleFolderChainsAndKeepsLeafPath()
    {
        var roots = PathTreeBuilder.Build(
            [new Item("src/A/B/C/file.cs")],
            item => item.Path,
            new PathTreeBuildOptions(CollapseSingleChildFolderChains: true));

        var folder = Assert.Single(roots);
        Assert.Equal("src/A/B/C", folder.DisplayName);
        Assert.Equal("src/A/B/C", folder.Path);
        Assert.Equal("src/A/B/C/file.cs", Assert.Single(folder.Children).Path);
    }

    [Fact]
    public void DeduplicatesExactPathsButNotCaseDistinctPaths()
    {
        var roots = PathTreeBuilder.Build(
            [new Item("Case.cs"), new Item("Case.cs"), new Item("case.cs")],
            item => item.Path);

        Assert.Equal(2, roots.Count);
        Assert.Equal(new[] { "Case.cs", "case.cs" }, roots.Select(node => node.Path));
    }

    [Fact]
    public void HandlesEmptyInput()
    {
        var roots = PathTreeBuilder.Build(Array.Empty<Item>(), item => item.Path);

        Assert.Empty(roots);
    }

    private sealed record Item(string Path);
}
