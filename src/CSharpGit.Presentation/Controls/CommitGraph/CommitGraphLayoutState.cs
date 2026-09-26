namespace CSharpGit.Presentation.Controls.CommitGraph;

public sealed class CommitGraphLayoutState
{
    public int ObservedMaxLaneCount { get; private set; }
    public double GraphWidth { get; private set; } = CommitGraphMetrics.DefaultGraphWidth;
    public double LaneWidth { get; private set; } = CommitGraphMetrics.DefaultLaneWidth;
    public CommitGraphMetrics Metrics => CommitGraphMetrics.Default with { LaneWidth = LaneWidth };
    internal CommitGraphGeometryCache GeometryCache { get; } = new();

    public bool ObserveLaneCount(int laneCount)
    {
        if (laneCount <= ObservedMaxLaneCount)
        {
            return false;
        }

        ObservedMaxLaneCount = laneCount;
        var previousWidth = GraphWidth;
        var previousLaneWidth = LaneWidth;
        Recalculate();
        return !AreEqual(previousWidth, GraphWidth) || !AreEqual(previousLaneWidth, LaneWidth);
    }

    public bool Reset()
    {
        GeometryCache.Clear();
        var changed = ObservedMaxLaneCount != 0
            || !AreEqual(GraphWidth, CommitGraphMetrics.DefaultGraphWidth)
            || !AreEqual(LaneWidth, CommitGraphMetrics.DefaultLaneWidth);

        ObservedMaxLaneCount = 0;
        GraphWidth = CommitGraphMetrics.DefaultGraphWidth;
        LaneWidth = CommitGraphMetrics.DefaultLaneWidth;
        return changed;
    }

    private void Recalculate()
    {
        if (ObservedMaxLaneCount <= 0)
        {
            GraphWidth = CommitGraphMetrics.DefaultGraphWidth;
            LaneWidth = CommitGraphMetrics.DefaultLaneWidth;
            return;
        }

        var defaultMetrics = CommitGraphMetrics.Default;
        var requiredWidth = defaultMetrics.CalculateWidth(ObservedMaxLaneCount);
        if (requiredWidth <= CommitGraphMetrics.DefaultGraphWidth)
        {
            GraphWidth = CommitGraphMetrics.DefaultGraphWidth;
            LaneWidth = CommitGraphMetrics.DefaultLaneWidth;
            return;
        }

        if (requiredWidth <= CommitGraphMetrics.MaxGraphWidth)
        {
            GraphWidth = requiredWidth;
            LaneWidth = CommitGraphMetrics.DefaultLaneWidth;
            return;
        }

        GraphWidth = CommitGraphMetrics.MaxGraphWidth;
        var laneDivisor = Math.Max(1, ObservedMaxLaneCount - 1);
        var availableLaneSpace = CommitGraphMetrics.MaxGraphWidth
            - defaultMetrics.HorizontalMargin * 2
            - defaultMetrics.NodeRadius * 2;
        LaneWidth = Math.Clamp(
            availableLaneSpace / laneDivisor,
            CommitGraphMetrics.MinLaneWidth,
            CommitGraphMetrics.DefaultLaneWidth);
    }

    private static bool AreEqual(double left, double right)
        => Math.Abs(left - right) <= 0.0001;
}
