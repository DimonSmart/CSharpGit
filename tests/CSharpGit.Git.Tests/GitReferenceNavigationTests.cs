using System.Text;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class GitReferenceNavigationTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-ref-nav-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadsAllReferencesThroughOldTargetWithTrailingContext()
    {
        InitializeRepository();
        // One fast-import process replaces 110 separate git commit processes.
        var targetHash = BuildLinearHistory(110);
        RunGit("branch", "stale/old", targetHash);

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(_temporaryDirectory);
        var service = GitTestServices.CreateHistoryService();
        var firstPage = await service.ReadHistoryAsync(
            repository,
            new HistoryQuery(HistoryScope.AllReferences, null, 0, 100));

        Assert.DoesNotContain(firstPage.Rows, row => row.Commit.Hash == targetHash);

        var navigationPage = await service.ReadHistoryThroughCommitAsync(
            repository,
            HistoryScope.AllReferences,
            targetHash,
            trailingCount: 3);

        var targetIndex = navigationPage.Rows.ToList().FindIndex(row => row.Commit.Hash == targetHash);
        Assert.True(targetIndex >= 100);
        Assert.True(navigationPage.Rows.Count >= targetIndex + 1);
        Assert.Equal("commit-5", navigationPage.Rows[targetIndex].Commit.Subject);
        Assert.Contains(navigationPage.Rows, row => row.Commit.Subject == "commit-4");
        Assert.True(navigationPage.HasMore);
        Assert.Equal("main", RunGit("branch", "--show-current"));
    }

    private void InitializeRepository()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit("init", "-b", "main");
        RunGit("config", "user.email", "tests@example.invalid");
        RunGit("config", "user.name", "CSharpGit Tests");
    }

    private string BuildLinearHistory(int commitCount)
    {
        var commands = new StringBuilder("feature done\n");
        for (var index = 0; index < commitCount; index++)
        {
            var subject = $"commit-{index}";
            commands.Append("commit refs/heads/main\n");
            commands.Append("mark :").Append(index + 1).Append('\n');
            commands.Append("committer CSharpGit Tests <tests@example.invalid> ")
                .Append(1700000000L + index).Append(" +0000\n");
            commands.Append("data ").Append(subject.Length).Append('\n');
            commands.Append(subject).Append('\n');
            if (index > 0) commands.Append("from :").Append(index).Append('\n');
            commands.Append('\n');
        }
        commands.Append("done\n");
        TestGitRunner.RunWithInput(_temporaryDirectory, commands.ToString(), "fast-import", "--quiet");
        // fast-import updates the branch but not the checked-out index or worktree.
        RunGit("reset", "--hard", "HEAD");
        return RunGit("rev-parse", $"HEAD~{commitCount - 6}");
    }

    private string RunGit(params string[] arguments) =>
        TestGitRunner.Run(_temporaryDirectory, arguments);

    public void Dispose() => TestDirectory.Delete(_temporaryDirectory);
}
