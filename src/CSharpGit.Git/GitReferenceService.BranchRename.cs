using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed partial class GitReferenceService
{
    public Task RenameBranchAsync(
        Repository repository,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        GitReferenceValidator.ValidateRefName(oldName, nameof(oldName));
        GitReferenceValidator.ValidateRefName(newName, nameof(newName));
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            throw new ArgumentException(
                "The new branch name must differ from the current name.",
                nameof(newName));

        return _commands.RunMutationAsync(
            repository,
            cancellationToken,
            "branch",
            "-m",
            oldName,
            newName);
    }
}
