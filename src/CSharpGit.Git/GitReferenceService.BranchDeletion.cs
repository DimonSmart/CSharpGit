using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed partial class GitReferenceService
{
    public Task DeleteBranchAsync(
        Repository repository,
        string branch,
        BranchDeletionMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        GitReferenceValidator.ValidateRefName(branch, nameof(branch));

        return mode switch
        {
            BranchDeletionMode.Safe =>
                _commands.RunMutationAsync(repository, cancellationToken, "branch", "--delete", branch),
            BranchDeletionMode.Force =>
                _commands.RunMutationAsync(repository, cancellationToken, "branch", "--delete", "--force", branch),
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
        GitReferenceValidator.ValidateRefName(remote, nameof(remote));
        GitReferenceValidator.ValidateRefName(branch, nameof(branch));
        await _push.RunAsync(repository, cancellationToken, "push", "--porcelain", remote, "--delete", branch);
    }
}
