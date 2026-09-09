namespace CSharpGit.Presentation.Controls.CommitGraph;

public readonly record struct CommitGraphMetrics(
    double LaneWidth,
    double HorizontalMargin,
    double LineThickness,
    double NodeRadius,
    double DefaultRowHeight)
{
    public static CommitGraphMetrics Default { get; } = new(
        LaneWidth: 16,
        HorizontalMargin: 8,
        LineThickness: 2,
        NodeRadius: 4,
        DefaultRowHeight: 34);

    public double CalculateWidth(int laneCount)
    {
        if (laneCount <= 0)
        {
            return 0;
        }

        return HorizontalMargin * 2
            + NodeRadius * 2
            + (laneCount - 1) * LaneWidth;
    }

    public double GetLaneX(int lane)
        => HorizontalMargin + NodeRadius + lane * LaneWidth;
}
