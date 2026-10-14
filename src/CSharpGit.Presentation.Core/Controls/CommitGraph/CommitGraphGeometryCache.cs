namespace CSharpGit.Presentation.Controls.CommitGraph;

internal sealed class CommitGraphGeometryCache
{
    internal const int DefaultCapacity = 2048;

    private readonly int _capacity;
    private readonly Dictionary<CommitGraphGeometryKey, CommitGraphGeometry> _entries;
    private readonly Queue<CommitGraphGeometryKey> _insertionOrder;

    internal CommitGraphGeometryCache(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _entries = new Dictionary<CommitGraphGeometryKey, CommitGraphGeometry>(capacity);
        _insertionOrder = new Queue<CommitGraphGeometryKey>(capacity);
    }

    internal int Count => _entries.Count;
    internal long Evictions { get; private set; }

    internal bool TryGet(
        in CommitGraphGeometryKey key,
        out CommitGraphGeometry geometry)
    {
        if (_entries.TryGetValue(key, out var cached))
        {
            geometry = cached;
            return true;
        }

        geometry = null!;
        return false;
    }

    internal bool Add(
        in CommitGraphGeometryKey key,
        CommitGraphGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (_entries.ContainsKey(key))
            return false;

        var evicted = false;
        while (_entries.Count >= _capacity && _insertionOrder.Count > 0)
        {
            var oldest = _insertionOrder.Dequeue();
            if (!_entries.Remove(oldest))
                continue;

            Evictions++;
            evicted = true;
            break;
        }

        _entries.Add(key, geometry);
        _insertionOrder.Enqueue(key);
        return evicted;
    }

    internal void Clear()
    {
        _entries.Clear();
        _insertionOrder.Clear();
        Evictions = 0;
    }
}
