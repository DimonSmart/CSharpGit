using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public sealed record RepositoryRefreshFingerprint(string Value);

public interface IRepositoryRefreshProbe
{
    Task<RepositoryRefreshFingerprint> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default);
}
