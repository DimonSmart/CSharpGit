namespace CSharpGit.Presentation.Controls;

internal readonly record struct AuthorAvatarRequest(
    long Generation,
    string Identity,
    CancellationToken CancellationToken);

internal sealed class AuthorAvatarRequestGate : IDisposable
{
    private long _generation;
    private CancellationTokenSource? _cancellation;

    public AuthorAvatarRequest Start(string identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Cancel();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var generation = Interlocked.Increment(ref _generation);
        return new AuthorAvatarRequest(generation, identity, cancellation.Token);
    }

    public bool IsCurrent(AuthorAvatarRequest request, string currentIdentity) =>
        !request.CancellationToken.IsCancellationRequested
        && request.Generation == Volatile.Read(ref _generation)
        && string.Equals(request.Identity, currentIdentity, StringComparison.Ordinal);

    public bool Cancel()
    {
        var cancellation = Interlocked.Exchange(ref _cancellation, null);
        if (cancellation is null) return false;
        cancellation.Cancel();
        cancellation.Dispose();
        return true;
    }

    public void Dispose() => Cancel();
}
