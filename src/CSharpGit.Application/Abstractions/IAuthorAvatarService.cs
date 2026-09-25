namespace CSharpGit.Application.Abstractions;

public enum AuthorAvatarSource
{
    None,
    GitHub,
    Gravatar
}

public sealed record AuthorAvatarResult(
    string? ImagePath,
    AuthorAvatarSource Source);

public interface IAuthorAvatarService
{
    Task<AuthorAvatarResult> ResolveAsync(
        string authorName,
        string authorEmail,
        CancellationToken cancellationToken = default);

    Task InvalidateAsync(
        string authorName,
        string authorEmail,
        CancellationToken cancellationToken = default);
}


public enum AuthorAvatarDiagnosticActivityKind
{
    MemoryCacheHit,
    MemoryCacheMiss,
    DiskCacheHit,
    DiskCacheMiss,
    DiskCacheWrite,
    RemoteRequest,
    RemoteBytesRead
}

public sealed class AuthorAvatarDiagnosticActivityEventArgs(
    AuthorAvatarDiagnosticActivityKind kind,
    long bytes = 0) : EventArgs
{
    public AuthorAvatarDiagnosticActivityKind Kind { get; } = kind;
    public long Bytes { get; } = bytes;
}

public interface IAuthorAvatarDiagnosticSource
{
    event EventHandler<AuthorAvatarDiagnosticActivityEventArgs>? DiagnosticActivity;
}
