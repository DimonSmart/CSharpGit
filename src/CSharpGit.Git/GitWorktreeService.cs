using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitWorktreeService(GitCommandExecutor executor) : IWorktreeService
{
    public async Task<IReadOnlyList<WorktreeInfo>> ListAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var output = await executor.ExecuteAsync(
            repository.WorkingDirectory,
            "list worktrees",
            cancellationToken,
            "worktree", "list", "--porcelain", "-z");
        return GitWorktreeParser.Parse(output, repository);
    }

    public Task AddAsync(
        Repository repository,
        string path,
        string branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        var destination = PrepareDestination(repository, path);
        return executor.ExecuteAsync(
            repository.WorkingDirectory,
            "add worktree",
            cancellationToken,
            "worktree", "add", destination, branch);
    }

    public Task AddNewBranchAsync(
        Repository repository,
        string path,
        string newBranch,
        string startPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(newBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(startPoint);
        var destination = PrepareDestination(repository, path);
        return executor.ExecuteAsync(
            repository.WorkingDirectory,
            "add worktree with new branch",
            cancellationToken,
            "worktree", "add", "-b", newBranch, destination, startPoint);
    }

    public Task RemoveAsync(
        Repository repository,
        WorktreeInfo worktree,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(worktree);
        if (worktree.IsCurrent)
            throw new InvalidOperationException("The current worktree cannot be removed from its own CSharpGit window.");

        var arguments = force
            ? new[] { "worktree", "remove", "--force", worktree.Path }
            : new[] { "worktree", "remove", worktree.Path };
        return executor.ExecuteAsync(
            repository.WorkingDirectory,
            force ? "force remove worktree" : "remove worktree",
            cancellationToken,
            arguments);
    }

    public Task LockAsync(
        Repository repository,
        WorktreeInfo worktree,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(worktree);
        var arguments = string.IsNullOrWhiteSpace(reason)
            ? new[] { "worktree", "lock", worktree.Path }
            : new[] { "worktree", "lock", "--reason", reason.Trim(), worktree.Path };
        return executor.ExecuteAsync(
            repository.WorkingDirectory,
            "lock worktree",
            cancellationToken,
            arguments);
    }

    public Task UnlockAsync(
        Repository repository,
        WorktreeInfo worktree,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(worktree);
        return executor.ExecuteAsync(
            repository.WorkingDirectory,
            "unlock worktree",
            cancellationToken,
            "worktree", "unlock", worktree.Path);
    }

    public Task PruneAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return executor.ExecuteAsync(
            repository.WorkingDirectory,
            "prune worktrees",
            cancellationToken,
            "worktree", "prune");
    }

    private static string PrepareDestination(Repository repository, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, repository.WorkingDirectory);
        fullPath = Path.TrimEndingDirectorySeparator(fullPath);
        if (Directory.Exists(fullPath) || File.Exists(fullPath))
            throw new InvalidOperationException($"The worktree destination already exists: {fullPath}");
        return fullPath;
    }
}
