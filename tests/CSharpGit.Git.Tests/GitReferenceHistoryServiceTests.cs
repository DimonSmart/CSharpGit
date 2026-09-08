using System.Diagnostics;

namespace CSharpGit.Git.Tests;

public sealed class GitReferenceHistoryServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-ref-history-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadsSelectedBranchWithoutCheckingItOut()
    {
        InitializeRepository();
        File.WriteAllText(Path.Combine(_temporaryDirectory, "file.txt"), "initial\n");
        RunGit("add", "file.txt");
        RunGit("commit", "-m", "initial");
        RunGit("switch", "-c", "feature/demo");
        File.AppendAllText(Path.Combine(_temporaryDirectory, "file.txt"), "feature\n");
        RunGit("commit", "-am", "feature commit");
        RunGit("switch", "main");
        File.AppendAllText(Path.Combine(_temporaryDirectory, "file.txt"), "main\n");
        RunGit("commit", "-am", "main commit");

        var repository = await new GitCliRepositoryService().OpenAsync(_temporaryDirectory);
        var page = await new GitReferenceHistoryService().ReadHistoryAsync(repository, "feature/demo", null, 0, 20);

        Assert.Equal("feature commit", page.Rows[0].Commit.Subject);
        Assert.Contains(page.Rows, row => row.Commit.Subject == "initial");
        Assert.DoesNotContain(page.Rows, row => row.Commit.Subject == "main commit");
        Assert.Equal("main", RunGit("branch", "--show-current"));
    }

    [Fact]
    public async Task ReadsCompactFileStatusForCommit()
    {
        InitializeRepository();
        File.WriteAllText(Path.Combine(_temporaryDirectory, "file.txt"), "initial\n");
        RunGit("add", "file.txt");
        RunGit("commit", "-m", "initial");
        File.AppendAllText(Path.Combine(_temporaryDirectory, "file.txt"), "changed\n");
        RunGit("commit", "-am", "change");
        var hash = RunGit("rev-parse", "HEAD");

        var repository = await new GitCliRepositoryService().OpenAsync(_temporaryDirectory);
        var statuses = await new GitReferenceHistoryService().ReadFileStatusesAsync(repository, hash);

        Assert.Equal("M", statuses["file.txt"]);
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

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, true);
    }
}
