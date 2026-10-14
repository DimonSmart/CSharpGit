namespace CSharpGit.Presentation.ViewModels;

public static class TreeHierarchyGuideBuilder
{
    public static void Apply<TNode>(
        IEnumerable<TNode> roots,
        Func<TNode, IReadOnlyList<TNode>> getChildren,
        Action<TNode, IReadOnlyList<RepositoryTreeGuideSegmentKind>> setSegments)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(getChildren);
        ArgumentNullException.ThrowIfNull(setSegments);

        foreach (var root in roots)
        {
            setSegments(root, Array.Empty<RepositoryTreeGuideSegmentKind>());
            ApplyChildren(getChildren(root), Array.Empty<bool>(), getChildren, setSegments);
        }
    }

    private static void ApplyChildren<TNode>(
        IReadOnlyList<TNode> nodes,
        IReadOnlyList<bool> ancestorHasFollowingSiblings,
        Func<TNode, IReadOnlyList<TNode>> getChildren,
        Action<TNode, IReadOnlyList<RepositoryTreeGuideSegmentKind>> setSegments)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var isLastSibling = index == nodes.Count - 1;
            setSegments(node, RepositoryTreeGuideLayout.BuildSegments(ancestorHasFollowingSiblings, isLastSibling));

            var children = getChildren(node);
            if (children.Count == 0) continue;

            var childAncestors = new bool[ancestorHasFollowingSiblings.Count + 1];
            for (var ancestorIndex = 0; ancestorIndex < ancestorHasFollowingSiblings.Count; ancestorIndex++)
                childAncestors[ancestorIndex] = ancestorHasFollowingSiblings[ancestorIndex];
            childAncestors[^1] = !isLastSibling;
            ApplyChildren(children, childAncestors, getChildren, setSegments);
        }
    }
}
