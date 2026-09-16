using System.Collections.ObjectModel;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed record ChangedFileTreeEntry(string Status, ChangedFile File);

public sealed class ChangedFileTreeNode
{
    private ChangedFileTreeNode(
        string displayName,
        string path,
        ChangedFileTreeEntry? entry,
        IReadOnlyList<ChangedFileTreeNode> children)
    {
        DisplayName = displayName;
        Path = path;
        Entry = entry;
        Children = new ObservableCollection<ChangedFileTreeNode>(children);
        AddedLines = entry?.File.AddedLines ?? children.Sum(child => child.AddedLines);
        RemovedLines = entry?.File.RemovedLines ?? children.Sum(child => child.RemovedLines);
        IsExpanded = entry is null;
    }

    public string DisplayName { get; }
    public string Path { get; }
    public ChangedFileTreeEntry? Entry { get; }
    public ObservableCollection<ChangedFileTreeNode> Children { get; }
    public bool IsExpanded { get; set; }
    public int AddedLines { get; }
    public int RemovedLines { get; }
    public string Status => Entry?.Status ?? string.Empty;
    public string AddedDisplay => Entry is not null
        ? Entry.File.AddedLines is { } value ? $"+{value}" : string.Empty
        : AddedLines > 0 ? $"+{AddedLines}" : string.Empty;
    public string RemovedDisplay => Entry is not null
        ? Entry.File.RemovedLines is { } value ? $"-{value}" : string.Empty
        : RemovedLines > 0 ? $"-{RemovedLines}" : string.Empty;

    public static IReadOnlyList<ChangedFileTreeNode> Build(IEnumerable<ChangedFileTreeEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var structure = PathTreeBuilder.Build(
            entries,
            entry => entry.File.Path.Replace('\\', '/'),
            new PathTreeBuildOptions(CollapseSingleChildFolderChains: true));
        return structure.Select(ToPresentationNode).ToList();
    }

    private static ChangedFileTreeNode ToPresentationNode(PathTreeNode<ChangedFileTreeEntry> source) =>
        new(
            source.DisplayName,
            source.Path,
            source.Item,
            source.Children.Select(ToPresentationNode).ToList());
}
