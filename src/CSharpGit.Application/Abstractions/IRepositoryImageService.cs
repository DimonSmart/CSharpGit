namespace CSharpGit.Application.Abstractions;

public enum RepositoryImageKind
{
    Icon,
    Preview
}

public sealed record RepositoryImageCacheState(
    string? ImagePath,
    bool ShouldRefresh,
    RepositoryImageKind? Kind = null);

public interface IRepositoryImageService
{
    RepositoryImageCacheState GetCachedState(string repositoryPath);

    Task<string?> ResolveAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);
}
