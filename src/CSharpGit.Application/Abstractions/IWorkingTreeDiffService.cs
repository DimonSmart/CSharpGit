using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public enum WorkingTreeDiffKind
{
    Unstaged,
    Staged
}

public interface IWorkingTreeDiffService
{
    Task<FileDiff> ReadDiffAsync(
        Repository repository,
        WorkingTreeChange change,
        WorkingTreeDiffKind kind,
        CancellationToken cancellationToken = default);

    Task<FileDiff> ReadDiffAsync(
        Repository repository,
        WorkingTreeChange change,
        WorkingTreeDiffKind kind,
        DiffLoadMode mode,
        CancellationToken cancellationToken = default) =>
        ReadDiffAsync(repository, change, kind, cancellationToken);
}
