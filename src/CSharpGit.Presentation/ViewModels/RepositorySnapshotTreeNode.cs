using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.ViewModels;

public sealed class RepositorySnapshotTreeNode
{
    private RepositorySnapshotTreeNode(
        string displayName,
        string path,
        RepositorySnapshotEntry? entry,
        IReadOnlyList<RepositorySnapshotTreeNode> children)
    {
        DisplayName = displayName;
        Path = path;
        Entry = entry;
        Children = children;
    }

    public string DisplayName { get; }
    public string Path { get; }
    public RepositorySnapshotEntry? Entry { get; }
    public IReadOnlyList<RepositorySnapshotTreeNode> Children { get; }
    public bool HasChildren => Children.Count > 0;
    public bool IsDirectory => Entry is null;
    public bool IsExpanded { get; set; }
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
        ArgumentNullException.ThrowIfNull(entries);
        var query = string.IsNullOrWhiteSpace(nameQuery) ? null : nameQuery.Trim();
        var filtered = entries.Where(entry =>
            query is null || entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
        var structure = PathTreeBuilder.Build(filtered, entry => entry.Path);
        var roots = structure.Select(ToPresentationNode).ToList();

        TreeHierarchyGuideBuilder.Apply(
            roots,
            node => node.Children,
            (node, segments) => node.HierarchyGuideSegments = segments);
        return roots;
    }

    private static RepositorySnapshotTreeNode ToPresentationNode(PathTreeNode<RepositorySnapshotEntry> source) =>
        new(
            source.DisplayName,
            source.Path,
            source.Item,
            source.Children.Select(ToPresentationNode).ToList());
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
