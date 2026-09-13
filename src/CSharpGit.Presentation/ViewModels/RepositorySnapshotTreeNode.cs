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
    public bool IsDirectory => Entry is null;

    public static IReadOnlyList<RepositorySnapshotTreeNode> Build(
        IEnumerable<RepositorySnapshotEntry> entries,
        string? nameQuery = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var query = string.IsNullOrWhiteSpace(nameQuery) ? null : nameQuery.Trim();
        var roots = new List<BuilderNode>();

        foreach (var entry in entries
                     .Where(entry => query is null || entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .GroupBy(entry => entry.Path, StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            var parts = entry.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
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

            current.Add(new BuilderNode(parts[^1], entry.Path, entry));
        }

        return Sort(roots).Select(ToPresentationNode).ToList();
    }

    private static RepositorySnapshotTreeNode ToPresentationNode(BuilderNode source) =>
        new(
            source.Name,
            source.Path,
            source.Entry,
            Sort(source.Children).Select(ToPresentationNode).ToList());

    private static IOrderedEnumerable<BuilderNode> Sort(IEnumerable<BuilderNode> nodes) =>
        nodes.OrderBy(node => node.Entry is null ? 0 : 1)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Name, StringComparer.Ordinal);

    private sealed class BuilderNode(string name, string path, RepositorySnapshotEntry? entry)
    {
        public string Name { get; } = name;
        public string Path { get; } = path;
        public RepositorySnapshotEntry? Entry { get; } = entry;
        public List<BuilderNode> Children { get; } = [];
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
