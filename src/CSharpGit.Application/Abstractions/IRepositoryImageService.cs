namespace CSharpGit.Application.Abstractions;

public sealed record RepositoryImageCacheState(string? ImagePath, bool ShouldRefresh);

public interface IRepositoryImageService
{
    RepositoryImageCacheState GetCachedState(string repositoryPath);

    Task<string?> ResolveAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);
}
