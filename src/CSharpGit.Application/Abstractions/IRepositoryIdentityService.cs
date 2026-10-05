using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IRepositoryIdentityService
{
    Task<RepositoryIdentitySnapshot> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        Repository repository,
        RepositoryIdentityEdit edit,
        CancellationToken cancellationToken = default);

    Task RemoveOverrideAsync(
        Repository repository,
        RepositoryIdentityField field,
        CancellationToken cancellationToken = default);
}
