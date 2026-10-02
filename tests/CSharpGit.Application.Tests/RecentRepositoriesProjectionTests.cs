using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class RecentRepositoriesProjectionTests
{
    [Theory]
    [InlineData("alpha", "Alpha Repository")]
    [InlineData("work/project", "Path Match")]
    [InlineData("FEATURE/LOGIN", "Branch Match")]
    public void SearchMatchesNamePathAndBranchCaseInsensitively(string query, string expectedDisplayName)
    {
        var repositories = new[]
        {
            Repository("Alpha Repository", "/repos/one", "main", false, null, 3),
            Repository("Path Match", "/work/project/two", "develop", false, null, 2),
            Repository("Branch Match", "/repos/three", "feature/login", true, 0, 1)
        };

        var projection = RecentRepositoriesProjectionBuilder.Build(repositories, query);
        var result = projection.Pinned.Concat(projection.Recent).ToArray();

        Assert.Single(result);
        Assert.Equal(expectedDisplayName, result[0].DisplayName);
    }

    [Fact]
    public void WhitespaceSearchIsEquivalentToEmptySearch()
    {
        var repositories = new[]
        {
            Repository("Pinned", "/repos/pinned", "main", true, 0, 1),
            Repository("Recent", "/repos/recent", "main", false, null, 2)
        };

        var empty = RecentRepositoriesProjectionBuilder.Build(repositories, string.Empty);
        var whitespace = RecentRepositoriesProjectionBuilder.Build(repositories, "   ");

        Assert.Equal(empty.Pinned, whitespace.Pinned);
        Assert.Equal(empty.Recent, whitespace.Recent);
    }

    [Fact]
    public void PinnedOrderAndRecentLastOpenedOrderAreIndependent()
    {
        var repositories = new[]
        {
            Repository("Pinned later opened", "/repos/p1", null, true, 1, 10),
            Repository("Pinned first", "/repos/p0", null, true, 0, 1),
            Repository("Recent old", "/repos/r1", null, false, null, 2),
            Repository("Recent new", "/repos/r2", null, false, null, 5)
        };

        var projection = RecentRepositoriesProjectionBuilder.Build(repositories, null);

        Assert.Equal(new[] { "Pinned first", "Pinned later opened" }, projection.Pinned.Select(x => x.DisplayName));
        Assert.Equal(new[] { "Recent new", "Recent old" }, projection.Recent.Select(x => x.DisplayName));
    }

    [Fact]
    public void SearchPreservesOrderInsidePinnedAndRecentGroups()
    {
        var repositories = new[]
        {
            Repository("match pinned two", "/repos/p2", null, true, 1, 10),
            Repository("match pinned one", "/repos/p1", null, true, 0, 1),
            Repository("match recent old", "/repos/r1", null, false, null, 2),
            Repository("match recent new", "/repos/r2", null, false, null, 5),
            Repository("hidden", "/repos/hidden", null, false, null, 20)
        };

        var projection = RecentRepositoriesProjectionBuilder.Build(repositories, "match");

        Assert.Equal(new[] { "match pinned one", "match pinned two" }, projection.Pinned.Select(x => x.DisplayName));
        Assert.Equal(new[] { "match recent new", "match recent old" }, projection.Recent.Select(x => x.DisplayName));
    }

    [Fact]
    public void QuickListExcludesCurrentAndPreservesPinnedThenRecentOrder()
    {
        var currentPath = Path.Combine(Path.GetTempPath(), "CSharpGit", "current");
        var repositories = new[]
        {
            Repository("Current pinned", currentPath + Path.DirectorySeparatorChar, null, true, 0, 10),
            Repository("Pinned", Path.Combine(Path.GetTempPath(), "CSharpGit", "pinned"), null, true, 1, 1),
            Repository("Recent old", Path.Combine(Path.GetTempPath(), "CSharpGit", "old"), null, false, null, 2),
            Repository("Recent new", Path.Combine(Path.GetTempPath(), "CSharpGit", "new"), null, false, null, 5)
        };

        var result = RecentRepositoriesProjectionBuilder.BuildQuickList(
            repositories,
            currentPath,
            8);

        Assert.Equal(
            new[] { "Pinned", "Recent new", "Recent old" },
            result.Select(repository => repository.DisplayName));
    }

    [Fact]
    public void QuickListAppliesCentralLimitAfterCurrentRepositoryIsRemoved()
    {
        var repositories = Enumerable.Range(0, 12)
            .Select(index => Repository(
                $"Repository {index}",
                Path.Combine(Path.GetTempPath(), "CSharpGit", $"repo-{index}"),
                null,
                false,
                null,
                index))
            .ToArray();

        var result = RecentRepositoriesProjectionBuilder.BuildQuickList(
            repositories,
            repositories[^1].Path,
            8);

        Assert.Equal(8, result.Count);
        Assert.DoesNotContain(result, repository =>
            RecentRepositoriesProjectionBuilder.PathsEqual(
                repository.Path,
                repositories[^1].Path));
    }

    private static RecentRepositorySettings Repository(
        string displayName,
        string path,
        string? branch,
        bool pinned,
        int? pinnedOrder,
        int minutes) =>
        new(
            path,
            displayName,
            DateTimeOffset.UnixEpoch.AddMinutes(minutes),
            branch,
            pinned,
            pinnedOrder);
}
