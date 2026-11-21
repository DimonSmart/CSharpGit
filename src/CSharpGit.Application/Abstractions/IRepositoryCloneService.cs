namespace CSharpGit.Application.Abstractions;

public interface IRepositoryCloneService
{
    Task CloneAsync(
        string repositoryUrl,
        string targetPath,
        CancellationToken cancellationToken = default);

    Task CloneWithSubmodulesAsync(
        string repositoryUrl,
        string targetPath,
        bool initializeSubmodules,
        CancellationToken cancellationToken = default) =>
        initializeSubmodules
            ? throw new NotSupportedException("Recursive submodule cloning is not supported by this clone provider.")
            : CloneAsync(repositoryUrl, targetPath, cancellationToken);
}
