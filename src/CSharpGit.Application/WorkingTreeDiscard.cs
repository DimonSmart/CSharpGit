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
        Func<IReadOnlyCollection<WorkingTreeChange>, CancellationToken, Task> discardTrackedBatch,
        Func<WorkingTreeChange, CancellationToken, Task> discardSingle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(discardTrackedBatch);
        ArgumentNullException.ThrowIfNull(discardSingle);

        var results = new WorkingTreeDiscardResult?[request.Changes.Count];
        var tracked = new List<IndexedChange>();
        var single = new List<IndexedChange>();

        for (var index = 0; index < request.Changes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var change = request.Changes[index];

            if (!IsEligible(change))
            {
                results[index] = new WorkingTreeDiscardResult(
                    change,
                    WorkingTreeDiscardOutcome.Failed,
                    change.IsConflicted
                        ? "Conflict paths must be handled by the conflict workflow."
                        : "The path is not an unstaged working-tree change.");
                continue;
            }

            var item = new IndexedChange(index, change);
            if (IsBatchEligibleTracked(change))
                tracked.Add(item);
            else
                single.Add(item);
        }

        foreach (var chunk in CreateTrackedChunks(tracked))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var changes = chunk.Select(item => item.Change).ToArray();

            try
            {
                await discardTrackedBatch(changes, cancellationToken);
                foreach (var item in chunk)
                    results[item.Index] = new WorkingTreeDiscardResult(
                        item.Change,
                        WorkingTreeDiscardOutcome.Restored);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception batchException)
            {
                foreach (var item in chunk)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        await discardSingle(item.Change, cancellationToken);
                        results[item.Index] = CreateSuccessfulSingleResult(repository, item.Change);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        results[item.Index] = new WorkingTreeDiscardResult(
                            item.Change,
                            WorkingTreeDiscardOutcome.Failed,
                            string.IsNullOrWhiteSpace(exception.Message)
                                ? batchException.Message
                                : exception.Message);
                    }
                }
            }
        }

        foreach (var item in single)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await discardSingle(item.Change, cancellationToken);
                results[item.Index] = CreateSuccessfulSingleResult(repository, item.Change);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                results[item.Index] = new WorkingTreeDiscardResult(
                    item.Change,
                    WorkingTreeDiscardOutcome.Failed,
                    exception.Message);
            }
        }

        return results
            .Select(result => result ?? throw new InvalidOperationException("Discard did not produce a result for every confirmed path."))
            .ToArray();
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

    private static bool IsBatchEligibleTracked(WorkingTreeChange change) =>
        change.IndexStatus != '?' &&
        change.OriginalPath is null &&
        change.Kind != FileChangeKind.Renamed;

    private static IEnumerable<IReadOnlyList<IndexedChange>> CreateTrackedChunks(
        IReadOnlyList<IndexedChange> changes)
    {
        var budget = OperatingSystem.IsWindows() ? 24 * 1024 : 64 * 1024;
        const int fixedArgumentBudget = 256;

        var chunk = new List<IndexedChange>();
        var estimatedLength = fixedArgumentBudget;

        foreach (var item in changes)
        {
            var itemLength = EstimateSerializedPathLength(item.Change.Path);
            if (chunk.Count > 0 && estimatedLength + itemLength > budget)
            {
                yield return chunk.ToArray();
                chunk = [];
                estimatedLength = fixedArgumentBudget;
            }

            chunk.Add(item);
            estimatedLength += itemLength;
        }

        if (chunk.Count > 0)
            yield return chunk.ToArray();
    }

    private static int EstimateSerializedPathLength(string path) =>
        path.Length * 4 + 64;

    private static WorkingTreeDiscardResult CreateSuccessfulSingleResult(
        Repository repository,
        WorkingTreeChange change)
    {
        if (change.IndexStatus == '?' && PathStillExists(repository, change.Path))
        {
            return new WorkingTreeDiscardResult(
                change,
                WorkingTreeDiscardOutcome.Failed,
                DirectoryExists(repository, change.Path)
                    ? "Untracked directories are not removed recursively by Discard."
                    : "The untracked file still exists after deletion.");
        }

        if (change.WorkingTreeStatus == 'R' &&
            change.OriginalPath is not null &&
            PathStillExists(repository, change.Path))
        {
            return new WorkingTreeDiscardResult(
                change,
                WorkingTreeDiscardOutcome.Failed,
                "The renamed destination still exists after restoring the index state.");
        }

        return new WorkingTreeDiscardResult(
            change,
            change.IndexStatus == '?'
                ? WorkingTreeDiscardOutcome.Deleted
                : WorkingTreeDiscardOutcome.Restored);
    }

    private sealed record IndexedChange(int Index, WorkingTreeChange Change);

    private static bool PathStillExists(Repository repository, string relativePath)
    {
        var path = Path.Combine(repository.WorkingDirectory, relativePath);
        return File.Exists(path) || Directory.Exists(path);
    }

    private static bool DirectoryExists(Repository repository, string relativePath) =>
        Directory.Exists(Path.Combine(repository.WorkingDirectory, relativePath));
}
