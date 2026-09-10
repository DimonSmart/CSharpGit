using System.Diagnostics;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class CommitActionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-actions-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreatesBranchAtHistoricalCommitAndChecksOutDetached()
    {
        Init();
        var historical = Commit("tracked.txt", "base\n", "base");
        Commit("tracked.txt", "tip\n", "tip");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);

        await service.CreateBranchAsync(repository, "historical", historical, switchToBranch: false);
        Assert.Equal(historical, Git("rev-parse", "historical"));
        Assert.Equal("main", (await service.ReadAsync(repository)).HeadReference);

        await service.CreateBranchAsync(repository, "historical-switch", historical, switchToBranch: true);
        var switched = await service.ReadAsync(repository);
        Assert.Equal("historical-switch", switched.HeadReference);
        Assert.Equal(historical, switched.HeadCommit);

        await service.SwitchBranchAsync(repository, "main");
        await service.CheckoutAsync(repository, historical);
        var detached = await service.ReadAsync(repository);
        Assert.True(detached.IsDetached);
        Assert.Equal(historical, detached.HeadCommit);
    }

    [Fact]
    public async Task CherryPickCompletesAndConflictUsesExistingLifecycle()
    {
        Init();
        Commit("tracked.txt", "base\n", "base");
        Run("switch", "-c", "feature");
        var picked = Commit("picked.txt", "picked\n", "picked");
        Run("switch", "main");
        Commit("main.txt", "main\n", "main");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var success = await service.CherryPickAsync(repository, picked);

        Assert.Equal(ApplyCommitResultKind.Completed, success.Kind);
        Assert.True(File.Exists(Path.Combine(_root, "picked.txt")));
        Assert.Equal(success.HeadCommit, (await service.ReadAsync(repository)).HeadCommit);

        Run("switch", "-c", "conflict-feature");
        Commit("tracked.txt", "feature\n", "feature conflict");
        var conflictCommit = Git("rev-parse", "HEAD");
        Run("switch", "main");
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "main\n");
        Run("add", "tracked.txt");
        Run("commit", "-m", "main conflict");

        var before = Git("rev-parse", "HEAD");
        var conflict = await service.CherryPickAsync(repository, conflictCommit);
        Assert.Equal(ApplyCommitResultKind.Conflicts, conflict.Kind);
        var state = await service.ReadAsync(repository);
        Assert.Equal(RepositoryOperation.CherryPick, state.Operation);
        Assert.True(state.CurrentOperation.CanContinue);
        Assert.True(state.CurrentOperation.CanAbort);
        Assert.True(state.CurrentOperation.CanSkip);

        await service.AbortOperationAsync(repository);
        Assert.Equal(before, Git("rev-parse", "HEAD"));
        Assert.Equal(RepositoryOperation.None, (await service.ReadAsync(repository)).Operation);

        conflict = await service.CherryPickAsync(repository, conflictCommit);
        Assert.Equal(ApplyCommitResultKind.Conflicts, conflict.Kind);
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "resolved\n");
        Run("add", "tracked.txt");
        await service.ContinueOperationAsync(repository);
        Assert.Equal(RepositoryOperation.None, (await service.ReadAsync(repository)).Operation);
    }

    [Fact]
    public async Task CherryPickMergeCommitRequiresAndHonorsMainline()
    {
        Init();
        var baseCommit = Commit("base.txt", "base\n", "base");
        Run("switch", "-c", "feature");
        Commit("feature.txt", "feature\n", "feature");
        Run("switch", "main");
        Commit("main.txt", "main\n", "main");
        Run("merge", "--no-ff", "feature", "-m", "merge");
        var merge = Git("rev-parse", "HEAD");
        Run("switch", "-c", "replay", baseCommit);

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);

        var withoutMainline = await service.CherryPickAsync(repository, merge);
        Assert.Equal(ApplyCommitResultKind.Failed, withoutMainline.Kind);

        var result = await service.CherryPickAsync(repository, merge, 1);
        Assert.Equal(ApplyCommitResultKind.Completed, result.Kind);
        Assert.True(File.Exists(Path.Combine(_root, "feature.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "main.txt")));
    }

    [Fact]
    public async Task RevertCompletesAndConflictCanAbortAndContinue()
    {
        Init();
        Commit("tracked.txt", "base\n", "base");
        var target = Commit("tracked.txt", "change\n", "change");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var success = await service.RevertAsync(repository, target);
        Assert.Equal(ApplyCommitResultKind.Completed, success.Kind);
        Assert.Equal("base\n", File.ReadAllText(Path.Combine(_root, "tracked.txt")));

        Run("reset", "--hard", target);
        Commit("tracked.txt", "later\n", "later");
        var before = Git("rev-parse", "HEAD");

        var conflict = await service.RevertAsync(repository, target);
        Assert.Equal(ApplyCommitResultKind.Conflicts, conflict.Kind);
        Assert.Equal(RepositoryOperation.Revert, (await service.ReadAsync(repository)).Operation);
        await service.AbortOperationAsync(repository);
        Assert.Equal(before, Git("rev-parse", "HEAD"));

        conflict = await service.RevertAsync(repository, target);
        Assert.Equal(ApplyCommitResultKind.Conflicts, conflict.Kind);
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "resolved revert\n");
        Run("add", "tracked.txt");
        await service.ContinueOperationAsync(repository);
        Assert.Equal(RepositoryOperation.None, (await service.ReadAsync(repository)).Operation);
    }

    [Fact]
    public async Task RevertMergeCommitHonorsMainline()
    {
        Init();
        Commit("base.txt", "base\n", "base");
        Run("switch", "-c", "feature");
        Commit("feature.txt", "feature\n", "feature");
        Run("switch", "main");
        Commit("main.txt", "main\n", "main");
        Run("merge", "--no-ff", "feature", "-m", "merge");
        var merge = Git("rev-parse", "HEAD");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var result = await service.RevertAsync(repository, merge, 1);

        Assert.Equal(ApplyCommitResultKind.Completed, result.Kind);
        Assert.False(File.Exists(Path.Combine(_root, "feature.txt")));
        Assert.True(File.Exists(Path.Combine(_root, "main.txt")));
    }

    [Fact]
    public async Task ResetModesFollowGitSemanticsAndHardKeepsUntrackedFiles()
    {
        Init();
        var target = Commit("tracked.txt", "base\n", "base");
        var tip = Commit("tracked.txt", "tip\n", "tip");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);

        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "dirty staged\n");
        Run("add", "tracked.txt");
        await service.ResetAsync(repository, target, ResetMode.Soft);
        Assert.Equal(target, Git("rev-parse", "HEAD"));
        Assert.Equal("dirty staged\n", File.ReadAllText(Path.Combine(_root, "tracked.txt")));
        Assert.NotEmpty(Git("diff", "--cached", "--name-only"));

        Run("reset", "--hard", tip);
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "dirty mixed\n");
        Run("add", "tracked.txt");
        await service.ResetAsync(repository, target, ResetMode.Mixed);
        Assert.Equal(target, Git("rev-parse", "HEAD"));
        Assert.Equal("dirty mixed\n", File.ReadAllText(Path.Combine(_root, "tracked.txt")));
        Assert.Empty(Git("diff", "--cached", "--name-only"));
        Assert.NotEmpty(Git("diff", "--name-only"));

        Run("reset", "--hard", tip);
        File.WriteAllText(Path.Combine(_root, "tracked.txt"), "dirty hard\n");
        File.WriteAllText(Path.Combine(_root, "untracked.txt"), "keep\n");
        await service.ResetAsync(repository, target, ResetMode.Hard);
        Assert.Equal(target, Git("rev-parse", "HEAD"));
        Assert.Equal("base\n", File.ReadAllText(Path.Combine(_root, "tracked.txt")));
        Assert.True(File.Exists(Path.Combine(_root, "untracked.txt")));
    }

    [Fact]
    public async Task ResetIsRejectedForDetachedHead()
    {
        Init();
        var target = Commit("tracked.txt", "base\n", "base");
        Commit("tracked.txt", "tip\n", "tip");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        await service.CheckoutAsync(repository, target);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ResetAsync(repository, target, ResetMode.Mixed));
    }

    private void Init()
    {
        Directory.CreateDirectory(_root);
        Run("init", "-b", "main");
        Run("config", "user.email", "tests@example.invalid");
        Run("config", "user.name", "CSharpGit Tests");
    }

    private string Commit(string path, string contents, string message)
    {
        File.WriteAllText(Path.Combine(_root, path), contents);
        Run("add", "--", path);
        Run("commit", "-m", message);
        return Git("rev-parse", "HEAD");
    }

    private void Run(params string[] arguments)
    {
        var result = RunCore(arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {result.Error}");
    }

    private string Git(params string[] arguments)
    {
        var result = RunCore(arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {result.Error}");
        return result.Output.Trim();
    }

    private (int ExitCode, string Output, string Error) RunCore(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }

    public void Dispose() => TestDirectory.Delete(_root);
}
