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

    private static bool HasSameGitState(RepositoryState left, RepositoryState right) =>
        left.Repository == right.Repository &&
        left.HeadReference == right.HeadReference &&
        left.HeadCommit == right.HeadCommit &&
        left.IsDetached == right.IsDetached &&
        left.Operation == right.Operation &&
        left.Changes.SequenceEqual(right.Changes) &&
        DictionariesEqual(left.GlobalConfiguration, right.GlobalConfiguration) &&
        DictionariesEqual(left.LocalConfiguration, right.LocalConfiguration);

    private static bool DictionariesEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);
}
