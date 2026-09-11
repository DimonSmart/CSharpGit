using System.Diagnostics;
using CSharpGit.Application.Exceptions;

namespace CSharpGit.Git.Tests;

public sealed class ForcePushWithLeaseRetryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-force-count-{Guid.NewGuid():N}");
    private readonly string _remote;
    private readonly string _pushLog;

    public ForcePushWithLeaseRetryTests()
    {
        Directory.CreateDirectory(_root);
        Git(_root, "init", "-b", "main");
        ConfigureIdentity(_root);
        Commit(_root, "history.txt", "A\n", "A");
        _remote = Path.Combine(_root, ".remote.git");
        _pushLog = Path.Combine(_root, "push-invocations.log");
        Git(_root, "init", "--bare", _remote);
        Git(_root, "remote", "add", "origin", _remote);
    }

    [Fact]
    public async Task LeaseRejectionExecutesExactlyOneGitPush()
    {
        if (OperatingSystem.IsWindows()) return;

        Commit(_root, "history.txt", "A\nB\n", "B");
        Commit(_root, "history.txt", "A\nB\nC\n", "C");
        Git(_root, "push", "--set-upstream", "origin", "main");
        Git(_root, "reset", "--hard", "HEAD~2");
        Commit(_root, "history.txt", "A\nB2\n", "B2");
        Commit(_root, "history.txt", "A\nB2\nC2\n", "C2");

        var normalService = new GitCliRepositoryService();
        var repository = await normalService.OpenAsync(_root);
        var snapshot = await normalService.PrepareForcePushWithLeaseAsync(repository);

        var actor = Path.Combine(_root, "actor");
        Git(_root, "clone", "--branch", "main", _remote, actor);
        ConfigureIdentity(actor);
        File.AppendAllText(Path.Combine(actor, "history.txt"), "D\n");
        Git(actor, "add", "history.txt");
        Git(actor, "commit", "-m", "D");
        Git(actor, "push", "origin", "main");
        var advancedRemote = RemoteTip("main");

        var wrapper = Path.Combine(_root, "counting-git.sh");
        File.WriteAllText(wrapper,
            $"#!/bin/sh\nif [ \"$1\" = \"push\" ]; then printf 'push\\n' >> '{_pushLog}'; fi\nexec git \"$@\"\n");
        File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var executor = new GitCommandExecutor(new GitCliOptions { ExecutablePath = wrapper });
        var instrumentedService = new GitCliRepositoryService(executor);
        var instrumentedRepository = await instrumentedService.OpenAsync(_root);
        var failure = await Assert.ThrowsAsync<PushRejectedException>(
            () => instrumentedService.ForcePushWithLeaseAsync(instrumentedRepository, snapshot));

        Assert.Equal(PushResultKind.LeaseRejected, failure.ResultKind);
        Assert.True(File.Exists(_pushLog));
        Assert.Single(File.ReadAllLines(_pushLog));
        Assert.Equal(advancedRemote, RemoteTip("main"));
    }

    private string RemoteTip(string branch) => GitOut(_root, "--git-dir", _remote, "rev-parse", $"refs/heads/{branch}");

    private static void Commit(string directory, string name, string content, string message)
    {
        File.WriteAllText(Path.Combine(directory, name), content);
        Git(directory, "add", name);
        Git(directory, "commit", "-m", message);
    }

    private static void ConfigureIdentity(string directory)
    {
        Git(directory, "config", "user.email", "tests@example.invalid");
        Git(directory, "config", "user.name", "CSharpGit Tests");
    }

    private static void Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
    }

    private static string GitOut(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
        return output.Trim();
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
