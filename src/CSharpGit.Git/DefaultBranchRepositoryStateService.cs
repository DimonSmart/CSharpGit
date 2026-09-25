using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class DefaultBranchRepositoryStateService : IRepositoryStateService
{
    private readonly GitRepositoryStateService _inner;
    private readonly GitTagService _tagService;

    internal DefaultBranchRepositoryStateService(
        GitRepositoryStateService inner,
        DefaultBranchResolver resolver,
        ITagService tagService)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _ = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _tagService = tagService as GitTagService
            ?? throw new ArgumentException("The optimized repository state reader requires GitTagService.", nameof(tagService));
    }

    public Task<RepositoryState> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        ReadCoreAsync(repository, cancellationToken);

    public Task<RepositoryState> ReadLocalOnlyAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        ReadCoreAsync(repository, cancellationToken);

    internal async Task<GitRepositoryStateReadResult> ReadDetailedAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadDetailedAsync(repository, cancellationToken);
        var tags = await _tagService.ReadTagsAsync(
            repository,
            read.EffectiveTagSort,
            cancellationToken);

        var references = read.State.Refs;
        var defaultRemoteBranch = read.LocalDefaultRemoteBranch;
        var localBranches = references.LocalBranches
            .Select(branch => branch with
            {
                IsDefault = defaultRemoteBranch is not null
                            && string.Equals(branch.Upstream, defaultRemoteBranch, StringComparison.Ordinal)
            })
            .ToArray();
        var remoteBranches = references.RemoteBranches
            .Select(branch => branch with
            {
                IsDefault = defaultRemoteBranch is not null
                            && string.Equals(branch.Name, defaultRemoteBranch, StringComparison.Ordinal)
            })
            .ToArray();

        var state = read.State with
        {
            References = references with
            {
                LocalBranches = localBranches,
                RemoteBranches = remoteBranches,
                Tags = tags
            }
        };

        return read with { State = state };
    }

    private async Task<RepositoryState> ReadCoreAsync(
        Repository repository,
        CancellationToken cancellationToken) =>
        (await ReadDetailedAsync(repository, cancellationToken)).State;
}
