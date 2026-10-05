using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IStashMutationService
{
    Task CreateStashAsync(
        Repository repository,
        string? message = null,
        CancellationToken cancellationToken = default);

    Task CreateStashAsync(
        Repository repository,
        CreateStashRequest request,
        CancellationToken cancellationToken = default);

    Task ApplyStashAsync(
        Repository repository,
        string stashName,
        CancellationToken cancellationToken = default);

    Task ApplyStashAsync(
        Repository repository,
        GitStash stash,
        CancellationToken cancellationToken = default);

    Task PopStashAsync(
        Repository repository,
        string stashName,
        CancellationToken cancellationToken = default);

    Task PopStashAsync(
        Repository repository,
        GitStash stash,
        CancellationToken cancellationToken = default);

    Task DropStashAsync(
        Repository repository,
        GitStash stash,
        CancellationToken cancellationToken = default);
}
