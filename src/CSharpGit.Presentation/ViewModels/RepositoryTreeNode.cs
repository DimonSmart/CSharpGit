using System.Collections.ObjectModel;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public enum RepositoryTreeNodeKind
{
    WorkingTree,
    Group,
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
        if (children is not null)
            foreach (var child in children) Children.Add(child);
    }

    public RepositoryTreeNodeKind Kind { get; }
    public string Name { get; }
    public string? ReferenceName { get; }
    public object? Value { get; }
    public bool IsCurrent { get; }
    public bool IsExpanded { get; }
    public ObservableCollection<RepositoryTreeNode> Children { get; } = [];
    public string DisplayName => IsCurrent ? $"✓ {Name}" : Name;
}

public sealed record CommitFileRow(string Status, ChangedFile File)
{
    public string Path => File.Path;
    public string AddedDisplay => File.AddedLines is { } value ? $"+{value}" : string.Empty;
    public string RemovedDisplay => File.RemovedLines is { } value ? $"-{value}" : string.Empty;
}
