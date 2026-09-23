using System.Diagnostics;
using CSharpGit.Application.Exceptions;

namespace CSharpGit.Git.Tests;

public sealed class StashTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-stash-{Guid.NewGuid():N}");

    public StashTests()
    {
        Directory.CreateDirectory(_root);
        Git(_root, "init", "-b", "main");
        Git(_root, "config", "user.email", "tests@example.invalid");
        Git(_root, "config", "user.name", "CSharpGit Tests");
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "initial\n");
        Git(_root, "add", "tracked.txt");
        Git(_root, "commit", "-m", "Initial");
    }

    [Fact]
    public async Task CreateWithoutMessageUsesOrdinaryGitStashSemantics()
    {
        File.AppendAllText(Path.Combine(_root, "tracked.txt"), "changed\n");
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(repository);

        Assert.Equal(string.Empty, GitOut(_root, "status", "--porcelain"));
        Assert.StartsWith("WIP on main:", GitOut(_root, "stash", "list", "-1", "--format=%gs"));
    }

    [Fact]
    public async Task CreateWithMessageTrimsMessageAndPassesItToGit()
    {
        File.AppendAllText(Path.Combine(_root, "tracked.txt"), "changed\n");
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(repository, "  release checkpoint  ");

        Assert.EndsWith("release checkpoint", GitOut(_root, "stash", "list", "-1", "--format=%gs"));
    }

    [Fact]
    public async Task WhitespaceMessageDoesNotCreateArtificialMessage()
    {
        File.AppendAllText(Path.Combine(_root, "tracked.txt"), "changed\n");
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(repository, "   ");

        Assert.StartsWith("WIP on main:", GitOut(_root, "stash", "list", "-1", "--format=%gs"));
    }

    [Fact]
    public async Task CreateDoesNotIncludeUntrackedFiles()
    {
        File.WriteAllText(Path.Combine(_root, "untracked.txt"), "untracked\n");
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(repository);

        Assert.Equal(string.Empty, GitOut(_root, "stash", "list", "--format=%H"));
        Assert.Contains("?? untracked.txt", GitOut(_root, "status", "--porcelain"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitRefusalIsNotMasked()
    {
        Git(_root, "switch", "-c", "other");
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "other\n");
        Git(_root, "add", "tracked.txt");
        Git(_root, "commit", "-m", "Other");

        Git(_root, "switch", "main");
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "main\n");
        Git(_root, "add", "tracked.txt");
        Git(_root, "commit", "-m", "Main");

        var merge = RunGit(_root, ["merge", "other"]);
        Assert.False(merge.Success);

        var (repository, service) = await CreateServicesAsync();

        await Assert.ThrowsAsync<RepositoryOpenException>(
            () => service.CreateStashAsync(repository));

        Assert.Contains("UU tracked.txt", GitOut(_root, "status", "--porcelain"), StringComparison.Ordinal);
    }

    private async Task<(CSharpGit.Domain.Repository Repository, GitRepositoryWorkflowService Service)> CreateServicesAsync()
    {
        var executor = GitTestServices.CreateExecutor();
        var repository = await new GitRepositoryService(executor).OpenAsync(_root);
        return (repository, new GitRepositoryWorkflowService(executor));
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

    private static (bool Success, string Output, string Error) RunGit(string directory, IReadOnlyList<string> arguments)
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
