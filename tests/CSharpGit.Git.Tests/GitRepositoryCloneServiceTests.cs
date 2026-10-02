using System.Diagnostics;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;

namespace CSharpGit.Git.Tests;

public sealed class GitRepositoryCloneServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"csharpgit-clone-{Guid.NewGuid():N}");

    public GitRepositoryCloneServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ClonesRepositoryIntoMissingTargetAndCreatesParent()
    {
        var source = CreateSourceRepository();
        var target = Path.Combine(_root, "nested", "clone");

        await GitTestServices.CreateRepositoryCloneService()
            .CloneAsync(source, target);

        Assert.True(Directory.Exists(Path.Combine(target, ".git")));
        Assert.Equal("tracked", await File.ReadAllTextAsync(Path.Combine(target, "tracked.txt")));
        Assert.Equal(
            Path.GetFullPath(source),
            Path.GetFullPath(RunGit(target, "remote", "get-url", "origin").Trim()));
    }

    [Fact]
    public async Task AllowsExistingEmptyTarget()
    {
        var source = CreateSourceRepository();
        var target = Path.Combine(_root, "empty-target");
        Directory.CreateDirectory(target);

        await GitTestServices.CreateRepositoryCloneService()
            .CloneAsync(source, target);

        Assert.True(Directory.Exists(Path.Combine(target, ".git")));
    }

    [Fact]
    public async Task RejectsNonEmptyTargetWithoutModifyingIt()
    {
        var source = CreateSourceRepository();
        var target = Path.Combine(_root, "non-empty");
        Directory.CreateDirectory(target);
        var existing = Path.Combine(target, "keep.txt");
        await File.WriteAllTextAsync(existing, "keep");

        var exception = await Assert.ThrowsAsync<RepositoryCloneException>(
            () => GitTestServices.CreateRepositoryCloneService().CloneAsync(source, target));

        Assert.Equal(RepositoryCloneFailureKind.TargetNotEmpty, exception.Kind);
        Assert.Equal("keep", await File.ReadAllTextAsync(existing));
        Assert.False(Directory.Exists(Path.Combine(target, ".git")));
    }

    [Fact]
    public async Task RejectsExistingFileAsTarget()
    {
        var source = CreateSourceRepository();
        var target = Path.Combine(_root, "target-file");
        await File.WriteAllTextAsync(target, "file");

        var exception = await Assert.ThrowsAsync<RepositoryCloneException>(
            () => GitTestServices.CreateRepositoryCloneService().CloneAsync(source, target));

        Assert.Equal(RepositoryCloneFailureKind.TargetIsFile, exception.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/path")]
    public async Task RejectsInvalidTargetPath(string target)
    {
        var exception = await Assert.ThrowsAsync<RepositoryCloneException>(
            () => GitTestServices.CreateRepositoryCloneService()
                .CloneAsync("source", target));

        Assert.Equal(RepositoryCloneFailureKind.InvalidTargetPath, exception.Kind);
    }

    [Fact]
    public async Task ReportsMissingGitExecutable()
    {
        var source = CreateSourceRepository();
        var target = Path.Combine(_root, "missing-git");
        var executor = GitTestServices.CreateExecutor(
            new GitCliOptions
            {
                ExecutablePath = Path.Combine(_root, "definitely-missing-git")
            });
        var service = new GitRepositoryCloneService(executor);

        var exception = await Assert.ThrowsAsync<RepositoryCloneException>(
            () => service.CloneAsync(source, target));

        Assert.Equal(RepositoryCloneFailureKind.GitUnavailable, exception.Kind);
    }

    [Fact]
    public async Task PreservesGitFailureDiagnostic()
    {
        var target = Path.Combine(_root, "failed");

        var exception = await Assert.ThrowsAsync<RepositoryCloneException>(
            () => GitTestServices.CreateRepositoryCloneService()
                .CloneAsync(Path.Combine(_root, "missing-source"), target));

        Assert.Equal(RepositoryCloneFailureKind.GitFailed, exception.Kind);
        Assert.NotNull(exception.GitExitCode);
        Assert.False(string.IsNullOrWhiteSpace(exception.GitDiagnostic));
    }

    [Fact]
    public async Task CancellationRemainsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => GitTestServices.CreateRepositoryCloneService()
                .CloneAsync(
                    "source",
                    Path.Combine(_root, "cancelled"),
                    cancellation.Token));
    }

    [Fact]
    public async Task CloneIsRecordedAsUserCommandWithSeparatedArguments()
    {
        var source = CreateSourceRepository();
        var target = Path.Combine(_root, "activity");
        var activity = new GitCommandActivityHistory();
        var executor = GitTestServices.CreateExecutor(activitySink: activity);
        var service = new GitRepositoryCloneService(executor);

        await service.CloneAsync(source, target);

        var command = Assert.Single(activity.GetSnapshot(GitCommandFilter.UserCommands));
        Assert.Equal(GitCommandKind.User, command.CommandKind);
        Assert.Equal(GitCommandStatus.Succeeded, command.Status);
        Assert.Equal("clone", command.Arguments[0]);
        Assert.Contains("--", command.Arguments);
        Assert.Equal(source, command.Arguments[^2]);
        Assert.Equal(Path.GetFullPath(target), command.Arguments[^1]);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string CreateSourceRepository()
    {
        var source = Path.Combine(_root, $"source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        RunGit(source, "init");
        RunGit(source, "config", "user.email", "tests@example.invalid");
        RunGit(source, "config", "user.name", "CSharpGit Tests");
        File.WriteAllText(Path.Combine(source, "tracked.txt"), "tracked");
        RunGit(source, "add", "tracked.txt");
        RunGit(source, "commit", "-m", "Initial");
        return source;
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Git did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git failed: {stderr}{stdout}");
        return stdout;
    }
}
