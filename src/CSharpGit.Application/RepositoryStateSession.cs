using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Application;

public sealed class RepositoryStateSessionFactory(IRepositoryStateService stateService) : IRepositoryStateSessionFactory
{
    public async Task<IRepositoryStateSession> CreateAsync(Repository repository, CancellationToken cancellationToken = default)
    {
        var initialState = await stateService.ReadAsync(repository, cancellationToken);
        return new RepositoryStateSession(stateService, initialState);
    }
}

internal sealed class RepositoryStateSession : IRepositoryStateSession
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private readonly IRepositoryStateService _stateService;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Task _monitor;
    private RepositoryState _current;

    public RepositoryStateSession(IRepositoryStateService stateService, RepositoryState initialState)
    {
        _stateService = stateService;
        _current = initialState;
        _monitor = MonitorAsync(_stop.Token);
    }

    public RepositoryState Current => Volatile.Read(ref _current);
    public event EventHandler<RepositoryState>? StateChanged;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var state = await _stateService.ReadAsync(Current.Repository, cancellationToken);
            var previous = Interlocked.Exchange(ref _current, state);
            if (!HasSameGitState(previous, state))
            {
                StateChanged?.Invoke(this, state);
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task RunMutationAsync(Func<CancellationToken, Task> mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        try
        {
            await mutation(cancellationToken);
        }
        finally
        {
            await RefreshAsync(CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try { await _monitor; } catch (OperationCanceledException) { }
        _stop.Dispose();
        _refreshLock.Dispose();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try { await RefreshAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch { /* A transient read failure must not stop future refresh attempts. */ }
        }
    }

    // ReadAtUtc is metadata of a read and is intentionally excluded from semantic Git state.
    private static bool HasSameGitState(RepositoryState left, RepositoryState right) =>
        left.Repository == right.Repository &&
        left.HeadReference == right.HeadReference &&
        left.HeadCommit == right.HeadCommit &&
        left.IsDetached == right.IsDetached &&
        left.Operation == right.Operation &&
        OperationsEqual(left.CurrentOperation, right.CurrentOperation) &&
        left.Changes.SequenceEqual(right.Changes) &&
        ReferencesEqual(left.Refs, right.Refs) &&
        StashesEqual(left.Stashes, right.Stashes) &&
        DictionariesEqual(left.GlobalConfiguration, right.GlobalConfiguration) &&
        DictionariesEqual(left.LocalConfiguration, right.LocalConfiguration);

    private static bool ReferencesEqual(GitReferences left, GitReferences right) =>
        BranchesEqual(left.LocalBranches, right.LocalBranches) &&
        BranchesEqual(left.RemoteBranches, right.RemoteBranches) &&
        RemotesEqual(left.Remotes, right.Remotes) &&
        TagsEqual(left.Tags, right.Tags);

    private static bool BranchesEqual(IReadOnlyList<GitBranch> left, IReadOnlyList<GitBranch> right) =>
        NamedItemsEqual(left, right, branch => branch.Name, BranchEqual);

    private static bool BranchEqual(GitBranch left, GitBranch right) =>
        StringComparer.Ordinal.Equals(left.Name, right.Name) &&
        StringComparer.Ordinal.Equals(left.Commit, right.Commit) &&
        left.IsCurrent == right.IsCurrent &&
        StringComparer.Ordinal.Equals(left.Upstream, right.Upstream) &&
        left.Ahead == right.Ahead &&
        left.Behind == right.Behind;

    private static bool RemotesEqual(IReadOnlyList<GitRemote> left, IReadOnlyList<GitRemote> right) =>
        NamedItemsEqual(left, right, remote => remote.Name, RemoteEqual);

    private static bool RemoteEqual(GitRemote left, GitRemote right) =>
        StringComparer.Ordinal.Equals(left.Name, right.Name) &&
        StringComparer.Ordinal.Equals(left.FetchUrl, right.FetchUrl) &&
        StringComparer.Ordinal.Equals(left.PushUrl, right.PushUrl);

    private static bool TagsEqual(IReadOnlyList<GitTag> left, IReadOnlyList<GitTag> right) =>
        NamedItemsEqual(left, right, tag => tag.Name, TagEqual);

    private static bool TagEqual(GitTag left, GitTag right) =>
        StringComparer.Ordinal.Equals(left.Name, right.Name) &&
        StringComparer.Ordinal.Equals(left.Commit, right.Commit);

    private static bool StashesEqual(IReadOnlyList<GitStash> left, IReadOnlyList<GitStash> right) =>
        NamedItemsEqual(left, right, stash => stash.Name, StashEqual);

    private static bool StashEqual(GitStash left, GitStash right) =>
        StringComparer.Ordinal.Equals(left.Name, right.Name) &&
        StringComparer.Ordinal.Equals(left.Commit, right.Commit) &&
        StringComparer.Ordinal.Equals(left.Message, right.Message);

    private static bool OperationsEqual(RepositoryOperationState left, RepositoryOperationState right) =>
        left.Kind == right.Kind &&
        left.CanContinue == right.CanContinue &&
        left.CanAbort == right.CanAbort &&
        left.CanSkip == right.CanSkip &&
        ConflictsEqual(left.Conflicts, right.Conflicts);

    private static bool ConflictsEqual(IReadOnlyList<ConflictFile> left, IReadOnlyList<ConflictFile> right) =>
        NamedItemsEqual(left, right, conflict => conflict.Path, ConflictEqual);

    private static bool ConflictEqual(ConflictFile left, ConflictFile right) =>
        StringComparer.Ordinal.Equals(left.Path, right.Path) &&
        left.Kind == right.Kind &&
        left.IsResolved == right.IsResolved &&
        left.CanOpenManually == right.CanOpenManually &&
        left.CanChooseCurrentLocal == right.CanChooseCurrentLocal &&
        left.CanChooseIncomingRemote == right.CanChooseIncomingRemote &&
        left.CanKeepDeletion == right.CanKeepDeletion &&
        left.CanStage == right.CanStage &&
        left.CanRunMergeTool == right.CanRunMergeTool &&
        StringComparer.Ordinal.Equals(left.CurrentLocalLabel, right.CurrentLocalLabel) &&
        StringComparer.Ordinal.Equals(left.IncomingRemoteLabel, right.IncomingRemoteLabel);

    private static bool NamedItemsEqual<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, string> nameSelector,
        Func<T, T, bool> itemEqual)
        where T : notnull
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var rightByName = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in right)
        {
            if (!rightByName.TryAdd(nameSelector(item), item))
            {
                return false;
            }
        }

        var matchedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in left)
        {
            var name = nameSelector(item);
            if (!matchedNames.Add(name) ||
                !rightByName.TryGetValue(name, out var matchingItem) ||
                !itemEqual(item, matchingItem))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DictionariesEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count &&
        left.All(pair => right.Any(candidate =>
            StringComparer.Ordinal.Equals(candidate.Key, pair.Key) &&
            StringComparer.Ordinal.Equals(candidate.Value, pair.Value)));
}
