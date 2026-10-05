using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

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

        await service.CreateStashAsync(
            repository,
            new CreateStashRequest("  release checkpoint  ", StashScope.AllTrackedChanges));

        Assert.EndsWith("release checkpoint", GitOut(_root, "stash", "list", "-1", "--format=%gs"));
    }

    [Fact]
    public async Task WhitespaceMessageDoesNotCreateArtificialMessage()
    {
        File.AppendAllText(Path.Combine(_root, "tracked.txt"), "changed\n");
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(
            repository,
            new CreateStashRequest("   ", StashScope.AllTrackedChanges));

        Assert.StartsWith("WIP on main:", GitOut(_root, "stash", "list", "-1", "--format=%gs"));
    }

    [Fact]
    public async Task CreateDoesNotIncludeUntrackedFilesByDefault()
    {
        File.WriteAllText(Path.Combine(_root, "untracked.txt"), "untracked\n");
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(repository);

        Assert.Equal(string.Empty, GitOut(_root, "stash", "list", "--format=%H"));
        Assert.Contains("?? untracked.txt", GitOut(_root, "status", "--porcelain"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StagedOnlyStashesIndexAndLeavesIndependentUnstagedChanges()
    {
        AddTrackedFile("other.txt", "other\n");
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "index\n");
        Git(_root, "add", "tracked.txt");
        File.WriteAllText(Path.Combine(_root, "other.txt"), "working\n");
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(
            repository,
            new CreateStashRequest(null, StashScope.StagedChangesOnly));

        var status = GitOut(_root, "status", "--porcelain");
        Assert.DoesNotContain("tracked.txt", status, StringComparison.Ordinal);
        Assert.Contains("M other.txt", status, StringComparison.Ordinal);
        Assert.Equal(
            "index",
            GitOut(_root, "show", "stash@{0}:tracked.txt"));
    }

    [Fact]
    public async Task StagedOnlyFailureCanCreateStashBeforeGitReportsCleanupError()
    {
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "B\n");
        Git(_root, "add", "tracked.txt");
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "C\n");
        var (repository, service) = await CreateServicesAsync();

        await Assert.ThrowsAsync<RepositoryOpenException>(() =>
            service.CreateStashAsync(
                repository,
                new CreateStashRequest(
                    null,
                    StashScope.StagedChangesOnly)));

        Assert.Equal(1, StashCount());
        Assert.Equal("B", GitOut(_root, "show", ":tracked.txt"));
        Assert.Equal("C", File.ReadAllText(Path.Combine(_root, "tracked.txt")).Trim());
        Assert.Contains("MM tracked.txt", GitOut(_root, "status", "--porcelain"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StagedOnlyAndIncludeUntrackedIsRejectedBeforeGit()
    {
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "index\n");
        Git(_root, "add", "tracked.txt");
        File.WriteAllText(Path.Combine(_root, "untracked.txt"), "u\n");
        var (repository, service) = await CreateServicesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateStashAsync(
                repository,
                new CreateStashRequest(
                    null,
                    StashScope.StagedChangesOnly,
                    IncludeUntracked: true)));

        Assert.Equal(string.Empty, GitOut(_root, "stash", "list", "--format=%H"));
        Assert.Contains("M  tracked.txt", GitOut(_root, "status", "--porcelain"), StringComparison.Ordinal);
        Assert.Contains("?? untracked.txt", GitOut(_root, "status", "--porcelain"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncludeUntrackedUsesGitIncludeUntrackedButNotIgnoredFiles()
    {
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "*.ignored\n");
        Git(_root, "add", ".gitignore");
        Git(_root, "commit", "-m", "Ignore");
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "changed\n");
        File.WriteAllText(Path.Combine(_root, "saved.txt"), "saved\n");
        File.WriteAllText(Path.Combine(_root, "local.ignored"), "ignored\n");
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(
            repository,
            new CreateStashRequest(
                null,
                StashScope.AllTrackedChanges,
                IncludeUntracked: true));

        Assert.False(File.Exists(Path.Combine(_root, "saved.txt")));
        Assert.True(File.Exists(Path.Combine(_root, "local.ignored")));
        var untrackedTree = GitOut(_root, "ls-tree", "-r", "--name-only", "stash@{0}^3");
        Assert.Contains("saved.txt", untrackedTree, StringComparison.Ordinal);
        Assert.DoesNotContain("local.ignored", untrackedTree, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectedTrackedPathDoesNotStashOtherChanges()
    {
        AddTrackedFile("other.txt", "other\n");
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "selected\n");
        File.WriteAllText(Path.Combine(_root, "other.txt"), "leave-me\n");
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(
            repository,
            Selected(new StashSelectedPath("tracked.txt")));

        var status = GitOut(_root, "status", "--porcelain");
        Assert.DoesNotContain("tracked.txt", status, StringComparison.Ordinal);
        Assert.Contains("M other.txt", status, StringComparison.Ordinal);
        Assert.Equal("selected", GitOut(_root, "show", "stash@{0}:tracked.txt"));
    }

    [Fact]
    public async Task SelectedPathSavesBothStagedAndUnstagedParts()
    {
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "index\n");
        Git(_root, "add", "tracked.txt");
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "working\n");
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(
            repository,
            Selected(new StashSelectedPath("tracked.txt")));

        Assert.Equal(string.Empty, GitOut(_root, "status", "--porcelain"));
        Assert.Equal("index", GitOut(_root, "show", "stash@{0}^2:tracked.txt"));
        Assert.Equal("working", GitOut(_root, "show", "stash@{0}:tracked.txt"));
    }

    [Fact]
    public async Task SelectedUntrackedPathAutomaticallyEnablesIncludeUntracked()
    {
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "notes\n");
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(
            repository,
            Selected(new StashSelectedPath("notes.txt", IsUntracked: true)));

        Assert.False(File.Exists(Path.Combine(_root, "notes.txt")));
        Assert.Contains(
            "notes.txt",
            GitOut(_root, "ls-tree", "-r", "--name-only", "stash@{0}^3"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectedTrackedAndUntrackedPathsAreStashedTogether()
    {
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "changed\n");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "notes\n");
        File.WriteAllText(Path.Combine(_root, "left.txt"), "left\n");
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(
            repository,
            Selected(
                new StashSelectedPath("tracked.txt"),
                new StashSelectedPath("notes.txt", IsUntracked: true)));

        var status = GitOut(_root, "status", "--porcelain");
        Assert.DoesNotContain("tracked.txt", status, StringComparison.Ordinal);
        Assert.DoesNotContain("notes.txt", status, StringComparison.Ordinal);
        Assert.Contains("?? left.txt", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectedRenameUsesCurrentAndOriginalPath()
    {
        Git(_root, "mv", "tracked.txt", "renamed.txt");
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(
            repository,
            Selected(new StashSelectedPath("renamed.txt", "tracked.txt")));

        Assert.Equal(string.Empty, GitOut(_root, "status", "--porcelain"));
        Assert.True(File.Exists(Path.Combine(_root, "tracked.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "renamed.txt")));
        Assert.Contains(
            "renamed.txt",
            GitOut(_root, "ls-tree", "-r", "--name-only", "stash@{0}"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectedPathsUseLiteralPathspecSemantics()
    {
        var literalNames = new List<string> { "a[b].txt" };
        if (!OperatingSystem.IsWindows())
        {
            literalNames.Add("foo*.txt");
            literalNames.Add(":(glob).txt");
        }

        foreach (var name in literalNames)
            File.WriteAllText(Path.Combine(_root, name), "initial\n");
        File.WriteAllText(Path.Combine(_root, "ab.txt"), "initial\n");
        Git(_root, "add", ".");
        Git(_root, "commit", "-m", "Literal paths");

        foreach (var name in literalNames)
            File.WriteAllText(Path.Combine(_root, name), "selected\n");
        File.WriteAllText(Path.Combine(_root, "ab.txt"), "must remain\n");

        var (repository, service) = await CreateServicesAsync();
        await service.CreateStashAsync(
            repository,
            new CreateStashRequest(
                null,
                StashScope.SelectedPaths,
                literalNames.Select(name => new StashSelectedPath(name)).ToArray()));

        var status = GitOut(_root, "status", "--porcelain");
        Assert.Contains("ab.txt", status, StringComparison.Ordinal);
        foreach (var name in literalNames)
            Assert.DoesNotContain(name, status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LargeSelectedPathSetUsesPathspecFileAndCleansTemporaryResource()
    {
        const int count = 500;
        var directory = Path.Combine(_root, "bulk");
        Directory.CreateDirectory(directory);
        var paths = new List<StashSelectedPath>(count);
        for (var index = 0; index < count; index++)
        {
            var name = $"bulk/file-{index:D4}-{"x".PadLeft(58, 'x')}.txt";
            File.WriteAllText(Path.Combine(_root, name.Replace('/', Path.DirectorySeparatorChar)), "initial\n");
            paths.Add(new StashSelectedPath(name));
        }
        Git(_root, "add", "bulk");
        Git(_root, "commit", "-m", "Bulk");
        foreach (var path in paths)
            File.WriteAllText(Path.Combine(_root, path.Path.Replace('/', Path.DirectorySeparatorChar)), "changed\n");

        var before = CurrentPathspecFiles();
        var (repository, service) = await CreateServicesAsync();

        await service.CreateStashAsync(
            repository,
            new CreateStashRequest(null, StashScope.SelectedPaths, paths));

        Assert.Equal(string.Empty, GitOut(_root, "status", "--porcelain"));
        Assert.Empty(CurrentPathspecFiles().Except(before, StringComparer.Ordinal));
    }

    [Fact]
    public async Task TemporaryPathspecFileIsCleanedWhenGitFails()
    {
        Git(_root, "switch", "-c", "other");
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "other\n");
        Git(_root, "add", "tracked.txt");
        Git(_root, "commit", "-m", "Other");
        Git(_root, "switch", "main");
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "main\n");
        Git(_root, "add", "tracked.txt");
        Git(_root, "commit", "-m", "Main");
        Assert.False(RunGit(_root, ["merge", "other"]).Success);

        var before = CurrentPathspecFiles();
        var (repository, service) = await CreateServicesAsync();

        await Assert.ThrowsAsync<RepositoryOpenException>(() =>
            service.CreateStashAsync(
                repository,
                Selected(new StashSelectedPath("tracked.txt"))));

        Assert.Empty(CurrentPathspecFiles().Except(before, StringComparer.Ordinal));
    }

    [Fact]
    public async Task TemporaryPathspecFileIsCleanedWhenCancelled()
    {
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "changed\n");
        var before = CurrentPathspecFiles();
        var (repository, service) = await CreateServicesAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CreateStashAsync(
                repository,
                Selected(new StashSelectedPath("tracked.txt")),
                cancellation.Token));

        Assert.Empty(CurrentPathspecFiles().Except(before, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ApplyUsesStableStashCommitEvenAfterReindex()
    {
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "first\n");
        var (repository, service) = await CreateServicesAsync();
        await service.CreateStashAsync(repository, "first");
        var selected = CurrentStash();

        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "second\n");
        await service.CreateStashAsync(repository, "second");

        await service.ApplyStashAsync(repository, selected);

        Assert.Equal("first", File.ReadAllText(Path.Combine(_root, "tracked.txt")).Trim());
        Assert.Equal(2, StashCount());
    }

    [Fact]
    public async Task PopRefusesStalePositionalStashName()
    {
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "first\n");
        var (repository, service) = await CreateServicesAsync();
        await service.CreateStashAsync(repository, "first");
        var selected = CurrentStash();

        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "second\n");
        await service.CreateStashAsync(repository, "second");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PopStashAsync(repository, selected));

        Assert.Equal(2, StashCount());
    }

    [Fact]
    public async Task DropRefusesStalePositionalStashName()
    {
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "first\n");
        var (repository, service) = await CreateServicesAsync();
        await service.CreateStashAsync(repository, "first");
        var selected = CurrentStash();

        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "second\n");
        await service.CreateStashAsync(repository, "second");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DropStashAsync(repository, selected));

        Assert.Equal(2, StashCount());
    }

    [Fact]
    public async Task PopAndDropRemoveOnlyCurrentStashEntry()
    {
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "pop\n");
        var (repository, service) = await CreateServicesAsync();
        await service.CreateStashAsync(repository, "pop");
        var pop = CurrentStash();

        await service.PopStashAsync(repository, pop);

        Assert.Equal(0, StashCount());
        Git(_root, "restore", "tracked.txt");
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "drop\n");
        await service.CreateStashAsync(repository, "drop");
        var drop = CurrentStash();

        await service.DropStashAsync(repository, drop);

        Assert.Equal(0, StashCount());
        Assert.Equal(string.Empty, GitOut(_root, "status", "--porcelain"));
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

    private CreateStashRequest Selected(params StashSelectedPath[] paths) =>
        new(null, StashScope.SelectedPaths, paths);

    private void AddTrackedFile(string path, string content)
    {
        File.WriteAllText(Path.Combine(_root, path), content);
        Git(_root, "add", path);
        Git(_root, "commit", "-m", $"Add {path}");
    }

    private GitStash CurrentStash()
    {
        var fields = GitOut(_root, "stash", "list", "-1", "--format=%gd%x00%H%x00%gs")
            .Split('\0', 3);
        Assert.Equal(3, fields.Length);
        return new GitStash(fields[0], fields[1], fields[2]);
    }

    private int StashCount()
    {
        var output = GitOut(_root, "stash", "list", "--format=%H");
        return string.IsNullOrWhiteSpace(output)
            ? 0
            : output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static HashSet<string> CurrentPathspecFiles() =>
        Directory.EnumerateFiles(Path.GetTempPath(), "csharpgit-stash-*.pathspec")
            .ToHashSet(StringComparer.Ordinal);

    private async Task<(Repository Repository, GitStashMutationService Service)> CreateServicesAsync()
    {
        var executor = GitTestServices.CreateExecutor();
        var repository = await new GitRepositoryService(executor).OpenAsync(_root);
        return (repository, GitTestServices.CreateStashMutationService(executor));
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
