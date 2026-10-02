using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public sealed record RepositoryStateReadResult(
    RepositoryState State,
    WorkingTreeStatusSnapshot WorkingTreeStatus);

public interface IRepositoryStateService
{
    Task<RepositoryState> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    Task<RepositoryState> ReadLocalOnlyAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    Task<RepositoryStateReadResult> ReadWithWorkingTreeStatusAsync(
        Repository repository,
        bool localOnly = false,
        CancellationToken cancellationToken = default);
}
