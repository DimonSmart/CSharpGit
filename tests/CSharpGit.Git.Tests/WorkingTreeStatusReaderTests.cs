using System.Diagnostics;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git.Tests;

public sealed class WorkingTreeStatusReaderTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"csharpgit-working-tree-status-{Guid.NewGuid():N}");

    [Fact]
    public async Task BackgroundReaderRunsExactlyOneStatusCommand()
    {
        var activity = new RecordingActivitySink();
        var executor = GitTestServices.CreateExecutor(activitySink: activity);
        var runner = new GitRepositoryCommandRunner(executor);
        var repository = await CreateRepositoryAsync(runner);
        activity.Commands.Clear();

        var snapshot = await new GitWorkingTreeStatusReader(runner).ReadAsync(repository);

        Assert.NotNull(snapshot);
        var command = Assert.Single(activity.Commands);
        Assert.Equal("status", command[0]);
        Assert.Contains("--porcelain=v2", command);
        Assert.Contains("-z", command);
        Assert.Contains("--untracked-files=all", command);
        Assert.DoesNotContain(activity.Commands, candidate =>
            candidate.Count > 0
            && candidate[0] is "config" or "for-each-ref" or "remote" or "stash" or "hash-object" or "ls-files");
    }

    [Fact]
    public async Task FullRefreshReturnsStatusSnapshotFromItsExistingStatusCommand()
    {
        var activity = new RecordingActivitySink();
        var executor = GitTestServices.CreateExecutor(activitySink: activity);
        var runner = new GitRepositoryCommandRunner(executor);
        var repository = await CreateRepositoryAsync(runner);
        activity.Commands.Clear();

        var service = new DefaultBranchRepositoryStateService(
            new GitRepositoryStateService(runner),
            new DefaultBranchResolver(executor),
            new GitTagService(executor));
        var read = await service.ReadWithWorkingTreeStatusAsync(repository);

        Assert.NotNull(read.WorkingTreeStatus);
        Assert.Equal(1, activity.Commands.Count(command => command.Count > 0 && command[0] == "status"));
        Assert.DoesNotContain(activity.Commands, command =>
            command.Count > 0 && command[0] is "hash-object" or "ls-files");
    }

    [Fact]
    public async Task RepeatedEditOfAlreadyModifiedFileKeepsSameStatusSnapshot()
    {
        var executor = GitTestServices.CreateExecutor();
        var runner = new GitRepositoryCommandRunner(executor);
        var repository = await CreateRepositoryAsync(runner);
        var reader = new GitWorkingTreeStatusReader(runner);

        await File.AppendAllTextAsync(Path.Combine(_root, "tracked.txt"), "first\n");
        var first = await reader.ReadAsync(repository);
        await File.AppendAllTextAsync(Path.Combine(_root, "tracked.txt"), "second\n");
        var second = await reader.ReadAsync(repository);

        Assert.Equal(first, second);
        Assert.Contains(second.Entries, entry => entry.Path == "tracked.txt");
    }

    [Fact]
    public async Task RepeatedEditOfExistingUntrackedFileKeepsSameStatusSnapshot()
    {
        var executor = GitTestServices.CreateExecutor();
        var runner = new GitRepositoryCommandRunner(executor);
        var repository = await CreateRepositoryAsync(runner);
        var reader = new GitWorkingTreeStatusReader(runner);

        var path = Path.Combine(_root, "notes.tmp");
        await File.WriteAllTextAsync(path, "first");
        var first = await reader.ReadAsync(repository);
        await File.AppendAllTextAsync(path, "second");
        var second = await reader.ReadAsync(repository);

        Assert.Equal(first, second);
        Assert.Contains(second.Entries, entry => entry.Path == "notes.tmp");
    }

    [Fact]
    public async Task RestagingDifferentContentChangesSnapshotThroughPorcelainObjectId()
    {
        var executor = GitTestServices.CreateExecutor();
        var runner = new GitRepositoryCommandRunner(executor);
        var repository = await CreateRepositoryAsync(runner);
        var reader = new GitWorkingTreeStatusReader(runner);

        await File.WriteAllTextAsync(Path.Combine(_root, "tracked.txt"), "first\n");
        RunGit(_root, "add", "tracked.txt");
        var first = await reader.ReadAsync(repository);

        await File.WriteAllTextAsync(Path.Combine(_root, "tracked.txt"), "second\n");
        RunGit(_root, "add", "tracked.txt");
        var second = await reader.ReadAsync(repository);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task IgnoredFilesDoNotAppearInSnapshot()
    {
        var executor = GitTestServices.CreateExecutor();
        var runner = new GitRepositoryCommandRunner(executor);
        var repository = await CreateRepositoryAsync(runner);
        var reader = new GitWorkingTreeStatusReader(runner);

        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        await File.WriteAllTextAsync(Path.Combine(_root, "obj", "cache.tmp"), "noise");

        var snapshot = await reader.ReadAsync(repository);

        Assert.DoesNotContain(snapshot.Entries, entry => entry.Path.StartsWith("obj/", StringComparison.Ordinal));
    }

    private async Task<CSharpGit.Domain.Repository> CreateRepositoryAsync(
        GitRepositoryCommandRunner runner)
    {
        Directory.CreateDirectory(_root);
        RunGit(_root, "init", "-b", "main");
        RunGit(_root, "config", "user.email", "tests@example.invalid");
        RunGit(_root, "config", "user.name", "Working Tree Status Tests");
        await File.WriteAllTextAsync(Path.Combine(_root, ".gitignore"), "bin/\nobj/\n");
        await File.WriteAllTextAsync(Path.Combine(_root, "tracked.txt"), "initial\n");
        RunGit(_root, "add", ".gitignore", "tracked.txt");
        RunGit(_root, "commit", "-m", "Initial");

        return await new GitRepositoryService(runner).OpenAsync(_root);
    }

    public void Dispose() => TestDirectory.Delete(_root);

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Git did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git failed: {stderr}{stdout}");
    }

    private sealed class RecordingActivitySink : IGitCommandActivitySink
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];

        public Guid Started(
            string executable,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandKind commandKind)
        {
            Commands.Add(arguments.ToArray());
            return Guid.NewGuid();
        }

        public void OutputReceived(Guid id, GitOutputStream stream, string chunk) { }
        public void Completed(Guid id, int exitCode) { }
        public void Cancelled(Guid id, int? exitCode) { }
    }
}
