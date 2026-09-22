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
        GitRefValidator.Validate(branch, nameof(branch));

        return mode switch
        {
            BranchDeletionMode.Safe =>
                _runner.RunMutationAsync(repository, cancellationToken, "branch", "--delete", branch),
            BranchDeletionMode.Force =>
                _runner.RunMutationAsync(repository, cancellationToken, "branch", "--delete", "--force", branch),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported branch deletion mode.")
        };
    }

}
