using System.Diagnostics;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;

namespace CSharpGit.Git.Tests;

public sealed class PublishBranchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-publish-{Guid.NewGuid():N}");
    private readonly string _origin;

    public PublishBranchTests()
    {
        Directory.CreateDirectory(_root);
        Git(_root, "init", "-b", "main");
        Git(_root, "config", "user.email", "tests@example.invalid");
        Git(_root, "config", "user.name", "CSharpGit Tests");
        File.WriteAllText(Path.Combine(_root, "initial.txt"), "initial\n");
        Git(_root, "add", "initial.txt");
        Git(_root, "commit", "-m", "Initial");
        _origin = Path.Combine(_root, ".origin.git");
        Git(_root, "init", "--bare", _origin);
        Git(_root, "remote", "add", "origin", _origin);
    }

    [Fact]
    public async Task PublishSameNameCreatesRemoteBranchAndUpstream()
    {
        Git(_root, "switch", "-c", "feature/foo");
        Commit("feature.txt", "feature\n", "Feature");
        var (repository, service) = await CreateServicesAsync();

        await service.PublishBranchAsync(
            repository,
            new PublishBranchRequest("origin", "feature/foo", true));

        Assert.Equal(
            GitOut(_root, "rev-parse", "refs/heads/feature/foo"),
            GitOut(_root, "--git-dir", _origin, "rev-parse", "refs/heads/feature/foo"));
        Assert.Equal("origin/feature/foo", GitOut(_root, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"));
    }

    [Fact]
    public async Task PublishDifferentNameSetsExpectedMapping()
    {
        Git(_root, "switch", "-c", "feature/foo");
        Commit("feature.txt", "feature\n", "Feature");
        var (repository, service) = await CreateServicesAsync();

        await service.PublishBranchAsync(
            repository,
            new PublishBranchRequest("origin", "experiments/foo", true));

        Assert.Equal(
            GitOut(_root, "rev-parse", "refs/heads/feature/foo"),
            GitOut(_root, "--git-dir", _origin, "rev-parse", "refs/heads/experiments/foo"));
        Assert.Equal("origin/experiments/foo", GitOut(_root, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"));
    }

    [Fact]
    public async Task PublishWithoutTrackingDoesNotCreateUpstream()
    {
        Git(_root, "switch", "-c", "feature/untracked");
        Commit("feature.txt", "feature\n", "Feature");
        var (repository, service) = await CreateServicesAsync();

        await service.PublishBranchAsync(
            repository,
            new PublishBranchRequest("origin", "feature/untracked", false));

        Assert.Equal(
            GitOut(_root, "rev-parse", "refs/heads/feature/untracked"),
            GitOut(_root, "--git-dir", _origin, "rev-parse", "refs/heads/feature/untracked"));
        Assert.False(GitTryOut(_root, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}").Success);
    }

    [Fact]
    public async Task AutoSetupRemoteUsesCommandScopedConfigAndLeavesGitConfigUntouched()
    {
        Git(_root, "switch", "-c", "feature/auto");
        Commit("feature.txt", "feature\n", "Feature");
        Git(_root, "config", "push.default", "current");
        var localBefore = GitTryOut(_root, "config", "--local", "--get", "push.autoSetupRemote");
        var globalBefore = GitTryOut(_root, "config", "--global", "--get", "push.autoSetupRemote");
        var (repository, service) = await CreateServicesAsync();

        await service.PushAsync(repository, new PushOptions(AutoSetupRemote: true));

        Assert.Equal("origin/feature/auto", GitOut(_root, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"));
        Assert.Equal(localBefore, GitTryOut(_root, "config", "--local", "--get", "push.autoSetupRemote"));
        Assert.Equal(globalBefore, GitTryOut(_root, "config", "--global", "--get", "push.autoSetupRemote"));
    }

    [Fact]
    public async Task SuggestedRemoteFollowsGitPriority()
    {
        var second = Path.Combine(_root, ".second.git");
        Git(_root, "init", "--bare", second);
        Git(_root, "remote", "add", "second", second);
        var (repository, service) = await CreateServicesAsync();

        Git(_root, "config", "branch.main.remote", "origin");
        Assert.Equal("origin", (await service.PreparePublishBranchAsync(repository)).SuggestedRemote);

        Git(_root, "config", "remote.pushDefault", "second");
        Assert.Equal("second", (await service.PreparePublishBranchAsync(repository)).SuggestedRemote);

        Git(_root, "config", "branch.main.pushRemote", "origin");
        Assert.Equal("origin", (await service.PreparePublishBranchAsync(repository)).SuggestedRemote);
    }

    [Fact]
    public async Task AutomaticPushDoesNotGuessWithAmbiguousRemotes()
    {
        Git(_root, "remote", "rename", "origin", "one");
        var second = Path.Combine(_root, ".second.git");
        Git(_root, "init", "--bare", second);
        Git(_root, "remote", "add", "two", second);
        Git(_root, "config", "push.default", "current");
        var (repository, service) = await CreateServicesAsync();

        var failure = await Assert.ThrowsAsync<PushRejectedException>(
            () => service.PushAsync(repository, new PushOptions(AutoSetupRemote: true)));

        Assert.Equal(PushResultKind.PushDestinationUnavailable, failure.ResultKind);
        Assert.False(GitTryOut(_root, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}").Success);
    }

    [Fact]
    public async Task PublishRejectsDetachedHeadBeforePush()
    {
        Git(_root, "checkout", "--detach", "HEAD");
        var (repository, service) = await CreateServicesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PublishBranchAsync(
                repository,
                new PublishBranchRequest("origin", "detached", true)));
    }

    private async Task<(CSharpGit.Domain.Repository Repository, GitRepositorySyncService Service)> CreateServicesAsync()
    {
        var executor = new GitCommandExecutor(new GitCliOptions(), new GitCommandActivityHistory());
        var repository = await new GitRepositoryService(executor).OpenAsync(_root);
        return (repository, new GitRepositorySyncService(executor));
    }

    private void Commit(string name, string content, string message)
    {
        File.WriteAllText(Path.Combine(_root, name), content);
        Git(_root, "add", name);
        Git(_root, "commit", "-m", message);
    }

    private static void Git(string directory, params string[] arguments)
    {
        var result = RunGit(directory, arguments);
        Assert.True(result.Success, $"git {string.Join(' ', arguments)} failed: {result.Error}");
    }

    private static string GitOut(string directory, params string[] arguments)
    {
        var result = RunGit(directory, arguments);
        Assert.True(result.Success, $"git {string.Join(' ', arguments)} failed: {result.Error}");
        return result.Output.Trim();
    }

    private static (bool Success, string Output, string Error) GitTryOut(string directory, params string[] arguments) =>
        RunGit(directory, arguments);

    private static (bool Success, string Output, string Error) RunGit(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git did not start.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode == 0, output, error);
    }

    public void Dispose() => TestDirectory.Delete(_root);
}
