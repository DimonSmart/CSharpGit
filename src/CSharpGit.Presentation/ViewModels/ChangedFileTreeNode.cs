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

        var roots = new List<BuilderNode>();
        foreach (var entry in entries
                     .GroupBy(item => item.File.Path, StringComparer.Ordinal)
                     .Select(group => group.First())
                     .OrderBy(item => item.File.Path, StringComparer.OrdinalIgnoreCase))
        {
            var normalizedPath = entry.File.Path.Replace('\\', '/');
            var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var current = roots;
            var pathParts = new List<string>(parts.Length);
            for (var index = 0; index < parts.Length - 1; index++)
            {
                pathParts.Add(parts[index]);
                var folderPath = string.Join('/', pathParts);
                var folder = current.FirstOrDefault(node =>
                    node.Entry is null && string.Equals(node.Name, parts[index], StringComparison.Ordinal));
                if (folder is null)
                {
                    folder = new BuilderNode(parts[index], folderPath, null);
                    current.Add(folder);
                }
                current = folder.Children;
            }

            current.Add(new BuilderNode(parts[^1], normalizedPath, entry));
        }

        return Sort(roots).Select(ToPresentationNode).ToList();
    }

    private static ChangedFileTreeNode ToPresentationNode(BuilderNode source)
    {
        var displayName = source.Name;
        var path = source.Path;
        var entry = source.Entry;
        var children = Sort(source.Children).Select(ToPresentationNode).ToList();

        while (entry is null && children.Count == 1 && children[0].Entry is null)
        {
            var onlyChild = children[0];
            displayName = $"{displayName}/{onlyChild.DisplayName}";
            path = onlyChild.Path;
            children = onlyChild.Children.ToList();
        }

        return new ChangedFileTreeNode(displayName, path, entry, children);
    }

    private static IOrderedEnumerable<BuilderNode> Sort(IEnumerable<BuilderNode> nodes) =>
        nodes.OrderBy(node => node.Entry is null ? 0 : 1)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase);

    private sealed class BuilderNode(string name, string path, ChangedFileTreeEntry? entry)
    {
        public string Name { get; } = name;
        public string Path { get; } = path;
        public ChangedFileTreeEntry? Entry { get; } = entry;
        public List<BuilderNode> Children { get; } = [];
    }
}
