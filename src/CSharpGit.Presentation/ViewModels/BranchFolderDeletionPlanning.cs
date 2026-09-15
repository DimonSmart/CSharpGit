using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

internal enum BranchFolderDeletionSkipReason
{
    CurrentBranch,
    UsedByWorktree
}

internal sealed record BranchFolderDeletionCandidate(
    GitBranch Branch,
    string? AssociatedWorktreePath);

internal sealed record BranchFolderDeletionSkippedBranch(
    BranchFolderDeletionCandidate Candidate,
    BranchFolderDeletionSkipReason Reason)
{
    public string BranchName => Candidate.Branch.Name;
}

internal sealed record BranchFolderDeletionPlan(
    IReadOnlyList<BranchFolderDeletionCandidate> Branches,
    IReadOnlyList<BranchFolderDeletionCandidate> EligibleBranches,
    IReadOnlyList<BranchFolderDeletionSkippedBranch> SkippedBranches)
{
    public int Total => Branches.Count;
    public int Skipped => SkippedBranches.Count;
    public int Attempted => EligibleBranches.Count;
}

internal sealed record RemoteBranchFolderDeletionTarget(
    GitBranch Branch,
    string RelativeBranchName)
{
    public string BranchName => Branch.Name;
}

internal sealed record BranchDeletionFailure(string BranchName, string Message);

internal sealed record BranchFolderDeletionExecutionResult(
    IReadOnlyList<string> SuccessfulBranches,
    IReadOnlyList<BranchDeletionFailure> Failures);

internal static class BranchFolderDeletionPlanner
{
    public static IReadOnlyList<BranchFolderDeletionCandidate> Collect<TNode>(
        TNode root,
        Func<TNode, IEnumerable<TNode>> childrenSelector,
        Func<TNode, BranchFolderDeletionCandidate?> candidateSelector)
    {
        var result = new List<BranchFolderDeletionCandidate>();
        CollectCore(root, childrenSelector, candidateSelector, result);
        return result.ToArray();
    }

    public static BranchFolderDeletionPlan CreateLocal(
        IEnumerable<BranchFolderDeletionCandidate> candidates)
    {
        var branches = Order(candidates).ToArray();
        var eligible = new List<BranchFolderDeletionCandidate>(branches.Length);
        var skipped = new List<BranchFolderDeletionSkippedBranch>();

        foreach (var candidate in branches)
        {
            if (candidate.Branch.IsCurrent)
            {
                skipped.Add(new BranchFolderDeletionSkippedBranch(
                    candidate,
                    BranchFolderDeletionSkipReason.CurrentBranch));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(candidate.AssociatedWorktreePath))
            {
                skipped.Add(new BranchFolderDeletionSkippedBranch(
                    candidate,
                    BranchFolderDeletionSkipReason.UsedByWorktree));
                continue;
            }

            eligible.Add(candidate);
        }

        return new BranchFolderDeletionPlan(branches, eligible.ToArray(), skipped.ToArray());
    }

    public static IReadOnlyList<RemoteBranchFolderDeletionTarget> CreateRemoteTargets(
        BranchFolderInfo folderInfo,
        IEnumerable<BranchFolderDeletionCandidate> candidates)
    {
        if (folderInfo.Scope != BranchFolderScope.Remote || string.IsNullOrWhiteSpace(folderInfo.RemoteName))
            throw new InvalidOperationException("Remote branch folder does not contain a configured remote name.");

        var remotePrefix = folderInfo.RemoteName + "/";
        var result = new List<RemoteBranchFolderDeletionTarget>();
        foreach (var candidate in Order(candidates))
        {
            var branchName = candidate.Branch.Name;
            if (!branchName.StartsWith(remotePrefix, StringComparison.Ordinal)
                || branchName.Length == remotePrefix.Length)
            {
                throw new InvalidOperationException(
                    $"Remote branch '{branchName}' does not belong to configured remote '{folderInfo.RemoteName}'.");
            }

            result.Add(new RemoteBranchFolderDeletionTarget(
                candidate.Branch,
                branchName[remotePrefix.Length..]));
        }

        return result.ToArray();
    }

    private static void CollectCore<TNode>(
        TNode node,
        Func<TNode, IEnumerable<TNode>> childrenSelector,
        Func<TNode, BranchFolderDeletionCandidate?> candidateSelector,
        ICollection<BranchFolderDeletionCandidate> result)
    {
        foreach (var child in childrenSelector(node))
        {
            if (candidateSelector(child) is { } candidate)
                result.Add(candidate);

            CollectCore(child, childrenSelector, candidateSelector, result);
        }
    }

    private static IOrderedEnumerable<BranchFolderDeletionCandidate> Order(
        IEnumerable<BranchFolderDeletionCandidate> candidates) =>
        candidates
            .OrderBy(candidate => candidate.Branch.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Branch.Name, StringComparer.Ordinal);
}

internal static class BranchFolderDeletionExecutor
{
    public static async Task<BranchFolderDeletionExecutionResult> ExecuteAsync<T>(
        IReadOnlyList<T> items,
        Func<T, string> branchNameSelector,
        Func<T, Task> deleteAsync)
    {
        var successfulBranches = new List<string>(items.Count);
        var failures = new List<BranchDeletionFailure>();

        foreach (var item in items)
        {
            var branchName = branchNameSelector(item);
            try
            {
                await deleteAsync(item);
                successfulBranches.Add(branchName);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(new BranchDeletionFailure(branchName, exception.Message));
            }
        }

        return new BranchFolderDeletionExecutionResult(
            successfulBranches.ToArray(),
            failures.ToArray());
    }
}
