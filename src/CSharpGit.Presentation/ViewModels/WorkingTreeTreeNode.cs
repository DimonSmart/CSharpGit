using System.ComponentModel;
using System.Runtime.CompilerServices;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed class WorkingTreeTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isBatchSelected;

    private WorkingTreeTreeNode(
        string displayName,
        string path,
        WorkingTreeChange? change,
        IReadOnlyList<WorkingTreeTreeNode> children,
        WorkingTreeDiffKind kind)
    {
        DisplayName = displayName;
        Path = path;
        Change = change;
        Children = children;
        Status = change is null
            ? string.Empty
            : (kind == WorkingTreeDiffKind.Unstaged ? change.WorkingTreeStatus : change.IndexStatus).ToString();
        _isExpanded = change is null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName { get; }
    public string Path { get; }
    public WorkingTreeChange? Change { get; }
    public IReadOnlyList<WorkingTreeTreeNode> Children { get; }
    public bool HasChildren => Children.Count > 0;
    public bool IsFolder => Change is null;
    public string Status { get; }
    public string ToolTipText => Change?.OriginalPath is { } originalPath
        ? $"{originalPath} → {Change.Path}"
        : Path;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public bool IsBatchSelected
    {
        get => _isBatchSelected;
        set => SetField(ref _isBatchSelected, value);
    }

    public IReadOnlyList<RepositoryTreeGuideSegmentKind> HierarchyGuideSegments { get; private set; } =
        Array.Empty<RepositoryTreeGuideSegmentKind>();

    public static IReadOnlyList<WorkingTreeTreeNode> Build(
        IEnumerable<WorkingTreeChange> changes,
        WorkingTreeDiffKind kind)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var filtered = kind == WorkingTreeDiffKind.Unstaged
            ? changes.Where(change => change.IsUnstaged)
            : changes.Where(change => change.IsStaged);
        var structure = PathTreeBuilder.Build(
            filtered,
            change => change.Path,
            new PathTreeBuildOptions(CollapseSingleChildFolderChains: true));
        var roots = structure.Select(node => ToPresentationNode(node, kind)).ToList();

        TreeHierarchyGuideBuilder.Apply(
            roots,
            node => node.Children,
            (node, segments) => node.HierarchyGuideSegments = segments);
        return roots;
    }

    private static WorkingTreeTreeNode ToPresentationNode(
        PathTreeNode<WorkingTreeChange> source,
        WorkingTreeDiffKind kind) =>
        new(
            source.DisplayName,
            source.Path,
            source.Item,
            source.Children.Select(child => ToPresentationNode(child, kind)).ToList(),
            kind);

    private void SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
