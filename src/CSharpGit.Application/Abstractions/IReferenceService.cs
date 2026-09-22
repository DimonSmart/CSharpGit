using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IReferenceService
{
    Task SwitchBranchAsync(Repository repository, string branch, CancellationToken cancellationToken = default);
    Task CheckoutAsync(Repository repository, string reference, CancellationToken cancellationToken = default);
    Task CreateBranchAsync(Repository repository, string branch, string? startPoint = null, bool switchToBranch = true, CancellationToken cancellationToken = default);
    Task RenameBranchAsync(Repository repository, string oldName, string newName, CancellationToken cancellationToken = default);
    Task DeleteBranchAsync(Repository repository, string branch, CancellationToken cancellationToken = default);
    Task DeleteBranchAsync(Repository repository, string branch, BranchDeletionMode mode, CancellationToken cancellationToken = default);
    Task DeleteRemoteBranchAsync(Repository repository, string remote, string branch, CancellationToken cancellationToken = default);
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
    Task<EditCommitMessageResult> EditCommitMessageAsync(
        Repository repository,
        string commit,
        string newMessage,
        CancellationToken cancellationToken = default);
}
