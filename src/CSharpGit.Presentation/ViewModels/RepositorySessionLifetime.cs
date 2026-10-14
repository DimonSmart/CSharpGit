namespace CSharpGit.Presentation.ViewModels;

// Owns only cancellable repository reads. Mutation lifetimes remain governed by
// the existing mutation gate and its close/switch restrictions.
internal sealed class RepositorySessionLifetime
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _token;
    private int _readers;
    private bool _ended;
    private bool _cancelled;

    public RepositorySessionLifetime(long generation)
    {
        Generation = generation;
        _token = _cancellation.Token;
    }

    public long Generation { get; }

    public bool IsActive
    {
        get { lock (_sync) return !_ended; }
    }

    public bool TryAcquire(out CancellationToken token)
    {
        lock (_sync)
        {
            token = _token;
            if (_ended) return false;
            _readers++;
            return true;
        }
    }

    public void Release()
    {
        lock (_sync)
        {
            if (_readers <= 0) throw new InvalidOperationException("Unbalanced repository read lifetime.");
            _readers--;
            if (_cancelled && _readers == 0)
                _cancellation.Dispose();
        }
    }

    public void End()
    {
        lock (_sync)
        {
            if (_ended) return;
            _ended = true;
            // Cancellation callbacks must not mutate the session while the lock
            // is held; Cancel runs outside the lock.
        }

        try
        {
            _cancellation.Cancel();
        }
        finally
        {
            lock (_sync)
            {
                _cancelled = true;
                if (_readers == 0)
                    _cancellation.Dispose();
            }
        }
    }
}
