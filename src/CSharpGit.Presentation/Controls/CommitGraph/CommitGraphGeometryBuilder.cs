namespace CSharpGit.Presentation.Controls.CommitGraph;

public readonly record struct GraphPoint(double X, double Y);

public sealed record GraphLinePrimitive(
    GraphPoint Start,
    GraphPoint End,
    int TrackId);

public sealed record GraphBezierPrimitive(
    GraphPoint Start,
    GraphPoint Control1,
    GraphPoint Control2,
    GraphPoint End,
    int TrackId);

public sealed record GraphNodePrimitive(
    GraphPoint Center,
    double Radius,
    int TrackId);

public sealed record CommitGraphGeometry(
    double Width,
    double Height,
    IReadOnlyList<GraphBezierPrimitive> Beziers,
    IReadOnlyList<GraphLinePrimitive> Lines,
    GraphNodePrimitive? Node);

public static class CommitGraphGeometryBuilder
{
    public static double CalculateWidth(int laneCount, CommitGraphMetrics? metrics = null)
        => (metrics ?? CommitGraphMetrics.Default).CalculateWidth(laneCount);

    public static int GetPaletteIndex(int trackId, int paletteCount)
    {
        if (paletteCount <= 0)
        {
            return 0;
        }

        var index = trackId % paletteCount;
        return index < 0 ? index + paletteCount : index;
    }

    public static CommitGraphGeometry Build(
        CommitGraphRowVisual? graph,
        double height,
        CommitGraphMetrics? metrics = null)
    {
        var actualMetrics = metrics ?? CommitGraphMetrics.Default;
        var actualHeight = double.IsFinite(height) && height > 0
            ? height
            : actualMetrics.DefaultRowHeight;

        if (graph is null || graph.LaneCount <= 0)
        {
            return new CommitGraphGeometry(0, actualHeight, [], [], null);
        }

        var beziers = new List<GraphBezierPrimitive>();
        var lines = new List<GraphLinePrimitive>();
        var centerY = actualHeight / 2;

        AddSegments(
            graph.IncomingSegments,
            graph.LaneCount,
            startY: 0,
            endY: centerY,
            actualMetrics,
            beziers,
            lines);

        AddSegments(
            graph.OutgoingSegments,
            graph.LaneCount,
            startY: centerY,
            endY: actualHeight,
            actualMetrics,
            beziers,
            lines);

        GraphNodePrimitive? node = null;
        if (IsValidLane(graph.NodeLane, graph.LaneCount))
        {
            node = new GraphNodePrimitive(
                new GraphPoint(actualMetrics.GetLaneX(graph.NodeLane), centerY),
                actualMetrics.NodeRadius,
                graph.NodeTrackId);
        }

        return new CommitGraphGeometry(
            actualMetrics.CalculateWidth(graph.LaneCount),
            actualHeight,
            beziers,
            lines,
            node);
    }

    private static void AddSegments(
        IReadOnlyList<CommitGraphSegment>? segments,
        int laneCount,
        double startY,
        double endY,
        CommitGraphMetrics metrics,
        ICollection<GraphBezierPrimitive> beziers,
        ICollection<GraphLinePrimitive> lines)
    {
        if (segments is null)
        {
            return;
        }

        foreach (var segment in segments)
        {
            if (segment is null
                || !IsValidLane(segment.FromLane, laneCount)
                || !IsValidLane(segment.ToLane, laneCount))
            {
                continue;
            }

            var start = new GraphPoint(metrics.GetLaneX(segment.FromLane), startY);
            var end = new GraphPoint(metrics.GetLaneX(segment.ToLane), endY);

            if (segment.FromLane == segment.ToLane)
            {
                lines.Add(new GraphLinePrimitive(start, end, segment.TrackId));
                continue;
            }

            var halfDeltaY = (endY - startY) / 2;
            beziers.Add(new GraphBezierPrimitive(
                start,
                new GraphPoint(start.X, startY + halfDeltaY),
                new GraphPoint(end.X, endY - halfDeltaY),
                end,
                segment.TrackId));
        }
    }

    private static bool IsValidLane(int lane, int laneCount)
        => lane >= 0 && lane < laneCount;
}
