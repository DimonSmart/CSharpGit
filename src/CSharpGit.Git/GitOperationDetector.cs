using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitOperationDetector
{
    internal RepositoryOperation Detect(Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (Directory.Exists(Path.Combine(repository.GitDirectory, "rebase-merge"))
            || Directory.Exists(Path.Combine(repository.GitDirectory, "rebase-apply")))
            return RepositoryOperation.Rebase;
        if (File.Exists(Path.Combine(repository.GitDirectory, "MERGE_HEAD")))
            return RepositoryOperation.Merge;
        if (File.Exists(Path.Combine(repository.GitDirectory, "CHERRY_PICK_HEAD")))
            return RepositoryOperation.CherryPick;
        if (File.Exists(Path.Combine(repository.GitDirectory, "REVERT_HEAD")))
            return RepositoryOperation.Revert;
        if (File.Exists(Path.Combine(repository.GitDirectory, "BISECT_LOG")))
            return RepositoryOperation.Bisect;
        return RepositoryOperation.None;
    }
}
