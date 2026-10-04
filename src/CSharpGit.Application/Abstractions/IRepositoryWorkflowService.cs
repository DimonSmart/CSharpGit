using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IRepositoryWorkflowService
{
    Task CreateStashAsync(Repository repository, string? message = null, CancellationToken cancellationToken = default);

    Task CreateStashAsync(
        Repository repository,
        CreateStashRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Scope == StashScope.AllTrackedChanges
            && !request.IncludeUntracked
            && (request.Paths is null || request.Paths.Count == 0))
            return CreateStashAsync(repository, request.Message, cancellationToken);

        throw new NotSupportedException("This repository workflow implementation does not support the requested stash scope.");
    }

    Task ApplyStashAsync(Repository repository, string stashName, CancellationToken cancellationToken = default);
    Task ApplyStashAsync(
        Repository repository,
        GitStash stash,
        CancellationToken cancellationToken = default) =>
        ApplyStashAsync(repository, stash.Name, cancellationToken);

    Task PopStashAsync(Repository repository, string stashName, CancellationToken cancellationToken = default);
    Task PopStashAsync(
        Repository repository,
        GitStash stash,
        CancellationToken cancellationToken = default) =>
        PopStashAsync(repository, stash.Name, cancellationToken);

    Task DropStashAsync(
        Repository repository,
        GitStash stash,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This repository workflow implementation does not support dropping stashes.");
    Task<MergeResult> MergeAsync(Repository repository, string branch, CancellationToken cancellationToken = default);
    Task<InteractiveRebasePlan> ReadInteractiveRebasePlanAsync(Repository repository, string onto, CancellationToken cancellationToken = default);
    Task<InteractiveRebasePlan> ReadInteractiveRebasePlanFromCommitAsync(Repository repository, string firstCommit, CancellationToken cancellationToken = default);
    Task<RebaseResult> StartInteractiveRebaseAsync(Repository repository, InteractiveRebasePlan plan, CancellationToken cancellationToken = default);
    Task<InteractiveRebaseTodo> ReadInteractiveRebaseTodoAsync(Repository repository, string onto, CancellationToken cancellationToken = default);
    Task<InteractiveRebaseTodo> ReadInteractiveRebaseTodoFromCommitAsync(Repository repository, string firstCommit, CancellationToken cancellationToken = default);
    Task<RebaseResult> StartInteractiveRebaseTodoAsync(Repository repository, InteractiveRebaseTodo todo, CancellationToken cancellationToken = default);
    Task<RebaseResult> ContinueRebaseAsync(Repository repository, CancellationToken cancellationToken = default);
    Task AbortRebaseAsync(Repository repository, CancellationToken cancellationToken = default);
    Task ChooseConflictSideAsync(Repository repository, ConflictFile conflict, ConflictResolutionSide side, CancellationToken cancellationToken = default);
    Task KeepConflictDeletionAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default);
    Task StageResolvedConflictAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default);
    Task ContinueOperationAsync(Repository repository, CancellationToken cancellationToken = default);
    Task AbortOperationAsync(Repository repository, CancellationToken cancellationToken = default);
    Task SkipOperationAsync(Repository repository, CancellationToken cancellationToken = default);
}
