using System.Diagnostics;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class GitReferenceNavigationTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-ref-nav-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadsAllReferencesThroughOldTargetWithTrailingContext()
    {
        InitializeRepository();
        string targetHash = string.Empty;
        for (var index = 0; index < 110; index++)
        {
            RunGit("commit", "--allow-empty", "-m", $"commit-{index}");
            if (index == 5)
            {
                targetHash = RunGit("rev-parse", "HEAD");
                RunGit("branch", "stale/old", targetHash);
            }
        }

        var repository = await new GitCliRepositoryService().OpenAsync(_temporaryDirectory);
        var service = new GitReferenceHistoryService();
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

    private string RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _temporaryDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        return output.Trim();
    }

    public void Dispose() => TestDirectory.Delete(_temporaryDirectory);
}
