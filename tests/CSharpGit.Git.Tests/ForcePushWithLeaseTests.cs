using System.Diagnostics;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class ForcePushWithLeaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-force-{Guid.NewGuid():N}");
    private readonly string _remote;

    public ForcePushWithLeaseTests()
    {
        Directory.CreateDirectory(_root);
        Git(_root, "init", "-b", "main");
        ConfigureIdentity(_root);
        Commit(_root, "history.txt", "A\n", "A");
        _remote = Path.Combine(_root, ".remote.git");
        Git(_root, "init", "--bare", _remote);
        Git(_root, "remote", "add", "origin", _remote);
    }

    [Fact]
    public void BuildsOnlyExplicitLeaseAndFullRefspec()
    {
        const string expected = "0123456789abcdef0123456789abcdef01234567";
        var snapshot = new ForcePushWithLeaseSnapshot(
            "feature/local-name", expected, "origin", "/tmp/remote.git",
            "feature/server-name", expected);

        var arguments = GitCliRepositoryService.BuildForcePushArguments(snapshot);

        Assert.Equal("push", arguments[0]);
        Assert.Contains("--porcelain", arguments);
        Assert.Contains($"--force-with-lease=refs/heads/feature/server-name:{expected}", arguments);
        Assert.Contains("refs/heads/feature/local-name:refs/heads/feature/server-name", arguments);
        Assert.DoesNotContain("--force", arguments);
        Assert.DoesNotContain("-f", arguments);
        Assert.DoesNotContain(arguments, argument => argument.StartsWith('+'));
        Assert.DoesNotContain("--force-with-lease", arguments);
        Assert.DoesNotContain("--force-with-lease=refs/heads/feature/server-name", arguments);
    }

    [Fact]
    public async Task SucceedsWithConfiguredUpstreamAndDifferentBranchNames()
    {
        Commit(_root, "history.txt", "A\nB\n", "B");
        Commit(_root, "history.txt", "A\nB\nC\n", "C");
        Git(_root, "push", "origin", "refs/heads/main:refs/heads/server-main");
        Git(_root, "config", "branch.main.remote", "origin");
        Git(_root, "config", "branch.main.merge", "refs/heads/server-main");
        var oldRemote = RemoteTip("server-main");
        Git(_root, "reset", "--hard", "HEAD~2");
        Commit(_root, "history.txt", "A\nB2\n", "B2");
        Commit(_root, "history.txt", "A\nB2\nC2\n", "C2");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var snapshot = await service.PrepareForcePushWithLeaseAsync(repository);

        Assert.Equal("main", snapshot.LocalBranch);
        Assert.Equal("origin", snapshot.Remote);
        Assert.Equal("server-main", snapshot.RemoteBranch);
        Assert.Equal(oldRemote, snapshot.ExpectedRemoteCommit);
        Assert.Equal(Path.GetFullPath(_remote), Path.GetFullPath(snapshot.RemotePushDestination));

        await service.ForcePushWithLeaseAsync(repository, snapshot);

        Assert.Equal(GitOut(_root, "rev-parse", "refs/heads/main"), RemoteTip("server-main"));
    }

    [Fact]
    public async Task RejectsOldSnapshotWhenAnotherActorAdvancesRemote()
    {
        Commit(_root, "history.txt", "A\nB\n", "B");
        Commit(_root, "history.txt", "A\nB\nC\n", "C");
        Git(_root, "push", "--set-upstream", "origin", "main");
        Git(_root, "reset", "--hard", "HEAD~2");
        Commit(_root, "history.txt", "A\nB2\n", "B2");
        Commit(_root, "history.txt", "A\nB2\nC2\n", "C2");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var snapshot = await service.PrepareForcePushWithLeaseAsync(repository);
        var actor = Path.Combine(_root, "actor");
        Git(_root, "clone", "--branch", "main", _remote, actor);
        ConfigureIdentity(actor);
        File.AppendAllText(Path.Combine(actor, "history.txt"), "D\n");
        Git(actor, "add", "history.txt");
        Git(actor, "commit", "-m", "D");
        Git(actor, "push", "origin", "main");
        var advancedRemote = RemoteTip("main");

        var failure = await Assert.ThrowsAsync<PushRejectedException>(
            () => service.ForcePushWithLeaseAsync(repository, snapshot));

        Assert.Equal(PushResultKind.LeaseRejected, failure.ResultKind);
        Assert.Equal(advancedRemote, RemoteTip("main"));
        Assert.NotEqual(snapshot.ExpectedRemoteCommit, advancedRemote);
    }

    [Fact]
    public async Task MissingUpstreamRequiresExplicitTargetAndDoesNotGuess()
    {
        Git(_root, "push", "origin", "refs/heads/main:refs/heads/server-main");
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);

        var missing = await Assert.ThrowsAsync<ForcePushWithLeasePreparationException>(
            () => service.PrepareForcePushWithLeaseAsync(repository));
        Assert.Equal(ForcePushPreparationFailure.MissingUpstream, missing.Failure);

        var explicitSnapshot = await service.PrepareForcePushWithLeaseAsync(repository, "origin", "server-main");
        Assert.Equal("server-main", explicitSnapshot.RemoteBranch);
    }

    [Fact]
    public async Task RefusesDetachedHeadMissingRemoteBranchAndMultiplePushDestinations()
    {
        Git(_root, "push", "origin", "main");
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);

        var missing = await Assert.ThrowsAsync<ForcePushWithLeasePreparationException>(
            () => service.PrepareForcePushWithLeaseAsync(repository, "origin", "missing"));
        Assert.Equal(ForcePushPreparationFailure.RemoteBranchDoesNotExist, missing.Failure);

        var secondRemote = Path.Combine(_root, ".second.git");
        Git(_root, "init", "--bare", secondRemote);
        Git(_root, "remote", "set-url", "--add", "--push", "origin", _remote);
        Git(_root, "remote", "set-url", "--add", "--push", "origin", secondRemote);
        var multiple = await Assert.ThrowsAsync<ForcePushWithLeasePreparationException>(
            () => service.PrepareForcePushWithLeaseAsync(repository, "origin", "main"));
        Assert.Equal(ForcePushPreparationFailure.MultiplePushDestinations, multiple.Failure);

        Git(_root, "remote", "set-url", "--delete", "--push", "origin", secondRemote);
        Git(_root, "checkout", "--detach", "HEAD");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PrepareForcePushWithLeaseAsync(repository, "origin", "main"));
    }

    [Fact]
    public async Task CancelsBeforePushWhenLocalBranchOrTipChanges()
    {
        Git(_root, "push", "--set-upstream", "origin", "main");
        var originalRemote = RemoteTip("main");
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var branchSnapshot = await service.PrepareForcePushWithLeaseAsync(repository);
        Git(_root, "switch", "-c", "other");

        await Assert.ThrowsAsync<ForcePushWithLeaseCancelledException>(
            () => service.ForcePushWithLeaseAsync(repository, branchSnapshot));
        Assert.Equal(originalRemote, RemoteTip("main"));

        Git(_root, "switch", "main");
        var tipSnapshot = await service.PrepareForcePushWithLeaseAsync(repository);
        Commit(_root, "tip.txt", "changed\n", "local tip changed");
        await Assert.ThrowsAsync<ForcePushWithLeaseCancelledException>(
            () => service.ForcePushWithLeaseAsync(repository, tipSnapshot));
        Assert.Equal(originalRemote, RemoteTip("main"));
    }

    [Fact]
    public async Task CancelsBeforePushWhenPushDestinationChanges()
    {
        Git(_root, "push", "--set-upstream", "origin", "main");
        var originalRemote = RemoteTip("main");
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var snapshot = await service.PrepareForcePushWithLeaseAsync(repository);
        var replacement = Path.Combine(_root, ".replacement.git");
        Git(_root, "init", "--bare", replacement);
        Git(_root, "remote", "set-url", "--push", "origin", replacement);

        await Assert.ThrowsAsync<ForcePushWithLeaseCancelledException>(
            () => service.ForcePushWithLeaseAsync(repository, snapshot));
        Assert.Equal(originalRemote, RemoteTip("main"));
    }

    [Fact]
    public async Task OrdinaryPushRemainsOrdinaryAndClassifiesNonFastForward()
    {
        Git(_root, "push", "--set-upstream", "origin", "main");
        var actor = Path.Combine(_root, "ordinary-actor");
        Git(_root, "clone", "--branch", "main", _remote, actor);
        ConfigureIdentity(actor);
        Commit(actor, "actor.txt", "remote\n", "remote advance");
        Git(actor, "push", "origin", "main");
        Commit(_root, "local.txt", "local\n", "local divergence");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var failure = await Assert.ThrowsAsync<PushRejectedException>(() => service.PushAsync(repository));

        Assert.Equal(PushResultKind.NonFastForwardRejected, failure.ResultKind);
        Assert.Equal(GitOut(actor, "rev-parse", "HEAD"), RemoteTip("main"));
    }

    [Theory]
    [InlineData("remote: Authentication failed", PushResultKind.AuthenticationOrTransportFailure)]
    [InlineData("! [rejected] main -> main (stale info)", PushResultKind.LeaseRejected)]
    [InlineData("! [rejected] main -> main (non-fast-forward)", PushResultKind.NonFastForwardRejected)]
    [InlineData("! [remote rejected] main -> main (pre-receive hook declined)", PushResultKind.RemoteRejected)]
    public void ClassifiesPushFailuresWithoutConfusingRemotePrefix(string message, PushResultKind expected)
    {
        Assert.Equal(expected, GitCliRepositoryService.ClassifyPushFailure(message));
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
