using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class GitWorktreeListRegressionTests
{
    [Fact]
    public void Parse_TwoWindowsStyleWorktrees_ReturnsBothAndOnlyFirstIsPrimary()
    {
        var current = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OneTalent"));
        var repository = new Repository(current, current, Path.Combine(current, ".git"), false);
        var output = string.Join('\0',
            "worktree E:/Work/ADG-PSN/OneTalent",
            "HEAD 0e1ccc0320000000000000000000000000000000",
            "branch refs/heads/feature/rec-050-006-add-feedback-e2e",
            string.Empty,
            "worktree E:/Work/ADG-PSN/OneTalent-fix-offer-seed",
            "HEAD dcfc0f62c0000000000000000000000000000000",
            "branch refs/heads/fix/rec-offer-status-seed",
            string.Empty);

        var worktrees = GitWorktreeParser.Parse(output, repository);

        Assert.Equal(2, worktrees.Count);
        Assert.True(worktrees[0].IsPrimary);
        Assert.False(worktrees[1].IsPrimary);
        Assert.Equal("feature/rec-050-006-add-feedback-e2e", worktrees[0].Branch);
        Assert.Equal("fix/rec-offer-status-seed", worktrees[1].Branch);
    }
}
