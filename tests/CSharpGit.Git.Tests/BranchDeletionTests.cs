using System.Diagnostics;
using CSharpGit.Application.Exceptions;

namespace CSharpGit.Git.Tests;

public sealed class BranchDeletionTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-branch-delete-{Guid.NewGuid():N}");

    [Fact]
    public async Task DeletesLocalBranch()
    {
        var work = CreateRepository();
        RunGit(work, "branch", "feature/delete");
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(work);

        await service.DeleteBranchAsync(repository, "feature/delete");

        Assert.False(LocalBranchExists(work, "feature/delete"));
    }

    [Fact]
    public async Task DeletesRemoteBranchWithSlashesFromBareRemote()
    {
        var (work, remote) = CreateRepositoryWithRemote();
        RunGit(work, "switch", "-c", "feature/test/delete");
        RunGit(work, "push", "origin", "feature/test/delete");
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(work);

        await service.DeleteRemoteBranchAsync(repository, "origin", "feature/test/delete");

        Assert.False(RemoteRefExists(remote, "refs/heads/feature/test/delete"));
    }

    [Fact]
    public async Task RemoteDeletionDoesNotDeleteMatchingLocalBranch()
    {
        var (work, remote) = CreateRepositoryWithRemote();
        RunGit(work, "switch", "-c", "feature/keep-local");
        RunGit(work, "push", "origin", "feature/keep-local");
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(work);

        await service.DeleteRemoteBranchAsync(repository, "origin", "feature/keep-local");

        Assert.False(RemoteRefExists(remote, "refs/heads/feature/keep-local"));
        Assert.True(LocalBranchExists(work, "feature/keep-local"));
    }

    [Fact]
    public async Task LocalDeletionKeepsSafeGitSemanticsForUnmergedBranch()
    {
        var work = CreateRepository();
        RunGit(work, "switch", "-c", "feature/unmerged");
        File.WriteAllText(Path.Combine(work, "feature.txt"), "feature\n");
        RunGit(work, "add", "feature.txt");
        RunGit(work, "commit", "-m", "feature work");
        RunGit(work, "switch", "main");
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(work);

        await Assert.ThrowsAsync<RepositoryOpenException>(() => service.DeleteBranchAsync(repository, "feature/unmerged"));

        Assert.True(LocalBranchExists(work, "feature/unmerged"));
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

    private static int RunGitExitCode(string workingDirectory, params string[] arguments) =>
        RunGitProcess(workingDirectory, arguments).ExitCode;

    private static (int ExitCode, string Error) RunGitProcess(string workingDirectory, params string[] arguments)
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
        _ = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, error);
    }
}
