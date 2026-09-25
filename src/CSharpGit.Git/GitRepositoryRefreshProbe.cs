using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitRepositoryRefreshProbe : IRepositoryRefreshProbe
{
    private readonly IRepositoryStateService _stateService;

    internal GitRepositoryRefreshProbe(IRepositoryStateService stateService)
    {
        _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
    }

    internal GitRepositoryRefreshProbe(GitCommandExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        var runner = new GitRepositoryCommandRunner(executor);
        var inner = new GitRepositoryStateService(runner);
        var tags = new GitTagService(executor);
        _stateService = new DefaultBranchRepositoryStateService(
            inner,
            new DefaultBranchResolver(executor),
            tags);
    }

    public async Task<RepositoryRefreshFingerprint> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var read = await _stateService.ReadWithRefreshFingerprintAsync(
            repository,
            localOnly: true,
            cancellationToken);
        return read.RefreshFingerprint
               ?? throw new InvalidOperationException(
                   "Repository refresh fingerprint could not be built reliably.");
    }
}
