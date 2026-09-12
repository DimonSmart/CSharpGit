using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IWorktreeService
{
    Task<IReadOnlyList<WorktreeInfo>> ListAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Repository repository,
        string path,
        string branch,
        CancellationToken cancellationToken = default);

    Task AddNewBranchAsync(
        Repository repository,
        string path,
        string newBranch,
        string startPoint,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Repository repository,
        WorktreeInfo worktree,
        bool force = false,
        CancellationToken cancellationToken = default);

    Task LockAsync(
        Repository repository,
        WorktreeInfo worktree,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task UnlockAsync(
        Repository repository,
        WorktreeInfo worktree,
        CancellationToken cancellationToken = default);

    Task PruneAsync(
        Repository repository,
        CancellationToken cancellationToken = default);
}
