using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CSharpGit.Presentation.Controls;

namespace CSharpGit.Presentation;

internal static class WorkingTreeTreeViewExtensions
{
    public static void ScrollIntoView(this TreeView tree, object item)
    {
        object? treeItem = item;
        if (item is WorkingTreeChange change && tree.ItemsSource is IEnumerable<WorkingTreeTreeNode> roots)
            treeItem = FindLeaf(roots, change.Path);

        if (treeItem is not null && tree.ContainerFromItem(treeItem) is FrameworkElement container)
            container.StartBringIntoView();
    }

    private static WorkingTreeTreeNode? FindLeaf(IEnumerable<WorkingTreeTreeNode> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (node.Change is not null && string.Equals(node.Path, path, StringComparison.Ordinal)) return node;
            if (FindLeaf(node.Children, path) is { } match) return match;
        }

        return null;
    }
}
