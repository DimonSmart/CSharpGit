using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class DefaultBranchRepositorySyncService : IRepositorySyncService
{
    private readonly GitRepositorySyncService _inner;
    private readonly DefaultBranchResolver _resolver;

    internal DefaultBranchRepositorySyncService(
        GitRepositorySyncService inner,
        DefaultBranchResolver resolver)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async Task FetchAsync(
        Repository repository,
        string remote,
        CancellationToken cancellationToken = default)
    {
        await _inner.FetchAsync(repository, remote, cancellationToken);
        await _resolver.RefreshRemoteHeadAsync(
            repository,
            remote,
            cancellationToken);
    }

    public async Task FetchAllAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        await _inner.FetchAllAsync(repository, cancellationToken);
        await _resolver.RefreshAllRemoteHeadsAsync(
            repository,
            cancellationToken);
    }

    public Task DeleteRemoteBranchAsync(
        Repository repository,
        string remote,
        string branch,
        CancellationToken cancellationToken = default) =>
        _inner.DeleteRemoteBranchAsync(
            repository,
            remote,
            branch,
            cancellationToken);

    public Task PullAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        _inner.PullAsync(repository, cancellationToken);

    public Task PushAsync(
        Repository repository,
        string? remote = null,
        string? branch = null,
        bool setUpstream = false,
        CancellationToken cancellationToken = default) =>
        _inner.PushAsync(
            repository,
            remote,
            branch,
            setUpstream,
            cancellationToken);

    public Task<ForcePushWithLeaseSnapshot> PrepareForcePushWithLeaseAsync(
        Repository repository,
        string? remote = null,
        string? remoteBranch = null,
        CancellationToken cancellationToken = default) =>
        _inner.PrepareForcePushWithLeaseAsync(
            repository,
            remote,
            remoteBranch,
            cancellationToken);

    public Task ForcePushWithLeaseAsync(
        Repository repository,
        ForcePushWithLeaseSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        _inner.ForcePushWithLeaseAsync(
            repository,
            snapshot,
            cancellationToken);
}
