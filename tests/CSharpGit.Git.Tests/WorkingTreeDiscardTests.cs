using System.Diagnostics;
using CSharpGit.Application;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class WorkingTreeDiscardTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-discard-{Guid.NewGuid():N}");

    [Fact]
    public async Task DiscardModifiedTrackedFileRestoresWorkingTreeFromIndex()
    {
        InitializeRepository(withCommit: true);
        CommitFile("tracked.txt", "index\n", "add tracked");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "working tree\n");

        var (service, repository) = await OpenAsync();
        var change = Assert.Single((await service.ReadAsync(repository)).Changes.Where(change => change.Path == "tracked.txt"));
        var indexBefore = RunGitOutput(_temporaryDirectory, "diff", "--cached", "--binary");

        var results = await ExecuteAsync(service, repository, WorkingTreeDiscard.CreateSelected([change])!);

        Assert.Equal(WorkingTreeDiscardOutcome.Restored, Assert.Single(results).Outcome);
        Assert.Equal("index\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "tracked.txt")));
        Assert.Equal(indexBefore, RunGitOutput(_temporaryDirectory, "diff", "--cached", "--binary"));
    }

    [Fact]
    public async Task DiscardMultipleSelectedTrackedFilesRestoresModifiedAndDeletedFiles()
    {
        InitializeRepository(withCommit: true);
        CommitFile("one.txt", "one\n", "add one");
        CommitFile("two.txt", "two\n", "add two");
        CommitFile("deleted.txt", "deleted\n", "add deleted");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "one.txt"), "one changed\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "two.txt"), "two changed\n");
        File.Delete(Path.Combine(_temporaryDirectory, "deleted.txt"));

        var (service, repository) = await OpenAsync();
        var changes = (await service.ReadAsync(repository)).Changes
            .Where(change => change.Path is "one.txt" or "two.txt" or "deleted.txt")
            .ToArray();

        var results = await ExecuteAsync(service, repository, WorkingTreeDiscard.CreateSelected(changes)!);

        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.Equal(WorkingTreeDiscardOutcome.Restored, result.Outcome));
        Assert.Equal("one\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "one.txt")));
        Assert.Equal("two\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "two.txt")));
        Assert.Equal("deleted\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "deleted.txt")));
    }

    [Fact]
    public async Task DiscardMixedSelectionRestoresTrackedAndDeletesOnlySelectedUntrackedFiles()
    {
        InitializeRepository(withCommit: true);
        CommitFile("tracked.txt", "base\n", "add tracked");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "changed\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "one.tmp"), "one\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "two.tmp"), "two\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "leave.tmp"), "leave\n");

        var (service, repository) = await OpenAsync();
        var changes = (await service.ReadAsync(repository)).Changes;
        var selected = new[]
        {
            changes.Single(change => change.Path == "tracked.txt"),
            changes.Single(change => change.Path == "one.tmp"),
            changes.Single(change => change.Path == "two.tmp")
        };

        var results = await ExecuteAsync(service, repository, WorkingTreeDiscard.CreateSelected(selected)!);

        Assert.Equal(WorkingTreeDiscardOutcome.Restored, results.Single(result => result.Path == "tracked.txt").Outcome);
        Assert.Equal(WorkingTreeDiscardOutcome.Deleted, results.Single(result => result.Path == "one.tmp").Outcome);
        Assert.Equal(WorkingTreeDiscardOutcome.Deleted, results.Single(result => result.Path == "two.tmp").Outcome);
        Assert.Equal("base\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "tracked.txt")));
        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "one.tmp")));
        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "two.tmp")));
        Assert.True(File.Exists(Path.Combine(_temporaryDirectory, "leave.tmp")));
    }

    [Fact]
    public async Task DiscardAllUsesConfirmationSnapshotAndDoesNotTouchLaterChanges()
    {
        InitializeRepository(withCommit: true);
        CommitFile("tracked.txt", "base\n", "add tracked");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "changed\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "confirmed.tmp"), "confirmed\n");

        var (service, repository) = await OpenAsync();
        var request = WorkingTreeDiscard.CreateAll((await service.ReadAsync(repository)).Changes)!;
        File.WriteAllText(Path.Combine(_temporaryDirectory, "later.tmp"), "later\n");

        var results = await ExecuteAsync(service, repository, request);

        Assert.Equal(2, results.Count);
        Assert.Equal("base\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "tracked.txt")));
        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "confirmed.tmp")));
        Assert.True(File.Exists(Path.Combine(_temporaryDirectory, "later.tmp")));
    }

    [Fact]
    public async Task DiscardPreservesStagedVersionWhenFileHasStagedAndUnstagedChanges()
    {
        InitializeRepository(withCommit: true);
        CommitFile("Foo.cs", "A\n", "base");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "Foo.cs"), "B\n");
        RunGit(_temporaryDirectory, "add", "--", "Foo.cs");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "Foo.cs"), "C\n");

        var cachedDiffBefore = RunGitOutput(_temporaryDirectory, "diff", "--cached", "--binary");
        var indexBefore = RunGitOutput(_temporaryDirectory, "show", ":Foo.cs");
        var (service, repository) = await OpenAsync();
        var change = Assert.Single((await service.ReadAsync(repository)).Changes.Where(change => change.Path == "Foo.cs"));
        Assert.True(change.IsStaged && change.IsUnstaged);

        await ExecuteAsync(service, repository, WorkingTreeDiscard.CreateSelected([change])!);

        Assert.Equal("B\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "Foo.cs")));
        Assert.Equal(indexBefore, RunGitOutput(_temporaryDirectory, "show", ":Foo.cs"));
        Assert.Equal(cachedDiffBefore, RunGitOutput(_temporaryDirectory, "diff", "--cached", "--binary"));
    }

    [Fact]
    public async Task DiscardAllPreservesAllStagedChangesForMultipleFiles()
    {
        InitializeRepository(withCommit: true);
        CommitFile("one.cs", "one A\n", "add one");
        CommitFile("two.cs", "two A\n", "add two");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "one.cs"), "one B\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "two.cs"), "two B\n");
        RunGit(_temporaryDirectory, "add", "--", "one.cs", "two.cs");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "one.cs"), "one C\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "two.cs"), "two C\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "untracked.tmp"), "temporary\n");

        var cachedDiffBefore = RunGitOutput(_temporaryDirectory, "diff", "--cached", "--binary");
        var (service, repository) = await OpenAsync();
        var request = WorkingTreeDiscard.CreateAll((await service.ReadAsync(repository)).Changes)!;

        var results = await ExecuteAsync(service, repository, request);

        Assert.Equal(3, results.Count);
        Assert.Equal("one B\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "one.cs")));
        Assert.Equal("two B\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "two.cs")));
        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "untracked.tmp")));
        Assert.Equal(cachedDiffBefore, RunGitOutput(_temporaryDirectory, "diff", "--cached", "--binary"));
    }

    [Fact]
    public async Task DiscardTrackedFileWorksWithoutHeadAndPreservesIndex()
    {
        InitializeRepository(withCommit: false);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "initial.cs"), "index version\n");
        RunGit(_temporaryDirectory, "add", "--", "initial.cs");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "initial.cs"), "working version\n");
        var cachedDiffBefore = RunGitOutput(_temporaryDirectory, "diff", "--cached", "--binary");
        Assert.NotEqual(0, RunGitExitCode(_temporaryDirectory, "rev-parse", "--verify", "HEAD"));

        var (service, repository) = await OpenAsync();
        var change = Assert.Single((await service.ReadAsync(repository)).Changes.Where(change => change.Path == "initial.cs"));
        Assert.True(change.IsStaged && change.IsUnstaged);

        await ExecuteAsync(service, repository, WorkingTreeDiscard.CreateSelected([change])!);

        Assert.Equal("index version\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "initial.cs")));
        Assert.Equal(cachedDiffBefore, RunGitOutput(_temporaryDirectory, "diff", "--cached", "--binary"));
    }

    [Fact]
    public async Task DiscardHandlesSpacesAndUnicodePaths()
    {
        InitializeRepository(withCommit: true);
        CommitFile("file with spaces.txt", "spaces\n", "add spaced path");
        CommitFile("файл.txt", "unicode\n", "add unicode path");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "file with spaces.txt"), "changed spaces\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "файл.txt"), "changed unicode\n");

        var (service, repository) = await OpenAsync();
        var changes = (await service.ReadAsync(repository)).Changes
            .Where(change => change.Path is "file with spaces.txt" or "файл.txt")
            .ToArray();

        var results = await ExecuteAsync(service, repository, WorkingTreeDiscard.CreateSelected(changes)!);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Equal(WorkingTreeDiscardOutcome.Restored, result.Outcome));
        Assert.Equal("spaces\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "file with spaces.txt")));
        Assert.Equal("unicode\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "файл.txt")));
    }

    [Fact]
    public async Task DiscardLogicalRenameRemovesDestinationAndRestoresIndexPath()
    {
        InitializeRepository(withCommit: true);
        CommitFile("old name.txt", "original\n", "add old path");
        File.Move(
            Path.Combine(_temporaryDirectory, "old name.txt"),
            Path.Combine(_temporaryDirectory, "new name.txt"));

        var (service, repository) = await OpenAsync();
        var rename = new WorkingTreeChange("new name.txt", ' ', 'R', "old name.txt");

        var result = Assert.Single(await ExecuteAsync(
            service,
            repository,
            WorkingTreeDiscard.CreateSelected([rename])!));

        Assert.Equal(WorkingTreeDiscardOutcome.Restored, result.Outcome);
        Assert.True(File.Exists(Path.Combine(_temporaryDirectory, "old name.txt")));
        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "new name.txt")));
        Assert.Equal("original\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "old name.txt")));
    }

    [Fact]
    public async Task FailureRestoringOneTrackedFileDoesNotUndoSuccessfulFiles()
    {
        InitializeRepository(withCommit: true);
        CommitFile("good.txt", "base\n", "add good");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "good.txt"), "changed\n");

        var (service, repository) = await OpenAsync();
        var good = Assert.Single((await service.ReadAsync(repository)).Changes.Where(change => change.Path == "good.txt"));
        var missing = new WorkingTreeChange("missing.txt", ' ', 'M');
        var request = WorkingTreeDiscard.CreateSelected([good, missing])!;

        var results = await ExecuteAsync(service, repository, request);

        Assert.Equal(WorkingTreeDiscardOutcome.Restored, results[0].Outcome);
        Assert.Equal(WorkingTreeDiscardOutcome.Failed, results[1].Outcome);
        Assert.Equal("base\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "good.txt")));
        Assert.Contains("missing.txt", WorkingTreeDiscard.FormatFailures(results));
    }

    [Fact]
    public async Task FailureDeletingOneUntrackedFileIsReportedAndOtherItemsContinue()
    {
        InitializeRepository(withCommit: true);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "good.tmp"), "good\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "locked.tmp"), "locked\n");
        var (_, repository) = await OpenAsync();
        var good = new WorkingTreeChange("good.tmp", '?', '?');
        var locked = new WorkingTreeChange("locked.tmp", '?', '?');
        var request = WorkingTreeDiscard.CreateSelected([locked, good])!;

        var results = await WorkingTreeDiscard.ExecuteAsync(
            repository,
            request,
            (change, _) =>
            {
                if (change.Path == "locked.tmp") throw new IOException("simulated filesystem error");
                File.Delete(Path.Combine(_temporaryDirectory, change.Path));
                return Task.CompletedTask;
            });

        Assert.Equal(WorkingTreeDiscardOutcome.Failed, results[0].Outcome);
        Assert.Equal(WorkingTreeDiscardOutcome.Deleted, results[1].Outcome);
        Assert.True(File.Exists(Path.Combine(_temporaryDirectory, "locked.tmp")));
        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "good.tmp")));
        var message = WorkingTreeDiscard.FormatFailures(results);
        Assert.Contains("locked.tmp", message);
        Assert.Contains("simulated filesystem error", message);
    }

    [Fact]
    public async Task UntrackedDirectoryIsNotRecursivelyDeletedAndIsReportedAsFailure()
    {
        InitializeRepository(withCommit: true);
        Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "folder"));
        File.WriteAllText(Path.Combine(_temporaryDirectory, "folder", "keep.txt"), "keep\n");

        var (service, repository) = await OpenAsync();
        var directory = new WorkingTreeChange("folder", '?', '?');
        var result = Assert.Single(await ExecuteAsync(
            service,
            repository,
            WorkingTreeDiscard.CreateSelected([directory])!));

        Assert.Equal(WorkingTreeDiscardOutcome.Failed, result.Outcome);
        Assert.Contains("not removed recursively", result.ErrorMessage);
        Assert.True(File.Exists(Path.Combine(_temporaryDirectory, "folder", "keep.txt")));
    }

    [Fact]
    public async Task ConflictPathsAreNotEligibleAndAreNeverSentToDiscardBackend()
    {
        InitializeRepository(withCommit: true);
        var (_, repository) = await OpenAsync();
        var conflict = new WorkingTreeChange("conflict.txt", 'U', 'U');
        var ordinary = new WorkingTreeChange("ordinary.txt", ' ', 'M');
        var calls = 0;

        Assert.False(WorkingTreeDiscard.CanDiscardSelected([ordinary, conflict]));
        Assert.Null(WorkingTreeDiscard.CreateSelected([ordinary, conflict]));
        Assert.Null(WorkingTreeDiscard.CreateAll([conflict]));

        var request = new WorkingTreeDiscardRequest(WorkingTreeDiscardScope.Selected, [conflict]);
        var result = Assert.Single(await WorkingTreeDiscard.ExecuteAsync(
            repository,
            request,
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            }));

        Assert.Equal(0, calls);
        Assert.Equal(WorkingTreeDiscardOutcome.Failed, result.Outcome);
        Assert.Contains("conflict workflow", result.ErrorMessage);
    }

    public void Dispose() => TestDirectory.Delete(_temporaryDirectory);

    private async Task<(GitCliRepositoryService Service, Repository Repository)> OpenAsync()
    {
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        return (service, repository);
    }

    private static Task<IReadOnlyList<WorkingTreeDiscardResult>> ExecuteAsync(
        GitCliRepositoryService service,
        Repository repository,
        WorkingTreeDiscardRequest request) =>
        WorkingTreeDiscard.ExecuteAsync(
            repository,
            request,
            (change, cancellationToken) => service.DiscardFileAsync(repository, change, cancellationToken));

    private void InitializeRepository(bool withCommit)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "CSharpGit Tests");
        RunGit(_temporaryDirectory, "config", "core.autocrlf", "false");
        if (withCommit) CommitFile("root.txt", "root\n", "initial");
    }

    private void CommitFile(string name, string contents, string message)
    {
        File.WriteAllText(Path.Combine(_temporaryDirectory, name), contents);
        RunGit(_temporaryDirectory, "add", "--", name);
        RunGit(_temporaryDirectory, "commit", "-m", message);
    }

    private static void RunGit(string directory, params string[] arguments)
    {
        var exitCode = RunGitExitCode(directory, arguments);
        Assert.Equal(0, exitCode);
    }

    private static int RunGitExitCode(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string RunGitOutput(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }
}
