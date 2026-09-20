using CSharpGit.Domain;

namespace CSharpGit.Application;

public static class RepositoryStateComparer
{
    public static bool HasSameGitState(RepositoryState left, RepositoryState right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return left.Repository == right.Repository &&
               StringComparer.Ordinal.Equals(left.HeadReference, right.HeadReference) &&
               StringComparer.Ordinal.Equals(left.HeadCommit, right.HeadCommit) &&
               left.IsDetached == right.IsDetached &&
               left.Operation == right.Operation &&
               OperationsEqual(left.CurrentOperation, right.CurrentOperation) &&
               NamedItemsEqual(left.Changes, right.Changes, ChangeKey) &&
               ReferencesEqual(left.Refs, right.Refs) &&
               NamedItemsEqual(left.Stashes, right.Stashes, stash => stash.Name) &&
               DictionariesEqual(left.GlobalConfiguration, right.GlobalConfiguration) &&
               DictionariesEqual(left.LocalConfiguration, right.LocalConfiguration);
    }

    private static bool ReferencesEqual(GitReferences left, GitReferences right) =>
        NamedItemsEqual(left.LocalBranches, right.LocalBranches, branch => branch.Name) &&
        NamedItemsEqual(left.RemoteBranches, right.RemoteBranches, branch => branch.Name) &&
        NamedItemsEqual(left.Remotes, right.Remotes, remote => remote.Name) &&
        NamedItemsEqual(left.Tags, right.Tags, tag => tag.Name);

    private static bool OperationsEqual(RepositoryOperationState left, RepositoryOperationState right) =>
        left.Kind == right.Kind &&
        left.CanContinue == right.CanContinue &&
        left.CanAbort == right.CanAbort &&
        left.CanSkip == right.CanSkip &&
        NamedItemsEqual(left.Conflicts, right.Conflicts, conflict => conflict.Path);

    private static string ChangeKey(WorkingTreeChange change) =>
        change.OriginalPath is null ? change.Path : change.Path + "\0" + change.OriginalPath;

    private static bool NamedItemsEqual<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, string> keySelector)
        where T : notnull
    {
        if (left.Count != right.Count) return false;

        var rightByKey = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in right)
        {
            if (!rightByKey.TryAdd(keySelector(item), item)) return false;
        }

        var matchedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in left)
        {
            var key = keySelector(item);
            if (!matchedKeys.Add(key) ||
                !rightByKey.TryGetValue(key, out var matchingItem) ||
                !EqualityComparer<T>.Default.Equals(item, matchingItem))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DictionariesEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count &&
        left.All(pair => right.Any(candidate =>
            StringComparer.Ordinal.Equals(candidate.Key, pair.Key) &&
            StringComparer.Ordinal.Equals(candidate.Value, pair.Value)));
}
