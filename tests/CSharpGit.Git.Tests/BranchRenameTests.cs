using System.Diagnostics;
using CSharpGit.Application.Exceptions;

namespace CSharpGit.Git.Tests;

public sealed class BranchRenameTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-branch-rename-{Guid.NewGuid():N}");

    [Fact]
    public async Task RenamesLocalBranchWithoutChangingCommit()
    {
        var work = CreateRepository();
        RunGit(work, "branch", "feature/old");
        var before = RunGitOutput(work, "rev-parse", "feature/old").Trim();
        var repositoryService = new GitRepositoryService();
        var service = new GitReferenceService();
        var repository = await repositoryService.OpenAsync(work);

        await service.RenameBranchAsync(repository, "feature/old", "feature/new");

        Assert.False(LocalBranchExists(work, "feature/old"));
        Assert.True(LocalBranchExists(work, "feature/new"));
        Assert.Equal(before, RunGitOutput(work, "rev-parse", "feature/new").Trim());
    }

    [Fact]
    public async Task RenamesCurrentBranchAndUpdatesHead()
    {
        var work = CreateRepository();
        RunGit(work, "switch", "-c", "feature/current");
        var repositoryService = new GitRepositoryService();
        var service = new GitReferenceService();
        var repository = await repositoryService.OpenAsync(work);

        await service.RenameBranchAsync(repository, "feature/current", "feature/renamed");

        Assert.Equal("refs/heads/feature/renamed", RunGitOutput(work, "symbolic-ref", "HEAD").Trim());
        Assert.Equal("feature/renamed", RunGitOutput(work, "branch", "--show-current").Trim());
    }

    [Fact]
    public async Task RenameCanMoveBranchBetweenNamespaces()
    {
        var work = CreateRepository();
        RunGit(work, "branch", "feature/login");
        var repositoryService = new GitRepositoryService();
        var service = new GitReferenceService();
        var repository = await repositoryService.OpenAsync(work);

        await service.RenameBranchAsync(repository, "feature/login", "bugfix/login");

        Assert.False(LocalBranchExists(work, "feature/login"));
        Assert.True(LocalBranchExists(work, "bugfix/login"));
    }

    [Fact]
    public async Task ExactNameConflictFailsWithoutChangingEitherBranch()
    {
        var work = CreateRepository();
        RunGit(work, "branch", "feature/one");
        RunGit(work, "branch", "feature/two");
        var repositoryService = new GitRepositoryService();
        var service = new GitReferenceService();
        var repository = await repositoryService.OpenAsync(work);

        await Assert.ThrowsAsync<RepositoryOpenException>(() =>
            service.RenameBranchAsync(repository, "feature/one", "feature/two"));

        Assert.True(LocalBranchExists(work, "feature/one"));
        Assert.True(LocalBranchExists(work, "feature/two"));
    }

    [Fact]
    public async Task NamespaceConflictFailsWithoutDamagingRefs()
    {
        var work = CreateRepository();
        RunGit(work, "branch", "feature");
        RunGit(work, "branch", "source");
        var repositoryService = new GitRepositoryService();
        var service = new GitReferenceService();
        var repository = await repositoryService.OpenAsync(work);

        await Assert.ThrowsAsync<RepositoryOpenException>(() =>
            service.RenameBranchAsync(repository, "source", "feature/login"));

        Assert.True(LocalBranchExists(work, "feature"));
        Assert.True(LocalBranchExists(work, "source"));
        Assert.False(LocalBranchExists(work, "feature/login"));
    }

    [Fact]
    public async Task RenamesBranchCheckedOutInLinkedWorktree()
    {
        var work = CreateRepository();
        RunGit(work, "branch", "feature/worktree");
        var linkedWorktree = Path.Combine(_temporaryDirectory, $"linked-{Guid.NewGuid():N}");
        RunGit(work, "worktree", "add", linkedWorktree, "feature/worktree");
        var repositoryService = new GitRepositoryService();
        var service = new GitReferenceService();
        var repository = await repositoryService.OpenAsync(work);

        await service.RenameBranchAsync(repository, "feature/worktree", "feature/renamed-worktree");

        Assert.False(LocalBranchExists(work, "feature/worktree"));
        Assert.True(LocalBranchExists(work, "feature/renamed-worktree"));
        Assert.Equal("refs/heads/feature/renamed-worktree", RunGitOutput(linkedWorktree, "symbolic-ref", "HEAD").Trim());
        Assert.Equal("feature/renamed-worktree", RunGitOutput(linkedWorktree, "branch", "--show-current").Trim());
        RunGit(linkedWorktree, "status", "--porcelain");
    }

    [Fact]
    public async Task TrackingBranchKeepsExistingRemoteAndUpstream()
    {
        var (work, remote) = CreateRepositoryWithRemote();
        RunGit(work, "switch", "-c", "feature/foo");
        RunGit(work, "push", "-u", "origin", "feature/foo");
        var repositoryService = new GitRepositoryService();
        var service = new GitReferenceService();
        var repository = await repositoryService.OpenAsync(work);

        await service.RenameBranchAsync(repository, "feature/foo", "feature/bar");

        Assert.True(LocalBranchExists(work, "feature/bar"));
        Assert.True(RemoteRefExists(remote, "refs/heads/feature/foo"));
        Assert.False(RemoteRefExists(remote, "refs/heads/feature/bar"));
        Assert.Equal(
            "origin/feature/foo",
            RunGitOutput(work, "rev-parse", "--abbrev-ref", "feature/bar@{upstream}").Trim());
    }

    [Fact]
    public async Task AcceptsCompositeBranchNameSupportedByGit()
    {
        var work = CreateRepository();
        RunGit(work, "branch", "source");
        var repositoryService = new GitRepositoryService();
        var service = new GitReferenceService();
        var repository = await repositoryService.OpenAsync(work);

        await service.RenameBranchAsync(repository, "source", "feature/test_branch-v2");

        Assert.False(LocalBranchExists(work, "source"));
        Assert.True(LocalBranchExists(work, "feature/test_branch-v2"));
    }

    [Fact]
    public async Task SameNameIsRejectedBeforeGitMutation()
    {
        var work = CreateRepository();
        RunGit(work, "branch", "feature/same");
        var repositoryService = new GitRepositoryService();
        var service = new GitReferenceService();
        var repository = await repositoryService.OpenAsync(work);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RenameBranchAsync(repository, "feature/same", "feature/same"));

        Assert.Equal("newName", exception.ParamName);
        Assert.True(LocalBranchExists(work, "feature/same"));
    }

    public void Dispose() => TestDirectory.Delete(_temporaryDirectory);

    private string CreateRepository()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var work = Path.Combine(_temporaryDirectory, $"work-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        RunGit(work, "init", "-b", "main");
        RunGit(work, "config", "user.email", "tests@example.invalid");
        RunGit(work, "config", "user.name", "CSharpGit Tests");
        File.WriteAllText(Path.Combine(work, "README.md"), "base\n");
        RunGit(work, "add", "README.md");
        RunGit(work, "commit", "-m", "initial");
        return work;
    }

    private (string Work, string Remote) CreateRepositoryWithRemote()
    {
        var work = CreateRepository();
        var remote = Path.Combine(_temporaryDirectory, $"remote-{Guid.NewGuid():N}.git");
        Directory.CreateDirectory(remote);
        RunGit(remote, "init", "--bare");
        RunGit(work, "remote", "add", "origin", remote);
        RunGit(work, "push", "-u", "origin", "main");
        return (work, remote);
    }

    private static bool LocalBranchExists(string work, string branch) =>
        RunGitExitCode(work, "show-ref", "--verify", $"refs/heads/{branch}") == 0;

    private static bool RemoteRefExists(string remote, string reference) =>
        RunGitExitCode(remote, "show-ref", "--verify", reference) == 0;

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var result = RunGitProcess(workingDirectory, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {result.Error}");
    }

    private static string RunGitOutput(string workingDirectory, params string[] arguments)
    {
        var result = RunGitProcess(workingDirectory, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {result.Error}");
        return result.Output;
    }

    private static int RunGitExitCode(string workingDirectory, params string[] arguments) =>
        RunGitProcess(workingDirectory, arguments).ExitCode;

    private static (int ExitCode, string Output, string Error) RunGitProcess(string workingDirectory, params string[] arguments)
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
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }
}
