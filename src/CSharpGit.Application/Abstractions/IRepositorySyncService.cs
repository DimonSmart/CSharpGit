using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public sealed record PushOptions(bool AutoSetupRemote = false);

public sealed record PublishBranchRequest(
    string Remote,
    string RemoteBranch,
    bool SetUpstream);

public sealed record PublishBranchPreparation(
    string LocalBranch,
    string? SuggestedRemote);

public interface IRepositorySyncService
{
    Task FetchAsync(Repository repository, string remote, CancellationToken cancellationToken = default);
    Task FetchAllAsync(Repository repository, CancellationToken cancellationToken = default);
    Task DeleteRemoteBranchAsync(Repository repository, string remote, string branch, CancellationToken cancellationToken = default);
    Task PullAsync(Repository repository, CancellationToken cancellationToken = default);
    Task PushAsync(Repository repository, PushOptions? options = null, CancellationToken cancellationToken = default);
    Task<PublishBranchPreparation> PreparePublishBranchAsync(Repository repository, CancellationToken cancellationToken = default);
    Task PublishBranchAsync(Repository repository, PublishBranchRequest request, CancellationToken cancellationToken = default);
    Task<ForcePushWithLeaseSnapshot> PrepareForcePushWithLeaseAsync(Repository repository, string? remote = null, string? remoteBranch = null, CancellationToken cancellationToken = default);
    Task ForcePushWithLeaseAsync(Repository repository, ForcePushWithLeaseSnapshot snapshot, CancellationToken cancellationToken = default);
}
