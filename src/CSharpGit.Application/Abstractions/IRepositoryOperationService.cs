using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IRepositoryOperationService
{
    Task ContinueOperationAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    Task AbortOperationAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    Task SkipOperationAsync(
        Repository repository,
        CancellationToken cancellationToken = default);
}
