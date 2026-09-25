using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class RepositoryStartupCommandBudgetTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"csharpgit-startup-budget-{Guid.NewGuid():N}");

    [Fact]
    public async Task CleanColdOpenUsesBoundedLocalOnlyCommandSet()
    {
        var work = CreateRemoteFixture();
        var activity = new RecordingActivitySink();
        var executor = new GitCommandExecutor(new GitCliOptions(), activity);
        var runner = new GitRepositoryCommandRunner(executor);
        var repositoryService = new GitRepositoryService(runner);
        var stateService = new DefaultBranchRepositoryStateService(
            new GitRepositoryStateService(runner),
            new DefaultBranchResolver(executor),
            new GitTagService(executor));
        var historyService = new GitFileAwareHistoryService(
            new GitReferenceHistoryService(executor),
            executor);

        var repository = await repositoryService.OpenAsync(work);
        var stateRead = await stateService.ReadWithRefreshFingerprintAsync(repository);
        var history = await historyService.ReadHistoryAsync(
            repository,
            new HistoryQuery(HistoryScope.AllReferences, null, 0, 100));

        Assert.NotNull(stateRead.RefreshFingerprint);
        Assert.NotEmpty(history.Rows);
        Assert.InRange(activity.Commands.Count, 1, 10);
        Assert.Equal(1, Count(activity.Commands, "--version"));
        Assert.Equal(1, Count(activity.Commands, "status"));
        Assert.Equal(1, Count(activity.Commands, "config"));
        Assert.Equal(1, Count(activity.Commands, "stash"));
        Assert.Equal(2, Count(activity.Commands, "for-each-ref"));
        Assert.Equal(1, Count(activity.Commands, "rev-parse"));
        Assert.DoesNotContain(
            activity.Commands,
            command => command.Count > 1
                       && command[0] == "remote"
                       && command[1] == "get-url");
        Assert.DoesNotContain(activity.Commands, IsNetworkCommand);
        Assert.DoesNotContain(
            activity.Commands,
            command => command.Count > 0 && command[0] == "symbolic-ref");

        var versionsBeforeSecondOpen = Count(activity.Commands, "--version");
        _ = await repositoryService.OpenAsync(work);
        _ = await stateService.ReadWithRefreshFingerprintAsync(repository);
        Assert.Equal(versionsBeforeSecondOpen, Count(activity.Commands, "--version"));
    }

    [Fact]
    public async Task MissingRemoteHeadAndUnreachableRemoteDoNotEnterNetworkPath()
    {
        Directory.CreateDirectory(_root);
        RunGit(_root, "init", "-b", "main");
        RunGit(_root, "config", "user.name", "Startup Budget");
        RunGit(_root, "config", "user.email", "startup@example.invalid");
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "tracked\n");
        RunGit(_root, "add", "tracked.txt");
        RunGit(_root, "commit", "-m", "Initial");
        RunGit(_root, "remote", "add", "origin", "https://example.invalid/unreachable.git");

        var activity = new RecordingActivitySink();
        var executor = new GitCommandExecutor(new GitCliOptions(), activity);
        var runner = new GitRepositoryCommandRunner(executor);
        var repositoryService = new GitRepositoryService(runner);
        var stateService = new DefaultBranchRepositoryStateService(
            new GitRepositoryStateService(runner),
            new DefaultBranchResolver(executor),
            new GitTagService(executor));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var repository = await repositoryService.OpenAsync(_root, timeout.Token);
        var read = await stateService.ReadWithRefreshFingerprintAsync(
            repository,
            cancellationToken: timeout.Token);

        Assert.NotNull(read.State);
        Assert.DoesNotContain(read.State.Refs.RemoteBranches, branch => branch.IsDefault);
        Assert.DoesNotContain(activity.Commands, IsNetworkCommand);
    }

    private string CreateRemoteFixture()
    {
        Directory.CreateDirectory(_root);
        var remote = Path.Combine(_root, "remote.git");
        var seed = Path.Combine(_root, "seed");
        var work = Path.Combine(_root, "work");
        Directory.CreateDirectory(seed);

        RunGit(_root, "init", "--bare", remote);
        RunGit(seed, "init", "-b", "main");
        RunGit(seed, "config", "user.name", "Startup Budget");
        RunGit(seed, "config", "user.email", "startup@example.invalid");
        File.WriteAllText(Path.Combine(seed, "tracked.txt"), "tracked\n");
        RunGit(seed, "add", "tracked.txt");
        RunGit(seed, "commit", "-m", "Initial");
        RunGit(seed, "remote", "add", "origin", remote);
        RunGit(seed, "push", "-u", "origin", "main");
        RunGit(remote, "symbolic-ref", "HEAD", "refs/heads/main");
        RunGit(_root, "clone", remote, work);
        return work;
    }

    private static int Count(
        IReadOnlyList<IReadOnlyList<string>> commands,
        string firstArgument) =>
        commands.Count(command => command.Count > 0
                                  && string.Equals(command[0], firstArgument, StringComparison.Ordinal));

    private static bool IsNetworkCommand(IReadOnlyList<string> command) =>
        command.Count > 0
        && command[0] is "ls-remote" or "fetch" or "pull" or "push";

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

    public void Dispose() => TestDirectory.Delete(_root);

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
