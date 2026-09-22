using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class GitWorktreeServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemoveAsync_PrimaryWorktreeIsRejectedBeforeGitExecution(bool force)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "csharpgit-primary-worktree"));
        var repository = new Repository(root, root, Path.Combine(root, ".git"), false);
        var worktree = new WorktreeInfo(
            root,
            "1111111111111111111111111111111111111111",
            "feature/primary",
            IsCurrent: false,
            IsDetached: false,
            IsLocked: false,
            LockReason: null,
            IsPrunable: false)
        {
            IsPrimary = true
        };
        var service = new GitWorktreeService(GitTestServices.CreateExecutor());

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = service.RemoveAsync(repository, worktree, force);
        });

        Assert.Contains("primary worktree", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
