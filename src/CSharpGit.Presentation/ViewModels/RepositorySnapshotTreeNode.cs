using System.Collections.ObjectModel;
using System.ComponentModel;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.ViewModels;

internal sealed record RepositorySnapshotTreeDescriptor(
    string Key,
    string DisplayName,
    string Path,
    RepositorySnapshotEntry? Entry,
    IReadOnlyList<RepositorySnapshotTreeDescriptor> Children);

internal static class RepositorySnapshotTreeDescriptorBuilder
{
    public static IReadOnlyList<RepositorySnapshotTreeDescriptor> Build(
        IEnumerable<RepositorySnapshotEntry> entries,
        string? nameQuery = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var query = string.IsNullOrWhiteSpace(nameQuery) ? null : nameQuery.Trim();
        var filtered = entries.Where(entry =>
            query is null || entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
        var structure = PathTreeBuilder.Build(filtered, entry => NormalizePath(entry.Path));
        return structure.Select(ToDescriptor).ToList();
    }

    private static RepositorySnapshotTreeDescriptor ToDescriptor(PathTreeNode<RepositorySnapshotEntry> source) =>
        new(
            BuildKey(source.Path, source.IsFolder),
            source.DisplayName,
            source.Path,
            source.Item,
            source.Children.Select(ToDescriptor).ToList());

    private static string NormalizePath(string path) => path.Replace('\\', '/');
    private static string BuildKey(string path, bool isDirectory) => $"{(isDirectory ? "dir" : "file")}:{path}";
}

public sealed class RepositorySnapshotTreeNode : INotifyPropertyChanged
{
    private string _displayName;
    private RepositorySnapshotEntry? _entry;
    private bool _isExpanded;

    internal RepositorySnapshotTreeNode(RepositorySnapshotTreeDescriptor descriptor)
    {
        Key = descriptor.Key;
        Path = descriptor.Path;
        _displayName = descriptor.DisplayName;
        _entry = descriptor.Entry;
        Children.CollectionChanged += (_, _) => Notify(nameof(HasChildren));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal string Key { get; }
    public string DisplayName => _displayName;
    public string Path { get; }
    public RepositorySnapshotEntry? Entry => _entry;
    public ObservableCollection<RepositorySnapshotTreeNode> Children { get; } = [];
    public bool HasChildren => Children.Count > 0;
    public bool IsDirectory => Entry is null;
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
    public string DisplayLabel => Entry?.Kind switch
    {
        RepositorySnapshotEntryKind.Symlink => $"↗ {DisplayName}",
        RepositorySnapshotEntryKind.Submodule => $"▣ {DisplayName}",
        RepositorySnapshotEntryKind.Unsupported => $"? {DisplayName}",
        _ => DisplayName
    };
    public string IconGlyph => IsDirectory ? "\uE8B7" : "\uE8A5";

    public static IReadOnlyList<RepositorySnapshotTreeNode> Build(
        IEnumerable<RepositorySnapshotEntry> entries,
        string? nameQuery = null)
    {
        var roots = new ObservableCollection<RepositorySnapshotTreeNode>();
        RepositorySnapshotTreeSynchronizer.Reconcile(roots, entries, nameQuery);
        return roots.ToList();
    }

    internal void UpdateFrom(RepositorySnapshotTreeDescriptor descriptor)
    {
        if (!string.Equals(Key, descriptor.Key, StringComparison.Ordinal))
            throw new InvalidOperationException("Repository snapshot tree node identity changed during reconciliation.");

        var oldDisplayName = DisplayName;
        var oldEntry = Entry;
        var oldDisplayLabel = DisplayLabel;
        var oldIconGlyph = IconGlyph;

        _displayName = descriptor.DisplayName;
        _entry = descriptor.Entry;

        if (!string.Equals(oldDisplayName, DisplayName, StringComparison.Ordinal)) Notify(nameof(DisplayName));
        if (!ReferenceEquals(oldEntry, Entry)) Notify(nameof(Entry));
        if (!string.Equals(oldDisplayLabel, DisplayLabel, StringComparison.Ordinal)) Notify(nameof(DisplayLabel));
        if (!string.Equals(oldIconGlyph, IconGlyph, StringComparison.Ordinal)) Notify(nameof(IconGlyph));
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

internal static class RepositorySnapshotTreeSynchronizer
{
    public static void Reconcile(
        ObservableCollection<RepositorySnapshotTreeNode> roots,
        IEnumerable<RepositorySnapshotEntry> entries,
        string? nameQuery = null)
    {
        var desired = RepositorySnapshotTreeDescriptorBuilder.Build(entries, nameQuery);
        IncrementalTreeReconciler.Reconcile(
            roots,
            desired,
            node => node.Key,
            descriptor => descriptor.Key,
            (node, descriptor) => node.UpdateFrom(descriptor),
            descriptor => new RepositorySnapshotTreeNode(descriptor),
            node => node.Children,
            descriptor => descriptor.Children,
            StringComparer.Ordinal);

        TreeHierarchyGuideBuilder.Apply(
            roots,
            node => node.Children,
            (node, segments) => node.SetHierarchyGuideSegments(segments));
    }
}

internal sealed class RepositorySnapshotCache(int capacity = 12)
{
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> _entries = [];
    private readonly LinkedList<CacheEntry> _lru = [];

    internal int Count => _entries.Count;

    internal bool TryGet(
        string repositoryIdentity,
        string commitHash,
        out IReadOnlyList<RepositorySnapshotEntry> snapshot)
    {
        var key = new CacheKey(repositoryIdentity, commitHash);
        if (!_entries.TryGetValue(key, out var node))
        {
            snapshot = [];
            return false;
        }

        _lru.Remove(node);
        _lru.AddFirst(node);
        snapshot = node.Value.Snapshot;
        return true;
    }

    internal void Set(
        string repositoryIdentity,
        string commitHash,
        IReadOnlyList<RepositorySnapshotEntry> snapshot)
    {
        var key = new CacheKey(repositoryIdentity, commitHash);
        if (_entries.TryGetValue(key, out var existing))
        {
            _lru.Remove(existing);
            _entries.Remove(key);
        }

        var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, snapshot));
        _lru.AddFirst(node);
        _entries[key] = node;

        while (_entries.Count > _capacity && _lru.Last is { } oldest)
        {
            _lru.RemoveLast();
            _entries.Remove(oldest.Value.Key);
        }
    }

    internal void Clear()
    {
        _entries.Clear();
        _lru.Clear();
    }

    private sealed record CacheKey(string RepositoryIdentity, string CommitHash);
    private sealed record CacheEntry(CacheKey Key, IReadOnlyList<RepositorySnapshotEntry> Snapshot);
}
