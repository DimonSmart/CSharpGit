namespace CSharpGit.Presentation.ViewModels;

internal static class WorkingTreeTreeExpansionState
{
    public static void Capture(
        IEnumerable<WorkingTreeTreeNode> nodes,
        IDictionary<string, bool> state)
    {
        foreach (var node in nodes)
        {
            if (node.IsFolder) state[node.Path] = node.IsExpanded;
            Capture(node.Children, state);
        }
    }

    public static void Restore(
        IEnumerable<WorkingTreeTreeNode> nodes,
        IReadOnlyDictionary<string, bool> state)
    {
        foreach (var node in nodes)
        {
            if (node.IsFolder && state.TryGetValue(node.Path, out var expanded)) node.IsExpanded = expanded;
            Restore(node.Children, state);
        }
    }
}
