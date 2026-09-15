using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class GitWorktreeParserTests
{
    [Fact]
    public void Parse_PorcelainZ_ParsesBranchDetachedLockedAndPrunableStates()
    {
        var current = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "csharpgit-worktree-current"));
        var other = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "csharpgit-worktree-other"));
        var detached = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "csharpgit-worktree-detached"));
        var repository = new Repository(current, current, Path.Combine(current, ".git"), false);
        var output = string.Join('\0',
            $"worktree {current}",
            "HEAD 1111111111111111111111111111111111111111",
            "branch refs/heads/main",
            string.Empty,
            $"worktree {other}",
            "HEAD 2222222222222222222222222222222222222222",
            "branch refs/heads/feature/worktrees",
            "locked removable drive",
            "prunable gitdir file points to non-existent location",
            "future-field ignored",
            string.Empty,
            $"worktree {detached}",
            "HEAD 3333333333333333333333333333333333333333",
            "detached",
            string.Empty);

        var worktrees = GitWorktreeParser.Parse(output, repository);

        Assert.Equal(3, worktrees.Count);
        Assert.True(worktrees[0].IsPrimary);
        Assert.True(worktrees[0].IsCurrent);
        Assert.Equal("main", worktrees[0].Branch);
        Assert.False(worktrees[0].IsDetached);

        Assert.False(worktrees[1].IsPrimary);
        Assert.Equal("feature/worktrees", worktrees[1].Branch);
        Assert.True(worktrees[1].IsLocked);
        Assert.Equal("removable drive", worktrees[1].LockReason);
        Assert.True(worktrees[1].IsPrunable);

        Assert.False(worktrees[2].IsPrimary);
        Assert.Null(worktrees[2].Branch);
        Assert.True(worktrees[2].IsDetached);
        Assert.False(worktrees[2].IsCurrent);
        Assert.Single(worktrees, candidate => candidate.IsPrimary);
    }

    [Fact]
    public void Parse_FirstValidRecordIsPrimaryIndependentlyOfCurrentWorktreeAndBranchName()
    {
        var primary = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "csharpgit-worktree-primary"));
        var current = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "csharpgit-worktree-linked-current"));
        var repository = new Repository(current, current, Path.Combine(current, ".git"), false);
        var output = string.Join('\0',
            "worktree ",
            "HEAD 0000000000000000000000000000000000000000",
            string.Empty,
            $"worktree {primary}",
            "HEAD 1111111111111111111111111111111111111111",
            "branch refs/heads/feature/primary-location",
            string.Empty,
            $"worktree {current}",
            "HEAD 2222222222222222222222222222222222222222",
            "branch refs/heads/main",
            string.Empty);

        var worktrees = GitWorktreeParser.Parse(output, repository);

        Assert.Equal(2, worktrees.Count);
        Assert.True(worktrees[0].IsPrimary);
        Assert.False(worktrees[0].IsCurrent);
        Assert.Equal("feature/primary-location", worktrees[0].Branch);
        Assert.False(worktrees[1].IsPrimary);
        Assert.True(worktrees[1].IsCurrent);
        Assert.Equal("main", worktrees[1].Branch);
        Assert.Single(worktrees, candidate => candidate.IsPrimary);
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsEmptyList()
    {
        var current = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "csharpgit-worktree-empty"));
        var repository = new Repository(current, current, Path.Combine(current, ".git"), false);

        Assert.Empty(GitWorktreeParser.Parse(string.Empty, repository));
    }
}
