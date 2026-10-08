using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class PullIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"csharpgit-pull-{Guid.NewGuid():N}");
    private readonly string _origin;
    private readonly string _local;
    private readonly string _actor;

    public PullIntegrationTests()
    {
        _origin = Path.Combine(_root, "remote.git");
        _local = Path.Combine(_root, "local");
        _actor = Path.Combine(_root, "actor");
        Directory.CreateDirectory(_root);
        Git(_root, "init", "--bare", _origin);
        Directory.CreateDirectory(_local);
        Git(_local, "init", "-b", "main");
        ConfigureAuthor(_local);
        File.WriteAllText(Path.Combine(_local, "shared.txt"), "base\n");
        Git(_local, "add", ".");
        Git(_local, "commit", "-m", "Base");
        Git(_local, "remote", "add", "origin", _origin);
        Git(_local, "push", "-u", "origin", "main");
        Git(_root, "clone", "--branch", "main", _origin, _actor);
        ConfigureAuthor(_actor);
    }

    [Theory]
    [InlineData(PullStrategy.GitConfiguration)]
    [InlineData(PullStrategy.Merge)]
    [InlineData(PullStrategy.Rebase)]
    [InlineData(PullStrategy.FastForwardOnly)]
    public async Task AllModesSupportUpToDateAndFastForward(PullStrategy strategy)
    {
        Git(_local, "config", "pull.rebase", "false");
        var (repository, service) = await OpenAsync();
        var upToDate = await service.PullAsync(repository, new PullOptions(strategy));
        Assert.Equal(PullOutcome.Completed, upToDate.Outcome);
        Assert.Equal(PullCompletionKind.UpToDate, upToDate.Completion);

        Commit(_actor, "remote.txt", "remote\n", "Remote update");
        Git(_actor, "push", "origin", "main");
        var result = await service.PullAsync(repository, new PullOptions(strategy));
        Assert.Equal(PullOutcome.Completed, result.Outcome);
        Assert.Equal(PullCompletionKind.FastForward, result.Completion);
        Assert.Equal(GitOut(_actor, "rev-parse", "HEAD"), GitOut(_local, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task MergeProducesMergeCommitWithoutOpeningEditor()
    {
        Commit(_local, "local.txt", "local\n", "Local update");
        Commit(_actor, "remote.txt", "remote\n", "Remote update");
        Git(_actor, "push", "origin", "main");
        Git(_local, "config", "pull.rebase", "true");
        var (repository, service) = await OpenAsync();

        var result = await service.PullAsync(repository, new PullOptions(PullStrategy.Merge));
        Assert.Equal(PullOutcome.Completed, result.Outcome);
        Assert.Equal(PullCompletionKind.MergeCommit, result.Completion);
        Assert.Equal(2, GitOut(_local, "show", "-s", "--format=%P", "HEAD")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task RebaseOverridesMergeConfigurationAndReplaysLocalWork()
    {
        Commit(_local, "local.txt", "local\n", "Local update");
        Commit(_actor, "remote.txt", "remote\n", "Remote update");
        Git(_actor, "push", "origin", "main");
        Git(_local, "config", "pull.rebase", "false");
        var (repository, service) = await OpenAsync();

        var result = await service.PullAsync(repository, new PullOptions(PullStrategy.Rebase));
        Assert.Equal(PullOutcome.Completed, result.Outcome);
        Assert.Equal(RepositoryOperation.None, result.ActiveOperation);
        Assert.Equal("local\n", File.ReadAllText(Path.Combine(_local, "local.txt")));
        Assert.Equal("remote\n", File.ReadAllText(Path.Combine(_local, "remote.txt")));
        Assert.Equal(GitOut(_local, "rev-parse", "origin/main"),
            GitOut(_local, "rev-parse", "HEAD~"));
    }

    [Fact]
    public async Task FastForwardOnlyRefusesDivergenceWithoutMovingLocalHead()
    {
        Commit(_local, "local.txt", "local\n", "Local update");
        Commit(_actor, "remote.txt", "remote\n", "Remote update");
        Git(_actor, "push", "origin", "main");
        var originalHead = GitOut(_local, "rev-parse", "HEAD");
        var (repository, service) = await OpenAsync();

        var result = await service.PullAsync(repository, new PullOptions(PullStrategy.FastForwardOnly));
        Assert.Equal(PullOutcome.Refused, result.Outcome);
        Assert.Equal(originalHead, GitOut(_local, "rev-parse", "HEAD"));
        Assert.Equal(GitOut(_actor, "rev-parse", "HEAD"),
            GitOut(_local, "rev-parse", "origin/main"));
        Assert.Equal(RepositoryOperation.None, result.ActiveOperation);
    }

    [Theory]
    [InlineData(PullStrategy.Merge, RepositoryOperation.Merge)]
    [InlineData(PullStrategy.Rebase, RepositoryOperation.Rebase)]
    public async Task ConflictsLeaveNativeOperationActive(
        PullStrategy strategy, RepositoryOperation expected)
    {
        File.WriteAllText(Path.Combine(_local, "shared.txt"), "local\n");
        Git(_local, "add", "shared.txt");
        Git(_local, "commit", "-m", "Local conflict");
        File.WriteAllText(Path.Combine(_actor, "shared.txt"), "remote\n");
        Git(_actor, "add", "shared.txt");
        Git(_actor, "commit", "-m", "Remote conflict");
        Git(_actor, "push", "origin", "main");
        var (repository, service) = await OpenAsync();

        var result = await service.PullAsync(repository, new PullOptions(strategy));
        Assert.Equal(PullOutcome.NeedsAttention, result.Outcome);
        Assert.Equal(expected, result.ActiveOperation);
        Assert.True(result.HasUnmergedPaths);
    }

    [Fact]
    public async Task AutostashApplyConflictsDoNotInventMergeOrRebaseContinuation()
    {
        File.WriteAllText(Path.Combine(_local, "shared.txt"), "uncommitted local\n");
        File.WriteAllText(Path.Combine(_actor, "shared.txt"), "committed remote\n");
        Git(_actor, "add", "shared.txt");
        Git(_actor, "commit", "-m", "Remote conflict");
        Git(_actor, "push", "origin", "main");
        var (repository, service) = await OpenAsync();

        var result = await service.PullAsync(
            repository, new PullOptions(PullStrategy.Merge, ForceAutoStash: true));
        Assert.Equal(PullOutcome.NeedsAttention, result.Outcome);
        Assert.Equal(RepositoryOperation.None, result.ActiveOperation);
        Assert.True(result.HasUnmergedPaths);

        var actualState = await GitTestServices.CreateRepositoryStateService()
            .ReadAsync(repository);
        Assert.Equal(RepositoryOperation.None, actualState.Operation);
        Assert.NotEmpty(actualState.CurrentOperation.Conflicts);
        Assert.All(actualState.CurrentOperation.Conflicts, conflict =>
            Assert.False(conflict.IsResolved));
        Assert.False(actualState.CurrentOperation.CanContinue);
        Assert.False(actualState.CurrentOperation.CanAbort);
        Assert.False(actualState.CurrentOperation.CanSkip);

        var retry = await service.PullAsync(repository, new PullOptions(PullStrategy.Merge));
        Assert.Equal(PullOutcome.NeedsAttention, retry.Outcome);
        Assert.Equal(RepositoryOperation.None, retry.ActiveOperation);
    }

    [Theory]
    [InlineData(PullStrategy.Merge)]
    [InlineData(PullStrategy.Rebase)]
    public async Task ExplicitIntegrationOverridesPullFfOnlyAndBranchRebase(PullStrategy strategy)
    {
        Commit(_local, "local.txt", "local\n", "Local update");
        Commit(_actor, "remote.txt", "remote\n", "Remote update");
        Git(_actor, "push", "origin", "main");
        Git(_local, "config", "pull.ff", "only");
        Git(_local, "config", "branch.main.rebase", strategy == PullStrategy.Merge ? "true" : "false");
        var (repository, service) = await OpenAsync();

        var result = await service.PullAsync(repository, new PullOptions(strategy));
        Assert.Equal(PullOutcome.Completed, result.Outcome);
        Assert.Equal(RepositoryOperation.None, result.ActiveOperation);
        Assert.Equal("local\n", File.ReadAllText(Path.Combine(_local, "local.txt")));
        Assert.Equal("remote\n", File.ReadAllText(Path.Combine(_local, "remote.txt")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GitConfigurationHonorsPerBranchRebase(bool useRebase)
    {
        Commit(_local, "local.txt", "local\n", "Local update");
        Commit(_actor, "remote.txt", "remote\n", "Remote update");
        Git(_actor, "push", "origin", "main");
        Git(_local, "config", "pull.rebase", useRebase ? "false" : "true");
        Git(_local, "config", "branch.main.rebase", useRebase ? "true" : "false");
        var (repository, service) = await OpenAsync();

        var result = await service.PullAsync(
            repository, new PullOptions(PullStrategy.GitConfiguration));
        Assert.Equal(PullOutcome.Completed, result.Outcome);
        var parents = GitOut(_local, "show", "-s", "--format=%P", "HEAD")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(useRebase ? 1 : 2, parents.Length);
    }

    [Fact]
    public async Task FetchFailureWithPreviouslyDivergedTrackingRefIsNotRefusal()
    {
        Commit(_local, "local.txt", "local\n", "Local update");
        Commit(_actor, "remote.txt", "remote\n", "Remote update");
        Git(_actor, "push", "origin", "main");
        Git(_local, "fetch", "origin"); // Cached origin/main is already diverged.
        Git(_local, "remote", "set-url", "origin", Path.Combine(_root, "missing-remote.git"));
        var (repository, service) = await OpenAsync();

        var result = await service.PullAsync(
            repository, new PullOptions(PullStrategy.FastForwardOnly));
        Assert.Equal(PullOutcome.Failed, result.Outcome);
        Assert.Equal(RepositoryOperation.None, result.ActiveOperation);
    }

    [Fact]
    public async Task UnrelatedFailureIsNotMisclassifiedAsFastForwardDivergence()
    {
        Commit(_local, "local.txt", "local\n", "Local update");
        Commit(_actor, "remote.txt", "remote\n", "Remote update");
        Git(_actor, "push", "origin", "main");
        Git(_local, "remote", "set-url", "origin", Path.Combine(_root, "missing-remote.git"));
        var (repository, service) = await OpenAsync();

        var result = await service.PullAsync(repository, new PullOptions(PullStrategy.FastForwardOnly));
        Assert.Equal(PullOutcome.Failed, result.Outcome);
        Assert.Equal(RepositoryOperation.None, result.ActiveOperation);
    }

    [Fact]
    public async Task PruneRemovesDeletedRemoteTrackingRefs()
    {
        Git(_actor, "switch", "-c", "to-remove");
        Commit(_actor, "ephemeral.txt", "ephemeral\n", "Ephemeral");
        Git(_actor, "push", "-u", "origin", "to-remove");
        Git(_local, "fetch", "origin");
        Assert.True(TryGit(_local, "show-ref", "--verify", "refs/remotes/origin/to-remove"));
        Git(_actor, "push", "origin", "--delete", "to-remove");
        var (repository, service) = await OpenAsync();

        var result = await service.PullAsync(repository, new PullOptions(PullStrategy.FastForwardOnly));
        Assert.Equal(PullOutcome.Completed, result.Outcome);
        Assert.False(TryGit(_local, "show-ref", "--verify", "refs/remotes/origin/to-remove"));
    }

    [Fact]
    public async Task PullRequiresLocalBranchWithUpstream()
    {
        var (repository, service) = await OpenAsync();
        Git(_local, "switch", "-c", "untracked");
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PullAsync(repository, new PullOptions(PullStrategy.Merge)));
        Assert.Contains("no configured upstream", missing.Message, StringComparison.Ordinal);

        Git(_local, "checkout", "--detach", "HEAD");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PullAsync(repository, new PullOptions(PullStrategy.Rebase)));
    }

    [Fact]
    public async Task InvalidStrategyFailsBeforeLaunchingPull()
    {
        var (repository, service) = await OpenAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.PullAsync(repository, new PullOptions((PullStrategy)734)));
    }

    private async Task<(Repository Repository, GitRepositorySyncService Service)> OpenAsync()
    {
        var executor = GitTestServices.CreateExecutor();
        var repository = await new GitRepositoryService(executor).OpenAsync(_local);
        return (repository, new GitRepositorySyncService(executor));
    }

    private static void ConfigureAuthor(string directory)
    {
        Git(directory, "config", "user.email", "test@example.invalid");
        Git(directory, "config", "user.name", "Test");
        Git(directory, "config", "commit.gpgsign", "false");
    }

    private static void Commit(string directory, string path, string content, string message)
    {
        File.WriteAllText(Path.Combine(directory, path), content);
        Git(directory, "add", path);
        Git(directory, "commit", "-m", message);
    }

    private static void Git(string directory, params string[] arguments) =>
        TestGitRunner.Run(directory, arguments);
    private static string GitOut(string directory, params string[] arguments) =>
        TestGitRunner.Run(directory, arguments);
    private static bool TryGit(string directory, params string[] arguments) =>
        TestGitRunner.TryRun(directory, arguments).Success;

    public void Dispose() => TestDirectory.Delete(_root);
}
