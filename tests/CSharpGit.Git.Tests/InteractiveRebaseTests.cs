using System.Diagnostics;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class InteractiveRebaseTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"csharpgit-interactive-rebase-{Guid.NewGuid():N}");

    private readonly string _a;
    private readonly string _b;
    private readonly string _c;
    private readonly string _d;
    private readonly string _e;
    private int _nextFile;

    public InteractiveRebaseTests()
    {
        Directory.CreateDirectory(_root);
        Git("init", "-b", "main");
        Git("config", "user.email", "tests@example.invalid");
        Git("config", "user.name", "CSharpGit Tests");

        _a = Commit("A");
        _b = Commit("B");
        _c = Commit("C");
        _d = Commit("D");
        _e = Commit("E");
    }

    [Fact]
    public async Task ReadFromCommitBuildsPlanStartingWithSelectedCommit()
    {
        var (repository, service) = await CreateServicesAsync();

        var plan = await service.ReadInteractiveRebasePlanFromCommitAsync(repository, _c);

        Assert.Equal(_b, plan.Onto);
        Assert.Equal([_c, _d, _e], plan.Items.Select(item => item.Commit).ToArray());
        Assert.NotNull(plan.SourceSnapshot);
        Assert.Equal(_e, plan.SourceSnapshot.ExpectedHeadCommit);
        Assert.Equal("refs/heads/main", plan.SourceSnapshot.ExpectedHeadReference);
    }

    [Fact]
    public async Task ReadFromHeadBuildsSingleItemPlan()
    {
        var (repository, service) = await CreateServicesAsync();

        var plan = await service.ReadInteractiveRebasePlanFromCommitAsync(repository, _e);

        Assert.Equal(_d, plan.Onto);
        Assert.Single(plan.Items);
        Assert.Equal(_e, plan.Items[0].Commit);
    }

    [Fact]
    public async Task CommitOutsideCurrentHeadAncestryIsRejected()
    {
        Git("switch", "-c", "other", _b);
        var outside = Commit("Outside");
        Git("switch", "main");

        var (repository, service) = await CreateServicesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReadInteractiveRebasePlanFromCommitAsync(repository, outside));

        Assert.Contains("not part of the current branch history", exception.Message);
        Assert.Equal(_e, GitOut("rev-parse", "HEAD"));
    }

    [Fact]
    public async Task DetachedHeadIsRejectedForHistoryEntryPoint()
    {
        Git("checkout", "--detach", _e);
        var (repository, service) = await CreateServicesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReadInteractiveRebasePlanFromCommitAsync(repository, _c));

        Assert.Contains("requires a local branch", exception.Message);
    }

    [Fact]
    public async Task RootCommitIsRejected()
    {
        var (repository, service) = await CreateServicesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReadInteractiveRebasePlanFromCommitAsync(repository, _a));

        Assert.Contains("root commit", exception.Message);
    }

    [Fact]
    public async Task SelectedMergeCommitIsRejected()
    {
        var merge = CreateMergeCommit();
        var (repository, service) = await CreateServicesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReadInteractiveRebasePlanFromCommitAsync(repository, merge));

        Assert.Contains("linear history only", exception.Message);
    }

    [Fact]
    public async Task MergeBetweenSelectedCommitAndHeadIsRejected()
    {
        CreateMergeCommit();
        var (repository, service) = await CreateServicesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReadInteractiveRebasePlanFromCommitAsync(repository, _e));

        Assert.Contains("selected range contains merge commits", exception.Message);
    }

    [Fact]
    public async Task MergeParentBelowSelectedRangeDoesNotBlockLinearPlan()
    {
        var merge = CreateMergeCommit();
        var selected = Commit("After merge C");
        var head = Commit("After merge D");
        var (repository, service) = await CreateServicesAsync();

        var plan = await service.ReadInteractiveRebasePlanFromCommitAsync(repository, selected);

        Assert.Equal(merge, plan.Onto);
        Assert.Equal([selected, head], plan.Items.Select(item => item.Commit).ToArray());
    }

    [Fact]
    public async Task GenericBuildPlanRejectsMergeRange()
    {
        CreateMergeCommit();
        var (repository, service) = await CreateServicesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReadInteractiveRebasePlanAsync(repository, _d));

        Assert.Contains("selected range contains merge commits", exception.Message);
    }

    [Fact]
    public async Task StartRejectsChangedHead()
    {
        var (repository, service) = await CreateServicesAsync();
        var plan = await service.ReadInteractiveRebasePlanAsync(repository, _b);
        var changedHead = Commit("F");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartInteractiveRebaseAsync(repository, plan));

        Assert.Contains("Repository history changed", exception.Message);
        Assert.Equal(changedHead, GitOut("rev-parse", "HEAD"));
    }

    [Fact]
    public async Task StartRejectsDifferentBranchAtSameHead()
    {
        var (repository, service) = await CreateServicesAsync();
        var plan = await service.ReadInteractiveRebasePlanAsync(repository, _b);

        Git("branch", "other");
        Git("switch", "other");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartInteractiveRebaseAsync(repository, plan));

        Assert.Contains("Repository history changed", exception.Message);
        Assert.Equal(_e, GitOut("rev-parse", "HEAD"));
        Assert.Equal("refs/heads/other", GitOut("symbolic-ref", "HEAD"));
    }

    [Fact]
    public async Task StartRejectsAttachedToDetachedAtSameHead()
    {
        var (repository, service) = await CreateServicesAsync();
        var plan = await service.ReadInteractiveRebasePlanAsync(repository, _b);

        Git("checkout", "--detach", _e);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartInteractiveRebaseAsync(repository, plan));

        Assert.Contains("Repository history changed", exception.Message);
        Assert.Equal(_e, GitOut("rev-parse", "HEAD"));
    }

    [Fact]
    public async Task ReorderUsesExistingInteractiveRebaseEngine()
    {
        var (repository, service) = await CreateServicesAsync();
        var plan = await service.ReadInteractiveRebasePlanFromCommitAsync(repository, _c);
        var edited = plan with { Items = [plan.Items[1], plan.Items[0], plan.Items[2]] };

        var result = await service.StartInteractiveRebaseAsync(repository, edited);

        Assert.Equal(RebaseResultKind.Completed, result.Kind);
        Assert.Equal(["D", "C", "E"], SubjectsAfter(_b));
    }

    [Fact]
    public async Task DropUsesExistingInteractiveRebaseEngine()
    {
        var (repository, service) = await CreateServicesAsync();
        var plan = await service.ReadInteractiveRebasePlanFromCommitAsync(repository, _c);
        var items = plan.Items
            .Select(item => item.Commit == _c ? item with { Action = RebaseAction.Drop } : item)
            .ToList();

        var result = await service.StartInteractiveRebaseAsync(repository, plan with { Items = items });

        Assert.Equal(RebaseResultKind.Completed, result.Kind);
        Assert.Equal(["D", "E"], SubjectsAfter(_b));
    }

    [Fact]
    public async Task RewordUsesExistingInteractiveRebaseEngine()
    {
        var (repository, service) = await CreateServicesAsync();
        var plan = await service.ReadInteractiveRebasePlanFromCommitAsync(repository, _c);
        var items = plan.Items.ToList();
        items[0] = items[0] with { Action = RebaseAction.Reword, NewMessage = "C rewritten" };

        var result = await service.StartInteractiveRebaseAsync(repository, plan with { Items = items });

        Assert.Equal(RebaseResultKind.Completed, result.Kind);
        Assert.Equal(["C rewritten", "D", "E"], SubjectsAfter(_b));
    }

    [Theory]
    [InlineData(RebaseAction.Squash)]
    [InlineData(RebaseAction.Fixup)]
    public async Task SquashAndFixupUseExistingInteractiveRebaseEngine(RebaseAction action)
    {
        var (repository, service) = await CreateServicesAsync();
        var plan = await service.ReadInteractiveRebasePlanFromCommitAsync(repository, _c);
        var items = plan.Items.ToList();
        items[1] = items[1] with { Action = action };

        var result = await service.StartInteractiveRebaseAsync(repository, plan with { Items = items });

        Assert.Equal(RebaseResultKind.Completed, result.Kind);
        Assert.Equal(2, SubjectsAfter(_b).Length);
    }

    [Fact]
    public async Task ConflictUsesExistingRebaseOperationLifecycle()
    {
        Git("reset", "--hard", _b);
        var selected = CommitFile("Add shared", "shared.txt", "one\n");
        CommitFile("Modify shared", "shared.txt", "two\n");
        var tail = Commit("Tail");

        var (repository, service) = await CreateServicesAsync();
        var plan = await service.ReadInteractiveRebasePlanFromCommitAsync(repository, selected);
        var edited = plan with { Items = [plan.Items[1], plan.Items[0], plan.Items[2]] };

        var result = await service.StartInteractiveRebaseAsync(repository, edited);

        Assert.Equal(RebaseResultKind.Conflicts, result.Kind);
        Assert.Equal(RepositoryOperation.Rebase, GitOperationDetector.Detect(repository));

        await service.AbortRebaseAsync(repository);
        Assert.Equal(RepositoryOperation.None, GitOperationDetector.Detect(repository));
        Assert.Equal(tail, GitOut("rev-parse", "HEAD"));
    }

    [Fact]
    public async Task DirtyWorkingTreeIsNotAutomaticallyStashedOrDiscarded()
    {
        var (repository, service) = await CreateServicesAsync();
        var plan = await service.ReadInteractiveRebasePlanFromCommitAsync(repository, _c);
        var path = Path.Combine(_root, "commit-1.txt");
        File.AppendAllText(path, "local change\n");

        var result = await service.StartInteractiveRebaseAsync(repository, plan);

        Assert.Equal(RebaseResultKind.Failed, result.Kind);
        Assert.Contains("local change", File.ReadAllText(path));
        Assert.Equal(string.Empty, GitOut("stash", "list", "--format=%H"));
    }

    private string CreateMergeCommit()
    {
        Git("switch", "-c", "side", _e);
        Commit("Side");
        Git("switch", "main");
        Commit("Main after E");
        Git("merge", "--no-ff", "side", "-m", "Merge side");
        return GitOut("rev-parse", "HEAD");
    }

    private string Commit(string subject) =>
        CommitFile(subject, $"commit-{++_nextFile}.txt", $"{subject}\n");

    private string CommitFile(string subject, string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_root, fileName), content);
        Git("add", fileName);
        Git("commit", "-m", subject);
        return GitOut("rev-parse", "HEAD");
    }

    private string[] SubjectsAfter(string commit) =>
        GitOut("log", "--reverse", "--format=%s", $"{commit}..HEAD")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task<(Repository Repository, GitRepositoryWorkflowService Service)> CreateServicesAsync()
    {
        var executor = GitTestServices.CreateExecutor();
        var repository = await new GitRepositoryService(executor).OpenAsync(_root);
        return (repository, new GitRepositoryWorkflowService(executor));
    }

    private void Git(params string[] arguments)
    {
        var result = RunGit(arguments);
        Assert.True(result.Success, $"git {string.Join(' ', arguments)} failed: {result.Error}");
    }

    private string GitOut(params string[] arguments)
    {
        var result = RunGit(arguments);
        Assert.True(result.Success, $"git {string.Join(' ', arguments)} failed: {result.Error}");
        return result.Output.Trim();
    }

    private (bool Success, string Output, string Error) RunGit(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git did not start.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode == 0, output, error);
    }

    public void Dispose() => TestDirectory.Delete(_root);
}
