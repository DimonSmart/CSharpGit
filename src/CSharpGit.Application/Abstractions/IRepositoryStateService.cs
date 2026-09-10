using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IRepositoryStateService
{
    Task<RepositoryState> ReadAsync(Repository repository, CancellationToken cancellationToken = default);
}

public interface IWorkingTreeService
{
    Task StageFileAsync(Repository repository, WorkingTreeChange change, CancellationToken cancellationToken = default);
    Task StageFilesAsync(Repository repository, IReadOnlyCollection<WorkingTreeChange> changes, CancellationToken cancellationToken = default);
    Task StageAllAsync(Repository repository, CancellationToken cancellationToken = default);
    Task UnstageFileAsync(Repository repository, WorkingTreeChange change, CancellationToken cancellationToken = default);
    Task UnstageFilesAsync(Repository repository, IReadOnlyCollection<WorkingTreeChange> changes, CancellationToken cancellationToken = default);
    Task UnstageAllAsync(Repository repository, CancellationToken cancellationToken = default);
    Task DiscardFileAsync(Repository repository, WorkingTreeChange change, CancellationToken cancellationToken = default);
    Task CommitAsync(Repository repository, string message, bool amend = false, bool intentionalEmpty = false, CancellationToken cancellationToken = default);
}

public interface IReferenceService
{
    Task SwitchBranchAsync(Repository repository, string branch, CancellationToken cancellationToken = default);
    Task CheckoutAsync(Repository repository, string reference, CancellationToken cancellationToken = default);
    Task CreateBranchAsync(Repository repository, string branch, string? startPoint = null, bool switchToBranch = true, CancellationToken cancellationToken = default);
    Task DeleteBranchAsync(Repository repository, string branch, CancellationToken cancellationToken = default);
    Task CheckoutRemoteBranchAsync(Repository repository, string remoteBranch, string localBranch, CancellationToken cancellationToken = default);
    Task FetchAsync(Repository repository, string remote, CancellationToken cancellationToken = default);
    Task FetchAllAsync(Repository repository, CancellationToken cancellationToken = default);
    Task PullAsync(Repository repository, CancellationToken cancellationToken = default);
    Task PushAsync(Repository repository, string? remote = null, string? branch = null, bool setUpstream = false, CancellationToken cancellationToken = default);
    Task<ForcePushWithLeaseSnapshot> PrepareForcePushWithLeaseAsync(Repository repository, string? remote = null, string? remoteBranch = null, CancellationToken cancellationToken = default);
    Task ForcePushWithLeaseAsync(Repository repository, ForcePushWithLeaseSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<ApplyCommitResult> CherryPickAsync(Repository repository, string commit, int? mainlineParent = null, CancellationToken cancellationToken = default);
    Task<ApplyCommitResult> RevertAsync(Repository repository, string commit, int? mainlineParent = null, CancellationToken cancellationToken = default);
    Task ResetAsync(Repository repository, string commit, ResetMode mode, CancellationToken cancellationToken = default);
}

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

public interface IRepositoryStateSession : IAsyncDisposable
{
    RepositoryState Current { get; }
    event EventHandler<RepositoryState>? StateChanged;
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task RunMutationAsync(Func<CancellationToken, Task> mutation, CancellationToken cancellationToken = default);
}

public interface IRepositoryStateSessionFactory
{
    Task<IRepositoryStateSession> CreateAsync(Repository repository, CancellationToken cancellationToken = default);
}
