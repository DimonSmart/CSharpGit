namespace CSharpGit.Presentation.Controls.CommitGraph;

internal readonly struct CommitGraphGeometryKey : IEquatable<CommitGraphGeometryKey>
{
    internal const double GeometryEpsilon = 0.01;

    private readonly CommitGraphRowVisual? _graph;
    private readonly int _topologyHash;

    private CommitGraphGeometryKey(
        CommitGraphRowVisual graph,
        double height,
        CommitGraphMetrics metrics)
    {
        _graph = graph;
        Height = height;
        LaneWidth = metrics.LaneWidth;
        HorizontalMargin = metrics.HorizontalMargin;
        NodeRadius = metrics.NodeRadius;
        _topologyHash = CalculateTopologyHash(graph);
    }

    internal double Height { get; }
    internal double LaneWidth { get; }
    internal double HorizontalMargin { get; }
    internal double NodeRadius { get; }

    internal static bool TryCreate(
        CommitGraphRowVisual? graph,
        double height,
        CommitGraphMetrics metrics,
        out CommitGraphGeometryKey key)
    {
        if (graph is null || !double.IsFinite(height) || height <= 0)
        {
            key = default;
            return false;
        }

        key = new CommitGraphGeometryKey(graph, height, metrics);
        return true;
    }

    internal CommitGraphGeometryKey ReuseHeightIfEquivalent(in CommitGraphGeometryKey previous)
    {
        if (HasSameTopology(previous)
            && HasSameGeometryMetrics(previous)
            && Math.Abs(Height - previous.Height) <= GeometryEpsilon)
        {
            return previous;
        }

        return this;
    }

    internal bool HasSameTopology(in CommitGraphGeometryKey other) =>
        _topologyHash == other._topologyHash
        && TopologyEquals(_graph, other._graph);

    internal bool HasSameGeometryMetrics(in CommitGraphGeometryKey other) =>
        LaneWidth.Equals(other.LaneWidth)
        && HorizontalMargin.Equals(other.HorizontalMargin)
        && NodeRadius.Equals(other.NodeRadius);

    internal static bool GeometryMetricsEqual(
        in CommitGraphMetrics left,
        in CommitGraphMetrics right) =>
        left.LaneWidth.Equals(right.LaneWidth)
        && left.HorizontalMargin.Equals(right.HorizontalMargin)
        && left.NodeRadius.Equals(right.NodeRadius);

    public bool Equals(CommitGraphGeometryKey other) =>
        Height.Equals(other.Height)
        && HasSameGeometryMetrics(other)
        && HasSameTopology(other);

    public override bool Equals(object? obj) =>
        obj is CommitGraphGeometryKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_topologyHash);
        hash.Add(Height);
        hash.Add(LaneWidth);
        hash.Add(HorizontalMargin);
        hash.Add(NodeRadius);
        return hash.ToHashCode();
    }

    public static bool operator ==(
        CommitGraphGeometryKey left,
        CommitGraphGeometryKey right) =>
        left.Equals(right);

    public static bool operator !=(
        CommitGraphGeometryKey left,
        CommitGraphGeometryKey right) =>
        !left.Equals(right);

    private static int CalculateTopologyHash(CommitGraphRowVisual graph)
    {
        var hash = new HashCode();
        hash.Add(graph.LaneCount);
        hash.Add(graph.NodeLane);
        hash.Add(graph.NodeTrackId);
        AddSegments(ref hash, graph.IncomingSegments);
        AddSegments(ref hash, graph.OutgoingSegments);
        return hash.ToHashCode();
    }

    private static void AddSegments(
        ref HashCode hash,
        IReadOnlyList<CommitGraphSegment>? segments)
    {
        if (segments is null)
        {
            hash.Add(-1);
            return;
        }

        hash.Add(segments.Count);
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment is null)
            {
                hash.Add(0);
                continue;
            }

            hash.Add(1);
            hash.Add(segment.FromLane);
            hash.Add(segment.ToLane);
            hash.Add(segment.TrackId);
        }
    }

    private static bool TopologyEquals(
        CommitGraphRowVisual? left,
        CommitGraphRowVisual? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        if (left.LaneCount != right.LaneCount
            || left.NodeLane != right.NodeLane
            || left.NodeTrackId != right.NodeTrackId)
        {
            return false;
        }

        return SegmentsEqual(left.IncomingSegments, right.IncomingSegments)
            && SegmentsEqual(left.OutgoingSegments, right.OutgoingSegments);
    }

    private static bool SegmentsEqual(
        IReadOnlyList<CommitGraphSegment>? left,
        IReadOnlyList<CommitGraphSegment>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            var leftSegment = left[index];
            var rightSegment = right[index];
            if (ReferenceEquals(leftSegment, rightSegment))
                continue;
            if (leftSegment is null || rightSegment is null)
                return false;
            if (leftSegment.FromLane != rightSegment.FromLane
                || leftSegment.ToLane != rightSegment.ToLane
                || leftSegment.TrackId != rightSegment.TrackId)
            {
                return false;
            }
        }

        return true;
    }
}
