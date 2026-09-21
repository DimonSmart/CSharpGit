using System.Collections.ObjectModel;
using System.ComponentModel;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed record ChangedFileTreeEntry(string Status, ChangedFile File);

internal sealed record ChangedFileTreeDescriptor(
    string Key,
    string DisplayName,
    string Path,
    ChangedFileTreeEntry? Entry,
    int AddedLines,
    int RemovedLines,
    IReadOnlyList<ChangedFileTreeDescriptor> Children);

internal static class ChangedFileTreeDescriptorBuilder
{
    public static IReadOnlyList<ChangedFileTreeDescriptor> Build(IEnumerable<ChangedFileTreeEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var structure = PathTreeBuilder.Build(
            entries,
            entry => NormalizePath(entry.File.Path),
            new PathTreeBuildOptions(CollapseSingleChildFolderChains: true));
        return structure.Select(ToDescriptor).ToList();
    }

    private static ChangedFileTreeDescriptor ToDescriptor(PathTreeNode<ChangedFileTreeEntry> source)
    {
        var children = source.Children.Select(ToDescriptor).ToList();
        var addedLines = source.Item?.File.AddedLines ?? children.Sum(child => child.AddedLines);
        var removedLines = source.Item?.File.RemovedLines ?? children.Sum(child => child.RemovedLines);
        return new ChangedFileTreeDescriptor(
            BuildKey(source.Path, source.IsFolder),
            source.DisplayName,
            source.Path,
            source.Item,
            addedLines,
            removedLines,
            children);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
    private static string BuildKey(string path, bool isDirectory) => $"{(isDirectory ? "dir" : "file")}:{path}";
}

public sealed class ChangedFileTreeNode : INotifyPropertyChanged
{
    private string _displayName;
    private ChangedFileTreeEntry? _entry;
    private bool _isExpanded;
    private int _addedLines;
    private int _removedLines;

    internal ChangedFileTreeNode(ChangedFileTreeDescriptor descriptor)
    {
        Key = descriptor.Key;
        Path = descriptor.Path;
        _displayName = descriptor.DisplayName;
        _entry = descriptor.Entry;
        _addedLines = descriptor.AddedLines;
        _removedLines = descriptor.RemovedLines;
        _isExpanded = descriptor.Entry is null;
        Children.CollectionChanged += (_, _) => Notify(nameof(HasChildren));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal string Key { get; }
    public string DisplayName => _displayName;
    public string Path { get; }
    public ChangedFileTreeEntry? Entry => _entry;
    public ObservableCollection<ChangedFileTreeNode> Children { get; } = [];
    public bool HasChildren => Children.Count > 0;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            Notify(nameof(IsExpanded));
        }
    }
    public IReadOnlyList<RepositoryTreeGuideSegmentKind> HierarchyGuideSegments { get; private set; } =
        Array.Empty<RepositoryTreeGuideSegmentKind>();
    public int AddedLines => _addedLines;
    public int RemovedLines => _removedLines;
    public string Status => Entry?.Status ?? string.Empty;
    public string AddedDisplay => Entry is not null
        ? Entry.File.AddedLines is { } value ? $"+{value}" : string.Empty
        : AddedLines > 0 ? $"+{AddedLines}" : string.Empty;
    public string RemovedDisplay => Entry is not null
        ? Entry.File.RemovedLines is { } value ? $"-{value}" : string.Empty
        : RemovedLines > 0 ? $"-{RemovedLines}" : string.Empty;

    public static IReadOnlyList<ChangedFileTreeNode> Build(IEnumerable<ChangedFileTreeEntry> entries)
    {
        var roots = new ObservableCollection<ChangedFileTreeNode>();
        ChangedFileTreeSynchronizer.Reconcile(roots, entries);
        return roots.ToList();
    }

    internal void UpdateFrom(ChangedFileTreeDescriptor descriptor)
    {
        if (!string.Equals(Key, descriptor.Key, StringComparison.Ordinal))
            throw new InvalidOperationException("Changed-file tree node identity changed during reconciliation.");

        var oldDisplayName = DisplayName;
        var oldEntry = Entry;
        var oldStatus = Status;
        var oldAddedLines = AddedLines;
        var oldRemovedLines = RemovedLines;
        var oldAddedDisplay = AddedDisplay;
        var oldRemovedDisplay = RemovedDisplay;

        _displayName = descriptor.DisplayName;
        _entry = descriptor.Entry;
        _addedLines = descriptor.AddedLines;
        _removedLines = descriptor.RemovedLines;

        if (!string.Equals(oldDisplayName, DisplayName, StringComparison.Ordinal)) Notify(nameof(DisplayName));
        if (!ReferenceEquals(oldEntry?.File, Entry?.File)
            || !string.Equals(oldEntry?.Status, Entry?.Status, StringComparison.Ordinal))
            Notify(nameof(Entry));
        if (!string.Equals(oldStatus, Status, StringComparison.Ordinal)) Notify(nameof(Status));
        if (oldAddedLines != AddedLines) Notify(nameof(AddedLines));
        if (oldRemovedLines != RemovedLines) Notify(nameof(RemovedLines));
        if (!string.Equals(oldAddedDisplay, AddedDisplay, StringComparison.Ordinal)) Notify(nameof(AddedDisplay));
        if (!string.Equals(oldRemovedDisplay, RemovedDisplay, StringComparison.Ordinal)) Notify(nameof(RemovedDisplay));
    }

    internal void SetHierarchyGuideSegments(IReadOnlyList<RepositoryTreeGuideSegmentKind> segments)
    {
        if (HierarchyGuideSegments.SequenceEqual(segments)) return;
        HierarchyGuideSegments = segments;
        Notify(nameof(HierarchyGuideSegments));
    }

    private void Notify(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal static class ChangedFileTreeSynchronizer
{
    public static void Reconcile(
        ObservableCollection<ChangedFileTreeNode> roots,
        IEnumerable<ChangedFileTreeEntry> entries)
    {
        var desired = ChangedFileTreeDescriptorBuilder.Build(entries);
        IncrementalTreeReconciler.Reconcile(
            roots,
            desired,
            node => node.Key,
            descriptor => descriptor.Key,
            (node, descriptor) => node.UpdateFrom(descriptor),
            descriptor => new ChangedFileTreeNode(descriptor),
            node => node.Children,
            descriptor => descriptor.Children,
            StringComparer.Ordinal);

        TreeHierarchyGuideBuilder.Apply(
            roots,
            node => node.Children,
            (node, segments) => node.SetHierarchyGuideSegments(segments));
    }
}
