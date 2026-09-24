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
