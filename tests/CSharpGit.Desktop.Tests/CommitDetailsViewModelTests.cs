using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSharpGit.Desktop.Tests;

public sealed class CommitDetailsViewModelTests
{
    [Fact]
    public async Task NewerStashWinsWhenOlderDetailsCompleteLater()
    {
        var repository = Repository();
        var stashService = new ControlledStashService();
        using var viewModel = new CommitDetailsViewModel(
            new UnusedHistoryService(),
            stashService,
            NullLogger<CommitDetailsViewModel>.Instance);
        viewModel.Attach(new FakeContext(repository));

        var first = Stash("stash@{0}", "a");
        var second = Stash("stash@{1}", "b");

        var firstLoad = viewModel.ShowStashAsync(first);
        await stashService.WaitUntilStartedAsync(first.Commit);

        var secondLoad = viewModel.ShowStashAsync(second);
        await stashService.WaitUntilStartedAsync(second.Commit);
        stashService.Complete(second, Details(second));
        await secondLoad;

        stashService.Complete(first, Details(first));
        await firstLoad;

        Assert.Equal(second.Commit, viewModel.SelectedObjectCommit);
        Assert.Equal(second.Commit, viewModel.SelectedStashDetails?.Stash.Commit);
    }

    [Fact]
    public async Task StaleStashCompletionCannotOverwriteCommitMode()
    {
        var repository = Repository();
        var stashService = new ControlledStashService();
        using var viewModel = new CommitDetailsViewModel(
            new UnusedHistoryService(),
            stashService,
            NullLogger<CommitDetailsViewModel>.Instance);
        viewModel.Attach(new FakeContext(repository));

        var stash = Stash("stash@{0}", "a");
        var load = viewModel.ShowStashAsync(stash);
        await stashService.WaitUntilStartedAsync(stash.Commit);

        var commit = Commit("commit-b");
        viewModel.ShowCommit(new HistoryRow(commit, new CommitTopology(0, [])));

        stashService.Complete(stash, Details(stash));
        await load;

        Assert.Equal(commit.Hash, viewModel.SelectedObjectCommit);
        Assert.False(viewModel.HasSelectedStash);
        Assert.Null(viewModel.SelectedStashDetails);
    }

    private static Repository Repository() =>
        new("/repo", "/repo", "/repo/.git", false);

    private static GitStash Stash(string name, string commit) =>
        new(name, commit, name);

    private static CommitHistoryItem Commit(string hash) =>
        new(hash, [], hash, hash, "author", DateTimeOffset.UnixEpoch, []);

    private static StashDetails Details(GitStash stash) =>
        new(stash, Commit(stash.Commit), "base", "index", null, []);

    private sealed class FakeContext(Repository repository) : ICommitDetailsRepositoryContext
    {
        public Repository? Repository { get; } = repository;
        public string? Error { get; private set; }

        public void ReportCommitDetailsError(string message) => Error = message;
    }

    private sealed class ControlledStashService : IStashService
    {
        private readonly Dictionary<string, TaskCompletionSource<bool>> _started = [];
        private readonly Dictionary<string, TaskCompletionSource<StashDetails>> _results = [];

        public Task<StashDetails> ReadAsync(
            Repository repository,
            GitStash stash,
            CancellationToken cancellationToken = default)
        {
            Started(stash.Commit).TrySetResult(true);
            return Result(stash.Commit).Task;
        }

        public Task WaitUntilStartedAsync(string commit) => Started(commit).Task;

        public void Complete(GitStash stash, StashDetails details) =>
            Result(stash.Commit).TrySetResult(details);

        private TaskCompletionSource<bool> Started(string commit)
        {
            if (_started.TryGetValue(commit, out var source)) return source;
            source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _started[commit] = source;
            return source;
        }

        private TaskCompletionSource<StashDetails> Result(string commit)
        {
            if (_results.TryGetValue(commit, out var source)) return source;
            source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _results[commit] = source;
            return source;
        }
    }

    private sealed class UnusedHistoryService : IHistoryService
    {
        public Task<HistoryPage> ReadHistoryAsync(
            Repository repository,
            HistoryQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HistoryPage> ReadHistoryThroughCommitAsync(
            Repository repository,
            HistoryScope scope,
            string targetHash,
            int trailingCount = 100,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HistoryPage> ReadHistoryThroughCommitAsync(
            Repository repository,
            HistoryQuery query,
            string targetHash,
            int trailingCount = 100,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CSharpGit.Domain.CommitDetails> ReadCommitAsync(
            Repository repository,
            string hash,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ChangedFile>> ReadChangedFilesAsync(
            Repository repository,
            string commitHash,
            string? parentHash,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileDiff> ReadDiffAsync(
            Repository repository,
            string hash,
            string path,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileDiff> ReadDiffAsync(
            Repository repository,
            string commitHash,
            string? parentHash,
            ChangedFile file,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileDiff> ReadDiffAsync(
            Repository repository,
            string commitHash,
            string? parentHash,
            ChangedFile file,
            DiffLoadMode mode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
