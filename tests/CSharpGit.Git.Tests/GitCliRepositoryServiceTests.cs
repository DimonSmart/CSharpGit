using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;
using CSharpGit.Git;

namespace CSharpGit.Git.Tests;

public sealed class GitCliRepositoryServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task OpensRepositoryAndReadsStateFromGit()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "CSharpGit Tests");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "tracked\n");
        RunGit(_temporaryDirectory, "add", "tracked.txt");
        RunGit(_temporaryDirectory, "commit", "-m", "Initial commit");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        var initial = await service.ReadAsync(repository);

        Assert.Equal("main", initial.HeadReference);
        Assert.Equal(RepositoryOperation.None, initial.Operation);
        Assert.Empty(initial.Changes);
        Assert.Equal("CSharpGit Tests", initial.LocalConfiguration["user.name"]);
        Assert.NotNull(initial.HeadCommit);

        File.AppendAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "changed");
        var refreshed = await service.ReadAsync(repository);

        var change = Assert.Single(refreshed.Changes);
        Assert.Equal("tracked.txt", change.Path);
        Assert.Equal('M', change.WorkingTreeStatus);
    }

    [Fact]
    public async Task ReportsMissingGitExecutableClearly()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var service = new GitCliRepositoryService(new GitCliOptions { ExecutablePath = Path.Combine(_temporaryDirectory, "missing-git") });

        var exception = await Assert.ThrowsAsync<RepositoryOpenException>(
            () => service.OpenAsync(_temporaryDirectory));

        Assert.Contains("could not start", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Git", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreservesUnicodePathsCommitMessagesAndGitOutput()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "Unicode Tester");
        var fileName = "Привет 世界.txt";
        File.WriteAllText(Path.Combine(_temporaryDirectory, fileName), "содержимое\n");
        RunGit(_temporaryDirectory, "add", fileName);
        RunGit(_temporaryDirectory, "commit", "-m", "Добавлен мир 世界");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        var history = await service.ReadHistoryAsync(repository, new HistoryQuery(HistoryScope.CurrentBranch, null, 0, 20));

        var commit = Assert.Single(history.Rows);
        Assert.Equal("Добавлен мир 世界", commit.Commit.Subject);

        File.AppendAllText(Path.Combine(_temporaryDirectory, fileName), "изменение\n");
        var state = await service.ReadAsync(repository);
        var change = Assert.Single(state.Changes);
        Assert.Equal(fileName, change.Path);
    }

    [Fact]
    public async Task ReportsWorktreeAndCommonGitDirectory()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "Worktree Tester");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "tracked\n");
        RunGit(_temporaryDirectory, "add", "tracked.txt");
        RunGit(_temporaryDirectory, "commit", "-m", "Initial commit");
        var worktreeDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-worktree-{Guid.NewGuid():N}");
        RunGit(_temporaryDirectory, "worktree", "add", "-b", "feature", worktreeDirectory);
        try
        {
            var service = new GitCliRepositoryService();
            var repository = await service.OpenAsync(worktreeDirectory);

            Assert.True(repository.IsWorktree);
            Assert.Equal(Path.GetFullPath(worktreeDirectory), repository.WorkingDirectory);
            Assert.NotEqual(Path.GetFullPath(Path.Combine(_temporaryDirectory, ".git")), repository.GitDirectory);
        }
        finally
        {
            RunGit(_temporaryDirectory, "worktree", "remove", "--force", worktreeDirectory);
        }
    }

    [Fact]
    public async Task ReadsAllReferencesHistory()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "History Tester");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "base.txt"), "base\n");
        RunGit(_temporaryDirectory, "add", "base.txt");
        RunGit(_temporaryDirectory, "commit", "-m", "Base");
        RunGit(_temporaryDirectory, "switch", "-c", "feature");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "feature.txt"), "feature\n");
        RunGit(_temporaryDirectory, "add", "feature.txt");
        RunGit(_temporaryDirectory, "commit", "-m", "Feature");
        RunGit(_temporaryDirectory, "switch", "main");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "main.txt"), "main\n");
        RunGit(_temporaryDirectory, "add", "main.txt");
        RunGit(_temporaryDirectory, "commit", "-m", "Main");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        var history = await service.ReadHistoryAsync(repository, new HistoryQuery(HistoryScope.AllReferences, null, 0, 20));

        Assert.Contains(history.Rows, row => row.Commit.Subject == "Feature");
        Assert.Contains(history.Rows, row => row.Commit.Subject == "Main");
    }

    [Fact]
    public async Task ReadsCommitDetailsAndDiff()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "Diff Tester");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "sample.txt"), "before\n");
        RunGit(_temporaryDirectory, "add", "sample.txt");
        RunGit(_temporaryDirectory, "commit", "-m", "Initial");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "sample.txt"), "after\n");
        RunGit(_temporaryDirectory, "add", "sample.txt");
        RunGit(_temporaryDirectory, "commit", "-m", "Changed");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        var history = await service.ReadHistoryAsync(repository, new HistoryQuery(HistoryScope.CurrentBranch, null, 0, 20));
        var changed = history.Rows.First(row => row.Commit.Subject == "Changed").Commit;
        var details = await service.ReadCommitAsync(repository, changed.Hash);
        var file = Assert.Single(details.Files);
        var diff = await service.ReadDiffAsync(repository, changed.Hash, file.Path);

        Assert.Equal("sample.txt", file.Path);
        Assert.Contains(diff.Lines, line => line.Kind == DiffLineKind.Added && line.Text.Contains("after", StringComparison.Ordinal));
        Assert.Contains(diff.Lines, line => line.Kind == DiffLineKind.Removed && line.Text.Contains("before", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(startInfo) ?? throw new InvalidOperationException("Git did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git failed: {stderr}{stdout}");
    }
}
