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
    private readonly IRepositoryStateService _stateService;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private RepositoryState _current;

    public RepositoryStateSession(IRepositoryStateService stateService, RepositoryState initialState)
    {
        _stateService = stateService;
        _current = initialState;
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
            if (!RepositoryStateComparer.HasSameGitState(previous, state))
                StateChanged?.Invoke(this, state);
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

    public ValueTask DisposeAsync()
    {
        _refreshLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
