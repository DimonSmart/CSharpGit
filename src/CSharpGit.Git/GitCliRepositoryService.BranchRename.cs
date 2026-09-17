using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed partial class GitCliRepositoryService
{
    public Task RenameBranchAsync(
        Repository repository,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateRefName(oldName, nameof(oldName));
        ValidateRefName(newName, nameof(newName));
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            throw new ArgumentException(
                "The new branch name must differ from the current name.",
                nameof(newName));

        return RunGitForMutationAsync(
            repository,
            cancellationToken,
            "branch",
            "-m",
            oldName,
            newName);
    }
}
