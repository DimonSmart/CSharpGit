using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public sealed record RepositoryStateReadResult(
    RepositoryState State,
    RepositoryRefreshFingerprint? RefreshFingerprint);

public interface IRepositoryStateService
{
    Task<RepositoryState> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    Task<RepositoryState> ReadLocalOnlyAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    async Task<RepositoryStateReadResult> ReadWithRefreshFingerprintAsync(
        Repository repository,
        bool localOnly = false,
        CancellationToken cancellationToken = default)
    {
        var state = localOnly
            ? await ReadLocalOnlyAsync(repository, cancellationToken)
            : await ReadAsync(repository, cancellationToken);
        return new RepositoryStateReadResult(state, null);
    }
}
