using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class DefaultBranchRepositoryStateService : IRepositoryStateService
{
    private readonly GitCliRepositoryService _inner;
    private readonly DefaultBranchResolver _resolver;

    internal DefaultBranchRepositoryStateService(
        GitCliRepositoryService inner,
        DefaultBranchResolver resolver)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async Task<RepositoryState> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        var state = await _inner.ReadAsync(repository, cancellationToken);
        var references = state.Refs;
        var defaultRemoteBranch = await _resolver.ResolveAsync(repository, references, cancellationToken);

        var localBranches = references.LocalBranches
            .Select(branch => branch with
            {
                IsDefault = defaultRemoteBranch is not null &&
                            string.Equals(branch.Upstream, defaultRemoteBranch, StringComparison.Ordinal)
            })
            .ToArray();
        var remoteBranches = references.RemoteBranches
            .Select(branch => branch with
            {
                IsDefault = defaultRemoteBranch is not null &&
                            string.Equals(branch.Name, defaultRemoteBranch, StringComparison.Ordinal)
            })
            .ToArray();

        return state with
        {
            References = references with
            {
                LocalBranches = localBranches,
                RemoteBranches = remoteBranches
            }
        };
    }
}
