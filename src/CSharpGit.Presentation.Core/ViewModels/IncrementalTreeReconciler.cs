using System.Collections.ObjectModel;

namespace CSharpGit.Presentation.ViewModels;

internal static class IncrementalTreeReconciler
{
    public static void Reconcile<TKey, TNode, TDesired>(
        ObservableCollection<TNode> target,
        IReadOnlyList<TDesired> desired,
        Func<TNode, TKey> nodeKey,
        Func<TDesired, TKey> desiredKey,
        Action<TNode, TDesired> update,
        Func<TDesired, TNode> create,
        Func<TNode, ObservableCollection<TNode>> children,
        Func<TDesired, IReadOnlyList<TDesired>> desiredChildren,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        comparer ??= EqualityComparer<TKey>.Default;
        ReconcileCore(target, desired, nodeKey, desiredKey, update, create, children, desiredChildren, comparer);
    }

    private static void ReconcileCore<TKey, TNode, TDesired>(
        ObservableCollection<TNode> target,
        IReadOnlyList<TDesired> desired,
        Func<TNode, TKey> nodeKey,
        Func<TDesired, TKey> desiredKey,
        Action<TNode, TDesired> update,
        Func<TDesired, TNode> create,
        Func<TNode, ObservableCollection<TNode>> children,
        Func<TDesired, IReadOnlyList<TDesired>> desiredChildren,
        IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        var desiredKeys = new HashSet<TKey>(comparer);
        foreach (var item in desired)
        {
            if (!desiredKeys.Add(desiredKey(item)))
                throw new InvalidOperationException("Desired repository tree contains duplicate sibling keys.");
        }

        var existingByKey = new Dictionary<TKey, TNode>(comparer);
        foreach (var item in target)
        {
            if (!existingByKey.TryAdd(nodeKey(item), item))
                throw new InvalidOperationException("Existing repository tree contains duplicate sibling keys.");
        }

        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (desiredKeys.Contains(nodeKey(target[index]))) continue;
            existingByKey.Remove(nodeKey(target[index]));
            target.RemoveAt(index);
        }

        var positions = BuildPositions(target, nodeKey, comparer);
        for (var desiredIndex = 0; desiredIndex < desired.Count; desiredIndex++)
        {
            var desiredItem = desired[desiredIndex];
            var key = desiredKey(desiredItem);
            if (!existingByKey.TryGetValue(key, out var node))
            {
                node = create(desiredItem);
                target.Insert(desiredIndex, node);
                existingByKey.Add(key, node);
                RefreshPositions(positions, target, nodeKey, desiredIndex, target.Count - 1);
            }
            else
            {
                var currentIndex = positions[key];
                if (currentIndex != desiredIndex)
                {
                    target.Move(currentIndex, desiredIndex);
                    RefreshPositions(
                        positions,
                        target,
                        nodeKey,
                        Math.Min(currentIndex, desiredIndex),
                        Math.Max(currentIndex, desiredIndex));
                }
            }

            update(node, desiredItem);
            ReconcileCore(
                children(node),
                desiredChildren(desiredItem),
                nodeKey,
                desiredKey,
                update,
                create,
                children,
                desiredChildren,
                comparer);
        }
    }

    private static Dictionary<TKey, int> BuildPositions<TKey, TNode>(
        IReadOnlyList<TNode> nodes,
        Func<TNode, TKey> nodeKey,
        IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, int>(nodes.Count, comparer);
        for (var index = 0; index < nodes.Count; index++) result[nodeKey(nodes[index])] = index;
        return result;
    }

    private static void RefreshPositions<TKey, TNode>(
        IDictionary<TKey, int> positions,
        IReadOnlyList<TNode> nodes,
        Func<TNode, TKey> nodeKey,
        int start,
        int end)
        where TKey : notnull
    {
        for (var index = start; index <= end; index++) positions[nodeKey(nodes[index])] = index;
    }
}
