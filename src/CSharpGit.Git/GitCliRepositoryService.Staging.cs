using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed partial class GitCliRepositoryService
{
    public Task StageFilesAsync(
        Repository repository,
        IReadOnlyCollection<WorkingTreeChange> changes,
        CancellationToken cancellationToken = default)
    {
        var arguments = BatchPathArguments("add", changes);
        return RunGitForMutationAsync(repository, cancellationToken, arguments);
    }

    public async Task UnstageFilesAsync(
        Repository repository,
        IReadOnlyCollection<WorkingTreeChange> changes,
        CancellationToken cancellationToken = default)
    {
        var paths = BatchPaths(changes);
        if (await HasHeadAsync(repository, cancellationToken))
        {
            await RunGitForMutationAsync(repository, cancellationToken, ["restore", "--staged", "--", .. paths]);
            return;
        }

        await RunGitForMutationAsync(repository, cancellationToken, ["rm", "--cached", "--force", "--ignore-unmatch", "--", .. paths]);
    }

    public async Task UnstageAllAsync(Repository repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (await HasHeadAsync(repository, cancellationToken))
        {
            await RunGitForMutationAsync(repository, cancellationToken, "reset", "--mixed");
            return;
        }

        // Unborn HEAD has no tree to reset the index to. Removing every index entry
        // with --cached preserves the working-tree bytes while making the index empty.
        await RunGitForMutationAsync(repository, cancellationToken, "rm", "--cached", "-r", "--force", "--ignore-unmatch", "--", ".");
    }

    private async Task<bool> HasHeadAsync(Repository repository, CancellationToken cancellationToken) =>
        await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken, "rev-parse", "--verify", "HEAD") is { Length: > 0 };

    private static string[] BatchPathArguments(string command, IReadOnlyCollection<WorkingTreeChange> changes) =>
        [command, "--", .. BatchPaths(changes)];

    private static string[] BatchPaths(IReadOnlyCollection<WorkingTreeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0) throw new ArgumentException("At least one working-tree change is required.", nameof(changes));

        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            ValidateChange(change);
            if (seen.Add(change.Path)) paths.Add(change.Path);
            if (change.OriginalPath is { } originalPath && seen.Add(originalPath)) paths.Add(originalPath);
        }

        return [.. paths];
    }
}
