using System.Diagnostics;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;
using CSharpGit.Git;

namespace CSharpGit.Git.Tests;

public sealed class GitRepositoryCreationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"csharpgit-create-{Guid.NewGuid():N}");

    public GitRepositoryCreationServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task CreatesPersonalRepositoryInNewDirectoryWithoutStarterContent()
    {
        var target = Path.Combine(_root, "new-project");
        var service = GitTestServices.CreateRepositoryCreationService();

        await service.CreateAsync(target, RepositoryCreationKind.WorkingTree);

        Assert.True(Directory.Exists(Path.Combine(target, ".git")));
        Assert.False(File.Exists(Path.Combine(target, "README.md")));
        Assert.False(File.Exists(Path.Combine(target, ".gitignore")));
        Assert.False(File.Exists(Path.Combine(target, "LICENSE")));
        Assert.Equal("0", RunGit(target, "rev-list", "--count", "--all").Trim());

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(target);
        Assert.Equal(Path.GetFullPath(target), repository.WorkingDirectory);
    }

    [Fact]
    public async Task CreatesMissingParentDirectoriesThroughGit()
    {
        var target = Path.Combine(_root, "one", "two", "three");

        await GitTestServices.CreateRepositoryCreationService()
            .CreateAsync(target, RepositoryCreationKind.WorkingTree);

        Assert.True(Directory.Exists(Path.Combine(target, ".git")));
    }

    [Fact]
    public async Task PersonalRepositoryPreservesExistingFiles()
    {
        var target = Path.Combine(_root, "existing-project");
        Directory.CreateDirectory(target);
        var file = Path.Combine(target, "Program.cs");
        await File.WriteAllTextAsync(file, "Console.WriteLine(42);");

        await GitTestServices.CreateRepositoryCreationService()
            .CreateAsync(target, RepositoryCreationKind.WorkingTree);

        Assert.Equal("Console.WriteLine(42);", await File.ReadAllTextAsync(file));
        Assert.True(Directory.Exists(Path.Combine(target, ".git")));
    }

    [Fact]
    public async Task RejectsExistingRepositoryWithoutReinitializingIt()
    {
        var target = Path.Combine(_root, "repository");
        Directory.CreateDirectory(target);
        RunGit(target, "init");
        var config = Path.Combine(target, ".git", "config");
        var before = await File.ReadAllTextAsync(config);

        var exception = await Assert.ThrowsAsync<RepositoryCreationException>(
            () => GitTestServices.CreateRepositoryCreationService()
                .CreateAsync(target, RepositoryCreationKind.WorkingTree));

        Assert.Equal(RepositoryCreationFailureKind.RepositoryAlreadyExists, exception.Kind);
        Assert.Equal(before, await File.ReadAllTextAsync(config));
    }

    [Fact]
    public async Task AllowsNestedRepositoryInsideAnotherRepository()
    {
        var parent = Path.Combine(_root, "parent");
        var child = Path.Combine(parent, "child");
        Directory.CreateDirectory(child);
        RunGit(parent, "init");

        await GitTestServices.CreateRepositoryCreationService()
            .CreateAsync(child, RepositoryCreationKind.WorkingTree);

        Assert.True(Directory.Exists(Path.Combine(child, ".git")));
        Assert.Equal(
            Path.GetFullPath(child),
            Path.GetFullPath(RunGit(child, "rev-parse", "--show-toplevel").Trim()));
    }

    [Fact]
    public async Task RejectsLinkedWorktreeRootAsExistingRepository()
    {
        var main = Path.Combine(_root, "main");
        var worktree = Path.Combine(_root, "worktree");
        Directory.CreateDirectory(main);
        RunGit(main, "init");
        ConfigureIdentity(main);
        await File.WriteAllTextAsync(Path.Combine(main, "tracked.txt"), "tracked");
        RunGit(main, "add", "tracked.txt");
        RunGit(main, "commit", "-m", "Initial");
        RunGit(main, "worktree", "add", "-b", "feature", worktree);

        var exception = await Assert.ThrowsAsync<RepositoryCreationException>(
            () => GitTestServices.CreateRepositoryCreationService()
                .CreateAsync(worktree, RepositoryCreationKind.WorkingTree));

        Assert.Equal(RepositoryCreationFailureKind.RepositoryAlreadyExists, exception.Kind);
    }

    [Fact]
    public async Task CreatesBareSharedRepository()
    {
        var target = Path.Combine(_root, "central.git");

        await GitTestServices.CreateRepositoryCreationService()
            .CreateAsync(target, RepositoryCreationKind.BareShared);

        Assert.Equal("true", RunGit(target, "rev-parse", "--is-bare-repository").Trim());
        Assert.False(string.IsNullOrWhiteSpace(
            RunGit(target, "config", "--get", "core.sharedRepository")));
    }

    [Fact]
    public async Task CreatesBareSharedRepositoryInExistingEmptyDirectory()
    {
        var target = Path.Combine(_root, "empty-central.git");
        Directory.CreateDirectory(target);

        await GitTestServices.CreateRepositoryCreationService()
            .CreateAsync(target, RepositoryCreationKind.BareShared);

        Assert.Equal("true", RunGit(target, "rev-parse", "--is-bare-repository").Trim());
    }

    [Fact]
    public async Task RejectsBareSharedRepositoryInNonEmptyOrdinaryDirectory()
    {
        var target = Path.Combine(_root, "not-empty");
        Directory.CreateDirectory(target);
        var file = Path.Combine(target, "keep.txt");
        await File.WriteAllTextAsync(file, "keep");

        var exception = await Assert.ThrowsAsync<RepositoryCreationException>(
            () => GitTestServices.CreateRepositoryCreationService()
                .CreateAsync(target, RepositoryCreationKind.BareShared));

        Assert.Equal(RepositoryCreationFailureKind.BareTargetNotEmpty, exception.Kind);
        Assert.Equal("keep", await File.ReadAllTextAsync(file));
        Assert.False(File.Exists(Path.Combine(target, "HEAD")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    public async Task RejectsInvalidOrRelativePath(string path)
    {
        var exception = await Assert.ThrowsAsync<RepositoryCreationException>(
            () => GitTestServices.CreateRepositoryCreationService()
                .CreateAsync(path, RepositoryCreationKind.WorkingTree));

        Assert.Equal(RepositoryCreationFailureKind.InvalidPath, exception.Kind);
    }

    [Fact]
    public async Task RejectsExistingFileAsTarget()
    {
        var target = Path.Combine(_root, "target-file");
        await File.WriteAllTextAsync(target, "file");

        var exception = await Assert.ThrowsAsync<RepositoryCreationException>(
            () => GitTestServices.CreateRepositoryCreationService()
                .CreateAsync(target, RepositoryCreationKind.WorkingTree));

        Assert.Equal(RepositoryCreationFailureKind.TargetIsFile, exception.Kind);
    }

    [Fact]
    public async Task ReportsMissingGitExecutable()
    {
        var target = Path.Combine(_root, "missing-git-target");
        var executor = GitTestServices.CreateExecutor(
            new GitCliOptions
            {
                ExecutablePath = Path.Combine(_root, "definitely-missing-git")
            });
        var service = new GitRepositoryCreationService(executor);

        var exception = await Assert.ThrowsAsync<RepositoryCreationException>(
            () => service.CreateAsync(target, RepositoryCreationKind.WorkingTree));

        Assert.Equal(RepositoryCreationFailureKind.GitUnavailable, exception.Kind);
    }

    [Fact]
    public async Task PreservesGitFailureExitCodeAndDiagnostic()
    {
        var blocker = Path.Combine(_root, "file-parent");
        await File.WriteAllTextAsync(blocker, "file");
        var target = Path.Combine(blocker, "child");

        var exception = await Assert.ThrowsAsync<RepositoryCreationException>(
            () => GitTestServices.CreateRepositoryCreationService()
                .CreateAsync(target, RepositoryCreationKind.WorkingTree));

        Assert.Equal(RepositoryCreationFailureKind.GitFailed, exception.Kind);
        Assert.NotNull(exception.GitExitCode);
        Assert.False(string.IsNullOrWhiteSpace(exception.GitDiagnostic));
    }

    [Fact]
    public async Task CancellationRemainsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => GitTestServices.CreateRepositoryCreationService()
                .CreateAsync(
                    Path.Combine(_root, "cancelled"),
                    RepositoryCreationKind.WorkingTree,
                    cancellation.Token));
    }

    [Fact]
    public async Task ActualInitIsAUserCommandAndProbesRemainInternal()
    {
        var target = Path.Combine(_root, "activity");
        Directory.CreateDirectory(target);
        var activity = new GitCommandActivityHistory();
        var executor = GitTestServices.CreateExecutor(activitySink: activity);
        var service = new GitRepositoryCreationService(executor);

        await service.CreateAsync(target, RepositoryCreationKind.WorkingTree);

        var userCommands = activity.GetSnapshot(GitCommandFilter.UserCommands);
        var init = Assert.Single(userCommands);
        Assert.Equal(GitCommandKind.User, init.CommandKind);
        Assert.Equal(GitCommandStatus.Succeeded, init.Status);
        Assert.Equal(0, init.ExitCode);
        Assert.Equal("init", init.Arguments[0]);
        Assert.Equal(Path.GetFullPath(target), init.Arguments[1]);
        Assert.True(activity.GetSnapshot(GitCommandFilter.AllCommands).Count > userCommands.Count);
    }

    [Fact]
    public async Task NewlyCreatedPersonalRepositorySupportsUnbornStateAndEmptyHistory()
    {
        var target = Path.Combine(_root, "unborn");
        await GitTestServices.CreateRepositoryCreationService()
            .CreateAsync(target, RepositoryCreationKind.WorkingTree);

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(target);
        var state = await GitTestServices.CreateRepositoryStateService().ReadAsync(repository);
        var refresh = await GitTestServices.CreateRepositoryRefreshProbe().ReadAsync(repository);
        var history = GitTestServices.CreateReferenceHistoryService();

        var all = await history.ReadHistoryAsync(
            repository,
            new HistoryQuery(HistoryScope.AllReferences, null, 0, 100));
        var current = await history.ReadHistoryAsync(
            repository,
            new HistoryQuery(HistoryScope.CurrentBranch, null, 0, 100));

        Assert.NotNull(state.HeadReference);
        Assert.Null(state.HeadCommit);
        Assert.False(state.IsDetached);
        Assert.NotNull(refresh);
        Assert.Empty(all.Rows);
        Assert.False(all.HasMore);
        Assert.Empty(current.Rows);
        Assert.False(current.HasMore);
    }

    [Fact]
    public async Task FirstCommitTransitionsUnbornRepositoryToNormalBranchHistory()
    {
        var target = Path.Combine(_root, "first-commit");
        await GitTestServices.CreateRepositoryCreationService()
            .CreateAsync(target, RepositoryCreationKind.WorkingTree);
        ConfigureIdentity(target);
        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(target);
        var workingTree = GitTestServices.CreateWorkingTreeService();
        var stateService = GitTestServices.CreateRepositoryStateService();

        await File.WriteAllTextAsync(Path.Combine(target, "first.txt"), "first");
        var before = await stateService.ReadAsync(repository);
        Assert.Contains(before.Changes, change => change.Path == "first.txt");

        await workingTree.StageAllAsync(repository);
        await workingTree.CommitAsync(repository, "First commit");

        var after = await stateService.ReadAsync(repository);
        var history = await GitTestServices.CreateReferenceHistoryService().ReadHistoryAsync(
            repository,
            new HistoryQuery(HistoryScope.CurrentBranch, null, 0, 100));

        Assert.NotNull(after.HeadCommit);
        Assert.NotNull(after.HeadReference);
        Assert.Contains(after.Refs.LocalBranches, branch => branch.IsCurrent);
        Assert.Contains(history.Rows, row => row.Commit.Subject == "First commit");
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

    private static void ConfigureIdentity(string workingDirectory)
    {
        RunGit(workingDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(workingDirectory, "config", "user.name", "CSharpGit Tests");
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
