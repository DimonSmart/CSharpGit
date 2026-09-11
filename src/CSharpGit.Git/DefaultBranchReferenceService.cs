using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class DefaultBranchReferenceService : IReferenceService
{
    private readonly GitCliRepositoryService _inner;
    private readonly DefaultBranchResolver _resolver;

    internal DefaultBranchReferenceService(
        GitCliRepositoryService inner,
        DefaultBranchResolver resolver)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public Task SwitchBranchAsync(Repository repository, string branch, CancellationToken cancellationToken = default) =>
        _inner.SwitchBranchAsync(repository, branch, cancellationToken);

    public Task CheckoutAsync(Repository repository, string reference, CancellationToken cancellationToken = default) =>
        _inner.CheckoutAsync(repository, reference, cancellationToken);

    public Task CreateBranchAsync(Repository repository, string branch, string? startPoint = null, bool switchToBranch = true, CancellationToken cancellationToken = default) =>
        _inner.CreateBranchAsync(repository, branch, startPoint, switchToBranch, cancellationToken);

    public Task DeleteBranchAsync(Repository repository, string branch, CancellationToken cancellationToken = default) =>
        _inner.DeleteBranchAsync(repository, branch, cancellationToken);

    public Task DeleteRemoteBranchAsync(Repository repository, string remote, string branch, CancellationToken cancellationToken = default) =>
        _inner.DeleteRemoteBranchAsync(repository, remote, branch, cancellationToken);

    public Task CheckoutRemoteBranchAsync(Repository repository, string remoteBranch, string localBranch, CancellationToken cancellationToken = default) =>
        _inner.CheckoutRemoteBranchAsync(repository, remoteBranch, localBranch, cancellationToken);

    public async Task FetchAsync(Repository repository, string remote, CancellationToken cancellationToken = default)
    {
        await _inner.FetchAsync(repository, remote, cancellationToken);
        await _resolver.RefreshRemoteHeadAsync(repository, remote, cancellationToken);
    }

    public async Task FetchAllAsync(Repository repository, CancellationToken cancellationToken = default)
    {
        await _inner.FetchAllAsync(repository, cancellationToken);
        await _resolver.RefreshAllRemoteHeadsAsync(repository, cancellationToken);
    }

    public Task PullAsync(Repository repository, CancellationToken cancellationToken = default) =>
        _inner.PullAsync(repository, cancellationToken);

    public Task PushAsync(Repository repository, string? remote = null, string? branch = null, bool setUpstream = false, CancellationToken cancellationToken = default) =>
        _inner.PushAsync(repository, remote, branch, setUpstream, cancellationToken);

    public Task<ForcePushWithLeaseSnapshot> PrepareForcePushWithLeaseAsync(Repository repository, string? remote = null, string? remoteBranch = null, CancellationToken cancellationToken = default) =>
        _inner.PrepareForcePushWithLeaseAsync(repository, remote, remoteBranch, cancellationToken);

    public Task ForcePushWithLeaseAsync(Repository repository, ForcePushWithLeaseSnapshot snapshot, CancellationToken cancellationToken = default) =>
        _inner.ForcePushWithLeaseAsync(repository, snapshot, cancellationToken);

    public Task<ApplyCommitResult> CherryPickAsync(Repository repository, string commit, int? mainlineParent = null, CancellationToken cancellationToken = default) =>
        _inner.CherryPickAsync(repository, commit, mainlineParent, cancellationToken);

    public Task<ApplyCommitResult> RevertAsync(Repository repository, string commit, int? mainlineParent = null, CancellationToken cancellationToken = default) =>
        _inner.RevertAsync(repository, commit, mainlineParent, cancellationToken);

    public Task ResetAsync(Repository repository, string commit, ResetMode mode, CancellationToken cancellationToken = default) =>
        _inner.ResetAsync(repository, commit, mode, cancellationToken);
}
