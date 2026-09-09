using System.Collections.ObjectModel;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public enum RepositoryTreeNodeKind
{
    WorkingTree,
    Group,
    BranchFolder,
    LocalBranch,
    Remote,
    RemoteBranch,
    Tag,
    Stash
}

public sealed class RepositoryTreeNode
{
    public RepositoryTreeNode(
        RepositoryTreeNodeKind kind,
        string name,
        string? referenceName = null,
        object? value = null,
        bool isCurrent = false,
        bool isExpanded = false,
        IEnumerable<RepositoryTreeNode>? children = null)
    {
        Kind = kind;
        Name = name;
        ReferenceName = referenceName;
        Value = value;
        IsCurrent = isCurrent;
        IsExpanded = isExpanded;

        if (children is null) return;

        var childNodes = children.ToList();
        if (childNodes.Count > 0 && childNodes.All(IsBranchNode))
            AddGroupedBranches(childNodes);
        else
            foreach (var child in childNodes) Children.Add(child);
    }

    public RepositoryTreeNodeKind Kind { get; }
    public string Name { get; }
    public string? ReferenceName { get; }
    public object? Value { get; }
    public bool IsCurrent { get; }
    public bool IsExpanded { get; private set; }
    public ObservableCollection<RepositoryTreeNode> Children { get; } = [];
    public string DisplayName => IsCurrent ? $"✓ {Name}" : Name;

    private static bool IsBranchNode(RepositoryTreeNode node) =>
        node.Kind is RepositoryTreeNodeKind.LocalBranch or RepositoryTreeNodeKind.RemoteBranch;

    private void AddGroupedBranches(IEnumerable<RepositoryTreeNode> branches)
    {
        foreach (var branch in branches)
        {
            var parts = branch.Name.Split('/');
            if (parts.Length == 1)
            {
                Children.Add(branch);
                continue;
            }

            AddBranch(Children, branch, parts, 0);
        }

        SortBranchNodes(Children);
    }

    private static void AddBranch(
        ObservableCollection<RepositoryTreeNode> nodes,
        RepositoryTreeNode branch,
        IReadOnlyList<string> parts,
        int index)
    {
        if (index == parts.Count - 1)
        {
            nodes.Add(new RepositoryTreeNode(
                branch.Kind,
                parts[index],
                branch.ReferenceName,
                branch.Value,
                branch.IsCurrent,
                branch.IsExpanded));
            return;
        }

        var folderName = parts[index];
        var folder = nodes.FirstOrDefault(node =>
            node.Kind == RepositoryTreeNodeKind.BranchFolder &&
            string.Equals(node.Name, folderName, StringComparison.Ordinal));

        if (folder is null)
        {
            folder = new RepositoryTreeNode(RepositoryTreeNodeKind.BranchFolder, folderName);
            nodes.Add(folder);
        }

        AddBranch(folder.Children, branch, parts, index + 1);
        if (branch.IsCurrent) folder.IsExpanded = true;
    }

    private static void SortBranchNodes(ObservableCollection<RepositoryTreeNode> nodes)
    {
        foreach (var folder in nodes.Where(node => node.Kind == RepositoryTreeNodeKind.BranchFolder))
            SortBranchNodes(folder.Children);

        var ordered = nodes
            .OrderByDescending(node => node.IsCurrent || node.Kind == RepositoryTreeNodeKind.BranchFolder && node.IsExpanded)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        nodes.Clear();
        foreach (var node in ordered) nodes.Add(node);
    }
}

public sealed record CommitFileRow(string Status, ChangedFile File)
{
    public string Path => File.Path;
    public string AddedDisplay => File.AddedLines is { } value ? $"+{value}" : string.Empty;
    public string RemovedDisplay => File.RemovedLines is { } value ? $"-{value}" : string.Empty;
}
