namespace CSharpGit.Presentation.ViewModels;

internal sealed record RepositoryTreeSelectionAnchor<TNode>(
    TNode SelectedNode,
    IReadOnlyList<RepositoryTreeSelectionLevel> Levels);

internal readonly record struct RepositoryTreeSelectionLevel(
    string? NextSiblingKey,
    string? PreviousSiblingKey,
    string? ParentKey);

internal static class RepositoryTreeSelection
{
    public static RepositoryTreeSelectionAnchor<TNode>? Capture<TNode>(
        IReadOnlyList<TNode> roots,
        TNode? selected,
        Func<TNode, string> key,
        Func<TNode, IReadOnlyList<TNode>> children)
        where TNode : class
    {
        if (selected is null) return null;
        var path = new List<TNode>();
        if (!TryFindPath(roots, selected, children, path)) return null;

        var levels = new List<RepositoryTreeSelectionLevel>(path.Count);
        IReadOnlyList<TNode> siblings = roots;
        for (var index = 0; index < path.Count; index++)
        {
            var node = path[index];
            var siblingIndex = FindReferenceIndex(siblings, node);
            levels.Add(new RepositoryTreeSelectionLevel(
                siblingIndex >= 0 && siblingIndex + 1 < siblings.Count ? key(siblings[siblingIndex + 1]) : null,
                siblingIndex > 0 ? key(siblings[siblingIndex - 1]) : null,
                index > 0 ? key(path[index - 1]) : null));
            siblings = children(node);
        }

        return new RepositoryTreeSelectionAnchor<TNode>(selected, levels);
    }

    public static TNode? Resolve<TNode>(
        IReadOnlyList<TNode> roots,
        RepositoryTreeSelectionAnchor<TNode>? anchor,
        Func<TNode, string> key,
        Func<TNode, IReadOnlyList<TNode>> children)
        where TNode : class
    {
        if (anchor is null) return null;
        if (ContainsReference(roots, anchor.SelectedNode, children)) return anchor.SelectedNode;

        for (var index = anchor.Levels.Count - 1; index >= 0; index--)
        {
            var level = anchor.Levels[index];
            if (FindByKey(roots, level.NextSiblingKey, key, children) is { } next) return next;
            if (FindByKey(roots, level.PreviousSiblingKey, key, children) is { } previous) return previous;
            if (FindByKey(roots, level.ParentKey, key, children) is { } parent) return parent;
        }

        return null;
    }

    private static bool TryFindPath<TNode>(
        IReadOnlyList<TNode> nodes,
        TNode target,
        Func<TNode, IReadOnlyList<TNode>> children,
        List<TNode> path)
        where TNode : class
    {
        foreach (var node in nodes)
        {
            path.Add(node);
            if (ReferenceEquals(node, target)) return true;
            if (TryFindPath(children(node), target, children, path)) return true;
            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    private static int FindReferenceIndex<TNode>(IReadOnlyList<TNode> nodes, TNode target)
        where TNode : class
    {
        for (var index = 0; index < nodes.Count; index++)
            if (ReferenceEquals(nodes[index], target)) return index;
        return -1;
    }

    private static bool ContainsReference<TNode>(
        IReadOnlyList<TNode> nodes,
        TNode target,
        Func<TNode, IReadOnlyList<TNode>> children)
        where TNode : class
    {
        foreach (var node in nodes)
        {
            if (ReferenceEquals(node, target)) return true;
            if (ContainsReference(children(node), target, children)) return true;
        }

        return false;
    }

    private static TNode? FindByKey<TNode>(
        IReadOnlyList<TNode> nodes,
        string? wantedKey,
        Func<TNode, string> key,
        Func<TNode, IReadOnlyList<TNode>> children)
        where TNode : class
    {
        if (wantedKey is null) return null;
        foreach (var node in nodes)
        {
            if (string.Equals(key(node), wantedKey, StringComparison.Ordinal)) return node;
            if (FindByKey(children(node), wantedKey, key, children) is { } nested) return nested;
        }

        return null;
    }
}
