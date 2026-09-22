using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IRepositoryWorkflowService
{
    Task CreateStashAsync(Repository repository, string? message = null, CancellationToken cancellationToken = default);
    Task ApplyStashAsync(Repository repository, string stashName, CancellationToken cancellationToken = default);
    Task PopStashAsync(Repository repository, string stashName, CancellationToken cancellationToken = default);
    Task<MergeResult> MergeAsync(Repository repository, string branch, CancellationToken cancellationToken = default);
    Task<InteractiveRebasePlan> ReadInteractiveRebasePlanAsync(Repository repository, string onto, CancellationToken cancellationToken = default);
    Task<RebaseResult> StartInteractiveRebaseAsync(Repository repository, InteractiveRebasePlan plan, CancellationToken cancellationToken = default);
    Task<RebaseResult> ContinueRebaseAsync(Repository repository, CancellationToken cancellationToken = default);
    Task AbortRebaseAsync(Repository repository, CancellationToken cancellationToken = default);
    Task ChooseConflictSideAsync(Repository repository, ConflictFile conflict, ConflictResolutionSide side, CancellationToken cancellationToken = default);
    Task KeepConflictDeletionAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default);
    Task StageResolvedConflictAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default);
    Task OpenConflictAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default);
    Task ConfigureMergeToolAsync(Repository repository, MergeToolConfiguration configuration, CancellationToken cancellationToken = default);
    Task RunMergeToolForFileAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default);
    Task RunMergeToolWorkflowAsync(Repository repository, CancellationToken cancellationToken = default);
    Task ContinueOperationAsync(Repository repository, CancellationToken cancellationToken = default);
    Task AbortOperationAsync(Repository repository, CancellationToken cancellationToken = default);
    Task SkipOperationAsync(Repository repository, CancellationToken cancellationToken = default);
}
