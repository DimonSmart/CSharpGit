using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed partial class GitCliRepositoryService
{
    public async Task DeleteRemoteBranchAsync(
        Repository repository,
        string remote,
        string branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateRefName(remote, nameof(remote));
        ValidateRefName(branch, nameof(branch));
        await RunPushAsync(repository, cancellationToken, "push", "--porcelain", remote, "--delete", branch);
    }
}
