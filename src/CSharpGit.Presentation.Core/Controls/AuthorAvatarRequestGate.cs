namespace CSharpGit.Presentation.Controls;

internal readonly record struct AuthorAvatarRequest(
    long Generation,
    AuthorAvatarLookupKey LookupKey,
    CancellationToken CancellationToken,
    CancellationTokenSource CancellationSource);

internal sealed class AuthorAvatarRequestGate : IDisposable
{
    private long _generation;
    private CancellationTokenSource? _cancellation;

    public AuthorAvatarRequest Start(AuthorAvatarLookupKey lookupKey)
    {
        Cancel();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var generation = Interlocked.Increment(ref _generation);
        return new AuthorAvatarRequest(
            generation,
            lookupKey,
            cancellation.Token,
            cancellation);
    }

    public bool IsCurrent(AuthorAvatarRequest request, AuthorAvatarLookupKey currentKey) =>
        !request.CancellationToken.IsCancellationRequested
        && request.Generation == Volatile.Read(ref _generation)
        && request.LookupKey == currentKey;

    public bool TryComplete(AuthorAvatarRequest request)
    {
        if (request.Generation != Volatile.Read(ref _generation))
            return false;

        var cancellation = Interlocked.CompareExchange(
            ref _cancellation,
            null,
            request.CancellationSource);
        if (!ReferenceEquals(cancellation, request.CancellationSource))
            return false;

        request.CancellationSource.Dispose();
        return true;
    }

    public bool Cancel()
    {
        var cancellation = Interlocked.Exchange(ref _cancellation, null);
        if (cancellation is null)
            return false;

        Interlocked.Increment(ref _generation);
        cancellation.Cancel();
        cancellation.Dispose();
        return true;
    }

    public void Dispose() => Cancel();
}
