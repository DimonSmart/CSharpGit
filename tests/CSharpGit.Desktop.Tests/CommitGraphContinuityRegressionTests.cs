using CSharpGit.Presentation.Controls.CommitGraph;

namespace CSharpGit.Desktop.Tests;

public sealed class CommitGraphContinuityRegressionTests
{
    [Theory]
    [InlineData(24)]
    [InlineData(26)]
    [InlineData(34)]
    [InlineData(48)]
    [InlineData(25.5)]
    public void ContinuingTracksUseActualRowBoundariesAcrossTopologies(double rowHeight)
    {
        var graph = new CommitGraphRowVisual(
            NodeLane: 0,
            NodeTrackId: 0,
            LaneCount: 2,
            IncomingSegments:
            [
                new(0, 0, 0),
                new(1, 0, 1),
                new(1, 1, 2),
            ],
            OutgoingSegments:
            [
                new(0, 0, 0),
                new(0, 1, 3),
                new(1, 1, 2),
            ]);

        var geometry = CommitGraphGeometryBuilder.Build(graph, rowHeight);
        var center = rowHeight / 2;

        Assert.Equal(rowHeight, geometry.Height);
        Assert.Equal(center, geometry.Node?.Center.Y);

        Assert.Contains(geometry.Lines, line =>
            line.TrackId == 0 && line.Start.Y == 0 && line.End.Y == center);
        Assert.Contains(geometry.Lines, line =>
            line.TrackId == 0 && line.Start.Y == center && line.End.Y == rowHeight);
        Assert.Contains(geometry.Lines, line =>
            line.TrackId == 2 && line.Start.Y == 0 && line.End.Y == center);
        Assert.Contains(geometry.Lines, line =>
            line.TrackId == 2 && line.Start.Y == center && line.End.Y == rowHeight);

        Assert.Contains(geometry.Beziers, curve =>
            curve.TrackId == 1 && curve.Start.Y == 0 && curve.End.Y == center);
        Assert.Contains(geometry.Beziers, curve =>
            curve.TrackId == 3 && curve.Start.Y == center && curve.End.Y == rowHeight);
    }
}
