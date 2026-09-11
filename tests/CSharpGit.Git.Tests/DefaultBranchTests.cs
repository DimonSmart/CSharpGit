using System.Diagnostics;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class DefaultBranchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-default-branch-{Guid.NewGuid():N}");

    [Fact]
    public async Task UsesRemoteHeadWithoutInferringBranchNameAndFallsBackToAdvertisedHead()
    {
        var fixture = CreateFixture("develop");
        RunGit(fixture.Work, "remote", "set-head", "origin", "--delete");

        var services = CreateServices();
        var repository = await services.Inner.OpenAsync(fixture.Work);
        var state = await services.State.ReadAsync(repository);

        Assert.True(state.Refs.RemoteBranches.Single(branch => branch.Name == "origin/develop").IsDefault);
        Assert.True(state.Refs.LocalBranches.Single(branch => branch.Name == "develop").IsDefault);
        Assert.DoesNotContain(state.Refs.RemoteBranches, branch => branch.Name is "origin/main" or "origin/master");
    }

    [Fact]
    public async Task RefreshesChangedRemoteDefaultAndMarksLocalBranchOnlyByUpstream()
    {
        var fixture = CreateFixture("develop");
        var services = CreateServices();
        var repository = await services.Inner.OpenAsync(fixture.Work);

        var initial = await services.State.ReadAsync(repository);
        Assert.True(initial.Refs.RemoteBranches.Single(branch => branch.Name == "origin/develop").IsDefault);
        Assert.True(initial.Refs.LocalBranches.Single(branch => branch.Name == "develop").IsDefault);

        RunGit(fixture.Seed, "switch", "-c", "release/candidate");
        File.WriteAllText(Path.Combine(fixture.Seed, "release.txt"), "release\n");
        RunGit(fixture.Seed, "add", "release.txt");
        RunGit(fixture.Seed, "commit", "-m", "Release candidate");
        RunGit(fixture.Seed, "push", "origin", "release/candidate");
        RunGit(fixture.Remote, "symbolic-ref", "HEAD", "refs/heads/release/candidate");

        RunGit(fixture.Work, "branch", "release/candidate", "develop");
        RunGit(fixture.Work, "branch", "--set-upstream-to=origin/develop", "release/candidate");

        await services.References.FetchAsync(repository, "origin");
        var refreshed = await services.State.ReadAsync(repository);

        Assert.False(refreshed.Refs.RemoteBranches.Single(branch => branch.Name == "origin/develop").IsDefault);
        Assert.True(refreshed.Refs.RemoteBranches.Single(branch => branch.Name == "origin/release/candidate").IsDefault);
        Assert.False(refreshed.Refs.LocalBranches.Single(branch => branch.Name == "release/candidate").IsDefault);
        Assert.DoesNotContain(refreshed.Refs.LocalBranches, branch => branch.IsDefault);

        RunGit(fixture.Work, "branch", "--set-upstream-to=origin/release/candidate", "release/candidate");
        var tracking = await services.State.ReadAsync(repository);

        Assert.True(tracking.Refs.LocalBranches.Single(branch => branch.Name == "release/candidate").IsDefault);
    }

    [Fact]
    public async Task LeavesBranchesUnmarkedWhenNoPrimaryRemoteExists()
    {
        var working = Path.Combine(_root, "local");
        Directory.CreateDirectory(working);
        RunGit(working, "init", "-b", "main");
        ConfigureIdentity(working);
        File.WriteAllText(Path.Combine(working, "tracked.txt"), "tracked\n");
        RunGit(working, "add", "tracked.txt");
        RunGit(working, "commit", "-m", "Initial");

        var services = CreateServices();
        var repository = await services.Inner.OpenAsync(working);
        var state = await services.State.ReadAsync(repository);

        Assert.DoesNotContain(state.Refs.LocalBranches, branch => branch.IsDefault);
        Assert.DoesNotContain(state.Refs.RemoteBranches, branch => branch.IsDefault);
    }

    public void Dispose() => TestDirectory.Delete(_root);

    private Fixture CreateFixture(string defaultBranch)
    {
        Directory.CreateDirectory(_root);
        var remote = Path.Combine(_root, "remote.git");
        var seed = Path.Combine(_root, "seed");
        var work = Path.Combine(_root, "work");
        Directory.CreateDirectory(seed);

        RunGit(_root, "init", "--bare", remote);
        RunGit(seed, "init", "-b", defaultBranch);
        ConfigureIdentity(seed);
        File.WriteAllText(Path.Combine(seed, "tracked.txt"), "tracked\n");
        RunGit(seed, "add", "tracked.txt");
        RunGit(seed, "commit", "-m", "Initial");
        RunGit(seed, "remote", "add", "origin", remote);
        RunGit(seed, "push", "-u", "origin", defaultBranch);
        RunGit(remote, "symbolic-ref", "HEAD", $"refs/heads/{defaultBranch}");
        RunGit(_root, "clone", remote, work);

        return new Fixture(remote, seed, work);
    }

    private static Services CreateServices()
    {
        var executor = new GitCommandExecutor(new GitCliOptions());
        var inner = new GitCliRepositoryService(executor);
        var resolver = new DefaultBranchResolver(executor);
        return new Services(
            inner,
            new DefaultBranchRepositoryStateService(inner, resolver),
            new DefaultBranchReferenceService(inner, resolver));
    }

    private static void ConfigureIdentity(string workingDirectory)
    {
        RunGit(workingDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(workingDirectory, "config", "user.name", "CSharpGit Tests");
    }

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
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Git did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git failed: {stderr}{stdout}");
    }

    private sealed record Fixture(string Remote, string Seed, string Work);
    private sealed record Services(
        GitCliRepositoryService Inner,
        DefaultBranchRepositoryStateService State,
        DefaultBranchReferenceService References);
}
