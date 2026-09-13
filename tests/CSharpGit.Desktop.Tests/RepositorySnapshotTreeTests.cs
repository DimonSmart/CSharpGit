using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositorySnapshotTreeTests
{
    [Fact]
    public void BuildPreservesCaseSensitiveGitPathIdentityAndDeterministicOrder()
    {
        var entries = new[]
        {
            Entry("src/case.cs"),
            Entry("README.md"),
            Entry("src/Case.cs"),
            Entry("tests/Z.cs"),
            Entry("tests/a.cs")
        };

        var roots = RepositorySnapshotTreeNode.Build(entries);

        Assert.Equal(new[] { "src", "tests", "README.md" }, roots.Select(node => node.DisplayName));
        var src = Assert.Single(roots, node => node.DisplayName == "src");
        Assert.Equal(2, src.Children.Count);
        Assert.Contains(src.Children, node => node.Path == "src/Case.cs");
        Assert.Contains(src.Children, node => node.Path == "src/case.cs");
    }

    [Fact]
    public void NameSearchMatchesFullPathAndRetainsOnlyRequiredAncestors()
    {
        var entries = new[]
        {
            Entry("src/CSharpGit.Application/Abstractions/IHistoryService.cs"),
            Entry("src/CSharpGit.Git/GitReferenceHistoryService.cs"),
            Entry("tests/OtherTests.cs")
        };

        var roots = RepositorySnapshotTreeNode.Build(entries, "historyservice");

        var src = Assert.Single(roots);
        Assert.Equal("src", src.DisplayName);
        Assert.Equal(2, LeafPaths(src).Count);
        Assert.DoesNotContain(LeafPaths(src), path => path.StartsWith("tests/", StringComparison.Ordinal));
    }

    [Fact]
    public void SnapshotCacheIsLruBoundedAndTouchesHits()
    {
        var cache = new RepositorySnapshotCache(2);
        cache.Set("repo", "a", [Entry("a.txt")]);
        cache.Set("repo", "b", [Entry("b.txt")]);
        Assert.True(cache.TryGet("repo", "a", out _));

        cache.Set("repo", "c", [Entry("c.txt")]);

        Assert.True(cache.TryGet("repo", "a", out _));
        Assert.False(cache.TryGet("repo", "b", out _));
        Assert.True(cache.TryGet("repo", "c", out _));
        Assert.Equal(2, cache.Count);
    }

    private static RepositorySnapshotEntry Entry(string path) =>
        new(path, RepositorySnapshotEntryKind.File, new string('a', 40), "100644", "blob");

    private static IReadOnlyList<string> LeafPaths(RepositorySnapshotTreeNode root)
    {
        var result = new List<string>();
        AddLeaves(root, result);
        return result;
    }

    private static void AddLeaves(RepositorySnapshotTreeNode node, ICollection<string> result)
    {
        if (node.Entry is not null)
        {
            result.Add(node.Path);
            return;
        }
        foreach (var child in node.Children) AddLeaves(child, result);
    }
}
