using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class RepositorySelectorGroupingTests
{
    [Fact]
    public void FourRepositoriesInSameDirectoryUseOneLabeledSeparator()
    {
        var parent = Path.Combine(Path.GetTempPath(), "private");
        var paths = new[] { "CSharpFar", "CSharpGit", "QuizGenerator", "NugetMcpServer" }
            .Select(name => Path.Combine(parent, name))
            .ToArray();

        var rows = RepositorySelectorGrouping.Build(paths);

        Assert.Equal(5, rows.Count);
        Assert.Null(rows[0].RepositoryIndex);
        Assert.Equal(parent, rows[0].DisplayPath);
        Assert.Equal(new[] { "CSharpFar", "CSharpGit", "QuizGenerator", "NugetMcpServer" },
            rows.Skip(1).Select(row => row.DisplayPath));
        Assert.Equal(paths, rows.Skip(1).Select(row => row.Path));
        Assert.Equal(new int?[] { 0, 1, 2, 3 }, rows.Skip(1).Select(row => row.RepositoryIndex));
    }

    [Fact]
    public void ExactlyThreeRepositoriesKeepFullPathsWithoutHeaders()
    {
        var parent = Path.Combine(Path.GetTempPath(), "work");
        var paths = new[] { "backend", "frontend", "shared" }
            .Select(name => Path.Combine(parent, name))
            .ToArray();

        var rows = RepositorySelectorGrouping.Build(paths);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.NotNull(row.RepositoryIndex));
        Assert.Equal(paths, rows.Select(row => row.DisplayPath));
    }

    [Fact]
    public void RootRepositoryGetsReadableNameWhenItsChildrenFormAGroup()
    {
        var parent = Path.Combine(Path.GetTempPath(), "work", "workspace");
        var paths = new[]
        {
            parent,
            Path.Combine(parent, "backend"),
            Path.Combine(parent, "frontend"),
            Path.Combine(parent, "tools")
        };

        var rows = RepositorySelectorGrouping.Build(paths);

        Assert.Equal(parent, rows[0].Path);
        Assert.Null(rows[0].RepositoryIndex);
        Assert.Equal(new[] { "root", "backend", "frontend", "tools" },
            rows.Skip(1).Select(row => row.DisplayPath));
    }

    [Fact]
    public void SeparateGroupsPreserveMemberOrderAndDoNotReorderStandaloneEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "csharpgit-grouping");
        var first = Path.Combine(root, "private");
        var second = Path.Combine(root, "work");
        var paths = new[]
        {
            Path.Combine(first, "A"),
            Path.Combine(second, "W1"),
            Path.Combine(first, "B"),
            Path.Combine(root, "misc"),
            Path.Combine(second, "W2"),
            Path.Combine(first, "C"),
            Path.Combine(second, "W3"),
            Path.Combine(first, "D"),
            Path.Combine(second, "W4")
        };

        var rows = RepositorySelectorGrouping.Build(paths);

        Assert.Equal(new[] { first, "A", "B", "C", "D", second, "W1", "W2", "W3", "W4", paths[3] },
            rows.Select(row => row.DisplayPath));
        Assert.Equal(new int?[] { null, 0, 2, 5, 7, null, 1, 4, 6, 8, 3 },
            rows.Select(row => row.RepositoryIndex));
    }

    [Fact]
    public void DeepestQualifiedGroupWinsAndParentDoesNotGetSpuriousHeader()
    {
        var root = Path.Combine(Path.GetTempPath(), "csharpgit-nested");
        var nested = Path.Combine(root, "workspace");
        var paths = new[]
        {
            Path.Combine(nested, "one"),
            Path.Combine(nested, "two"),
            Path.Combine(nested, "three"),
            Path.Combine(nested, "four"),
            Path.Combine(root, "other")
        };

        var rows = RepositorySelectorGrouping.Build(paths);

        Assert.Single(rows.Where(row => row.RepositoryIndex is null));
        Assert.Equal(nested, rows[0].Path);
        Assert.Equal(paths[4], rows[^1].DisplayPath);
        Assert.Equal(new[] { "one", "two", "three", "four" },
            rows.Skip(1).Take(4).Select(row => row.DisplayPath));
    }

    [Fact]
    public void CommonStringPrefixWithoutSharedDirectoryIsNotGrouped()
    {
        var root = Path.GetTempPath();
        var paths = new[] { "repo-one", "repo-two", "repo-three", "repo-four" }
            .Select(name => Path.Combine(root, name))
            .ToArray();
        var rows = RepositorySelectorGrouping.Build(paths);

        Assert.Single(rows.Where(row => row.RepositoryIndex is null));
        Assert.Equal(Path.TrimEndingDirectorySeparator(root), rows[0].Path);
    }

    [Fact]
    public void InvalidPathsRemainUnmodifiedAndDoNotCreateGroups()
    {
        var paths = new[] { "relative-one", "relative-two", "relative-three", "relative-four" };

        var rows = RepositorySelectorGrouping.Build(paths);

        Assert.Equal(paths, rows.Select(row => row.DisplayPath));
        Assert.All(rows, row => Assert.NotNull(row.RepositoryIndex));
    }
}
