using CSharpGit.Domain;

namespace CSharpGit.Application;

public enum WorkingTreeDiscardScope
{
    Selected,
    All
}

public enum WorkingTreeDiscardOutcome
{
    Restored,
    Deleted,
    Failed
}

public sealed record WorkingTreeDiscardRequest(
    WorkingTreeDiscardScope Scope,
    IReadOnlyList<WorkingTreeChange> Changes)
{
    public int UntrackedCount => Changes.Count(IsUntracked);

    public string ConfirmationMessage
    {
        get
        {
            var count = Changes.Count;
            var files = count == 1 ? "file" : "files";
            var firstLine = Scope switch
            {
                WorkingTreeDiscardScope.Selected when count == 1 =>
                    $"Discard unstaged changes in '{Changes[0].Path}'?",
                WorkingTreeDiscardScope.Selected => $"Discard changes in {count} selected files?",
                _ => $"Discard changes in {count} {files}?"
            };

            if (UntrackedCount == 0) return firstLine;

            var untrackedFiles = UntrackedCount == 1 ? "file" : "files";
            return $"{firstLine}{Environment.NewLine}{UntrackedCount} untracked {untrackedFiles} will be permanently deleted.";
        }
    }

    private static bool IsUntracked(WorkingTreeChange change) => change.IndexStatus == '?';
}

public sealed record WorkingTreeDiscardResult(
    WorkingTreeChange Change,
    WorkingTreeDiscardOutcome Outcome,
    string? ErrorMessage = null)
{
    public string Path => Change.Path;
}

public static class WorkingTreeDiscard
{
    public static bool IsEligible(WorkingTreeChange change) =>
        change.IsUnstaged && !change.IsConflicted;

    public static bool CanDiscardSelected(IEnumerable<WorkingTreeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var snapshot = changes.ToArray();
        return snapshot.Length > 0 && snapshot.All(IsEligible);
    }

    public static WorkingTreeDiscardRequest? CreateSelected(IEnumerable<WorkingTreeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var snapshot = changes.ToArray();
        return snapshot.Length > 0 && snapshot.All(IsEligible)
            ? CreateRequest(WorkingTreeDiscardScope.Selected, snapshot)
            : null;
    }

    public static WorkingTreeDiscardRequest? CreateAll(IEnumerable<WorkingTreeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var snapshot = changes.Where(IsEligible).ToArray();
        return snapshot.Length == 0
            ? null
            : CreateRequest(WorkingTreeDiscardScope.All, snapshot);
    }

    public static async Task<IReadOnlyList<WorkingTreeDiscardResult>> ExecuteAsync(
        Repository repository,
        WorkingTreeDiscardRequest request,
        Func<WorkingTreeChange, CancellationToken, Task> discard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(discard);

        var results = new List<WorkingTreeDiscardResult>(request.Changes.Count);
        foreach (var change in request.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsEligible(change))
            {
                results.Add(new WorkingTreeDiscardResult(
                    change,
                    WorkingTreeDiscardOutcome.Failed,
                    change.IsConflicted
                        ? "Conflict paths must be handled by the conflict workflow."
                        : "The path is not an unstaged working-tree change."));
                continue;
            }

            try
            {
                await discard(change, cancellationToken);

                if (change.IndexStatus == '?' && PathStillExists(repository, change.Path))
                {
                    results.Add(new WorkingTreeDiscardResult(
                        change,
                        WorkingTreeDiscardOutcome.Failed,
                        DirectoryExists(repository, change.Path)
                            ? "Untracked directories are not removed recursively by Discard."
                            : "The untracked file still exists after deletion."));
                    continue;
                }

                if (change.WorkingTreeStatus == 'R' &&
                    change.OriginalPath is not null &&
                    PathStillExists(repository, change.Path))
                {
                    results.Add(new WorkingTreeDiscardResult(
                        change,
                        WorkingTreeDiscardOutcome.Failed,
                        "The renamed destination still exists after restoring the index state."));
                    continue;
                }

                results.Add(new WorkingTreeDiscardResult(
                    change,
                    change.IndexStatus == '?'
                        ? WorkingTreeDiscardOutcome.Deleted
                        : WorkingTreeDiscardOutcome.Restored));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(new WorkingTreeDiscardResult(
                    change,
                    WorkingTreeDiscardOutcome.Failed,
                    exception.Message));
            }
        }

        return results;
    }

    public static string FormatFailures(IEnumerable<WorkingTreeDiscardResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var failed = results.Where(result => result.Outcome == WorkingTreeDiscardOutcome.Failed).ToArray();
        if (failed.Length == 0) return string.Empty;

        var files = failed.Length == 1 ? "file" : "files";
        return $"Could not discard {failed.Length} {files}:{Environment.NewLine}" +
               string.Join(
                   Environment.NewLine,
                   failed.Select(result => $"{result.Path}: {result.ErrorMessage ?? "Unknown error"}"));
    }

    private static WorkingTreeDiscardRequest CreateRequest(
        WorkingTreeDiscardScope scope,
        WorkingTreeChange[] snapshot) =>
        new(scope, Array.AsReadOnly(snapshot));

    private static bool PathStillExists(Repository repository, string relativePath)
    {
        var path = Path.Combine(repository.WorkingDirectory, relativePath);
        return File.Exists(path) || Directory.Exists(path);
    }

    private static bool DirectoryExists(Repository repository, string relativePath) =>
        Directory.Exists(Path.Combine(repository.WorkingDirectory, relativePath));
}
