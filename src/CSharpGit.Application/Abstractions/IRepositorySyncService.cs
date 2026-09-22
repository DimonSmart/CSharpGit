using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IRepositorySyncService
{
    Task FetchAsync(Repository repository, string remote, CancellationToken cancellationToken = default);
    Task FetchAllAsync(Repository repository, CancellationToken cancellationToken = default);
    Task PullAsync(Repository repository, CancellationToken cancellationToken = default);
    Task PushAsync(Repository repository, string? remote = null, string? branch = null, bool setUpstream = false, CancellationToken cancellationToken = default);
    Task<ForcePushWithLeaseSnapshot> PrepareForcePushWithLeaseAsync(Repository repository, string? remote = null, string? remoteBranch = null, CancellationToken cancellationToken = default);
    Task ForcePushWithLeaseAsync(Repository repository, ForcePushWithLeaseSnapshot snapshot, CancellationToken cancellationToken = default);
}
