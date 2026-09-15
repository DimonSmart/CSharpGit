using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed partial class GitCliRepositoryService
{
    public Task DeleteBranchAsync(
        Repository repository,
        string branch,
        BranchDeletionMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateRefName(branch, nameof(branch));

        return mode switch
        {
            BranchDeletionMode.Safe =>
                RunGitForMutationAsync(repository, cancellationToken, "branch", "--delete", branch),
            BranchDeletionMode.Force =>
                RunGitForMutationAsync(repository, cancellationToken, "branch", "--delete", "--force", branch),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported branch deletion mode.")
        };
    }

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
