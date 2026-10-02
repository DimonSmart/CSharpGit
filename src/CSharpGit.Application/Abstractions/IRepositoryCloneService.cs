namespace CSharpGit.Application.Abstractions;

public interface IRepositoryCloneService
{
    Task CloneAsync(
        string repositoryUrl,
        string targetPath,
        CancellationToken cancellationToken = default);
}
