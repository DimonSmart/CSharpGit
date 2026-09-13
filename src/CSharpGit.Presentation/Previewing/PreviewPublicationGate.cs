namespace CSharpGit.Presentation.Previewing;

internal readonly record struct PreviewLease<TContext>(
    long Generation,
    TContext Context,
    CancellationToken CancellationToken);

internal sealed class PreviewPublicationGate<TContext> where TContext : notnull
{
    private readonly object _sync = new();
    private CancellationTokenSource? _active;
    private long _generation;

    internal PreviewLease<TContext> Begin(TContext context)
    {
        CancellationTokenSource? previous;
        CancellationTokenSource current;
        long generation;
        lock (_sync)
        {
            previous = _active;
            current = new CancellationTokenSource();
            _active = current;
            generation = ++_generation;
        }
        CancelAndDispose(previous);
        return new PreviewLease<TContext>(generation, context, current.Token);
    }

    internal void Cancel()
    {
        CancellationTokenSource? previous;
        lock (_sync)
        {
            previous = _active;
            _active = null;
            _generation++;
        }
        CancelAndDispose(previous);
    }

    internal bool CanPublish(PreviewLease<TContext> lease, TContext currentContext)
    {
        lock (_sync)
        {
            return _active is not null
                   && !lease.CancellationToken.IsCancellationRequested
                   && lease.Generation == _generation
                   && EqualityComparer<TContext>.Default.Equals(lease.Context, currentContext);
        }
    }

    private static void CancelAndDispose(CancellationTokenSource? source)
    {
        if (source is null) return;
        source.Cancel();
        source.Dispose();
    }
}
