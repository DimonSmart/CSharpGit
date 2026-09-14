namespace CSharpGit.Presentation.Controls.CommitGraph;

public readonly record struct CommitGraphMetrics(
    double LaneWidth,
    double HorizontalMargin,
    double LineThickness,
    double NodeRadius)
{
    public const double DefaultGraphWidth = 120;
    public const double MaxGraphWidth = 200;
    public const double DefaultLaneWidth = 16;
    public const double MinLaneWidth = 8;
    public const double DefaultHorizontalMargin = 8;
    public const double DefaultLineThickness = 2;
    public const double DefaultNodeRadius = 4;

    public static CommitGraphMetrics Default { get; } = new(
        LaneWidth: DefaultLaneWidth,
        HorizontalMargin: DefaultHorizontalMargin,
        LineThickness: DefaultLineThickness,
        NodeRadius: DefaultNodeRadius);

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
