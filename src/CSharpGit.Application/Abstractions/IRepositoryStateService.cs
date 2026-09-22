using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IRepositoryStateService
{
    Task<RepositoryState> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    Task<RepositoryState> ReadLocalOnlyAsync(
        Repository repository,
        CancellationToken cancellationToken = default);
}
