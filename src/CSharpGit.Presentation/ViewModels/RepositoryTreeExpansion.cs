namespace CSharpGit.Presentation.ViewModels;

internal static class RepositoryTreeExpansion
{
    public static bool ExpandPathToReference<TNode>(
        TNode node,
        string referenceName,
        Func<TNode, RepositoryTreeNodeKind> kind,
        Func<TNode, string?> reference,
        Func<TNode, IEnumerable<TNode>> children,
        Action<TNode> expand)
    {
        foreach (var child in children(node))
        {
            if (kind(child) == RepositoryTreeNodeKind.LocalBranch &&
                string.Equals(reference(child), referenceName, StringComparison.Ordinal))
                return true;

            if (kind(child) != RepositoryTreeNodeKind.BranchFolder ||
                !ExpandPathToReference(child, referenceName, kind, reference, children, expand))
                continue;

            expand(child);
            return true;
        }

        return false;
    }
}
