using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IInteractiveRebaseService
{
    Task<InteractiveRebasePlan> ReadInteractiveRebasePlanAsync(
        Repository repository,
        string onto,
        CancellationToken cancellationToken = default);

    Task<InteractiveRebasePlan> ReadInteractiveRebasePlanFromCommitAsync(
        Repository repository,
        string firstCommit,
        CancellationToken cancellationToken = default);

    Task<InteractiveRebaseTodo> ReadInteractiveRebaseTodoAsync(
        Repository repository,
        string onto,
        CancellationToken cancellationToken = default);

    Task<InteractiveRebaseTodo> ReadInteractiveRebaseTodoFromCommitAsync(
        Repository repository,
        string firstCommit,
        CancellationToken cancellationToken = default);

    Task<RebaseResult> StartInteractiveRebaseAsync(
        Repository repository,
        InteractiveRebasePlan plan,
        CancellationToken cancellationToken = default);

    Task<RebaseResult> StartInteractiveRebaseTodoAsync(
        Repository repository,
        InteractiveRebaseTodo todo,
        CancellationToken cancellationToken = default);

    Task<RebaseResult> ContinueRebaseAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    Task AbortRebaseAsync(
        Repository repository,
        CancellationToken cancellationToken = default);
}
