using CSharpGit.Presentation.Controls.CommitGraph;

namespace CSharpGit.Desktop.Tests;

public sealed class CommitGraphGeometryBuilderTests
{
    [Fact]
    public void SingleLinearBranchBuildsNodeAndTwoStraightSegments()
    {
        var graph = new CommitGraphRowVisual(
            NodeLane: 0,
            NodeTrackId: 0,
            LaneCount: 1,
            IncomingSegments: [new(0, 0, 0)],
            OutgoingSegments: [new(0, 0, 0)]);

        var geometry = CommitGraphGeometryBuilder.Build(graph, 32);

        Assert.Equal(24, geometry.Width);
        Assert.Equal(32, geometry.Height);
        Assert.Empty(geometry.Beziers);
        Assert.Equal(2, geometry.Lines.Count);
        Assert.Equal(new GraphPoint(12, 16), geometry.Node?.Center);
        Assert.Equal(new GraphPoint(12, 0), geometry.Lines[0].Start);
        Assert.Equal(new GraphPoint(12, 16), geometry.Lines[0].End);
        Assert.Equal(new GraphPoint(12, 16), geometry.Lines[1].Start);
        Assert.Equal(new GraphPoint(12, 32), geometry.Lines[1].End);
    }

    [Fact]
    public void ParallelBranchKeepsContinuationOnSecondLane()
    {
        var graph = new CommitGraphRowVisual(
            NodeLane: 0,
            NodeTrackId: 0,
            LaneCount: 2,
            IncomingSegments: [new(0, 0, 0), new(1, 1, 1)],
            OutgoingSegments: [new(0, 0, 0), new(1, 1, 1)]);

        var geometry = CommitGraphGeometryBuilder.Build(graph, 32);

        Assert.Contains(geometry.Lines, line =>
            line.TrackId == 1
            && line.Start == new GraphPoint(28, 0)
            && line.End == new GraphPoint(28, 16));
        Assert.Contains(geometry.Lines, line =>
            line.TrackId == 1
            && line.Start == new GraphPoint(28, 16)
            && line.End == new GraphPoint(28, 32));
    }

    [Fact]
    public void LaneTransitionBuildsBezier()
    {
        var graph = new CommitGraphRowVisual(
            NodeLane: 0,
            NodeTrackId: 0,
            LaneCount: 2,
            IncomingSegments: [new(1, 0, 1)],
            OutgoingSegments: []);

        var geometry = CommitGraphGeometryBuilder.Build(graph, 32);

        var curve = Assert.Single(geometry.Beziers);
        Assert.Equal(new GraphPoint(28, 0), curve.Start);
        Assert.Equal(new GraphPoint(12, 16), curve.End);
        Assert.Equal(1, curve.TrackId);
    }

    [Fact]
    public void BranchSplitBuildsTwoOutgoingSegments()
    {
        var graph = new CommitGraphRowVisual(
            NodeLane: 0,
            NodeTrackId: 0,
            LaneCount: 2,
            IncomingSegments: [],
            OutgoingSegments: [new(0, 0, 0), new(0, 1, 1)]);

        var geometry = CommitGraphGeometryBuilder.Build(graph, 32);

        Assert.Single(geometry.Lines);
        Assert.Single(geometry.Beziers);
        Assert.Equal(new GraphPoint(12, 16), geometry.Beziers[0].Start);
        Assert.Equal(new GraphPoint(28, 32), geometry.Beziers[0].End);
    }

    [Fact]
    public void MergeBuildsMultipleIncomingSegments()
    {
        var graph = new CommitGraphRowVisual(
            NodeLane: 0,
            NodeTrackId: 0,
            LaneCount: 2,
            IncomingSegments: [new(0, 0, 0), new(1, 0, 1)],
            OutgoingSegments: [new(0, 0, 0)]);

        var geometry = CommitGraphGeometryBuilder.Build(graph, 32);

        Assert.Equal(2, geometry.Lines.Count);
        var mergeCurve = Assert.Single(geometry.Beziers);
        Assert.Equal(new GraphPoint(28, 0), mergeCurve.Start);
        Assert.Equal(new GraphPoint(12, 16), mergeCurve.End);
    }

    [Theory]
    [InlineData(1, 24)]
    [InlineData(2, 40)]
    [InlineData(4, 72)]
    [InlineData(8, 136)]
    public void WidthIsCalculatedFromLaneCount(int laneCount, double expectedWidth)
    {
        Assert.Equal(expectedWidth, CommitGraphGeometryBuilder.CalculateWidth(laneCount));
    }

    [Fact]
    public void TrackColorIndexDependsOnTrackIdNotLane()
    {
        const int paletteCount = 8;
        var first = new CommitGraphSegment(3, 2, 11);
        var second = new CommitGraphSegment(1, 0, 11);

        Assert.Equal(
            CommitGraphGeometryBuilder.GetPaletteIndex(first.TrackId, paletteCount),
            CommitGraphGeometryBuilder.GetPaletteIndex(second.TrackId, paletteCount));
        Assert.Equal(3, CommitGraphGeometryBuilder.GetPaletteIndex(11, paletteCount));
    }

    [Fact]
    public void InvalidLanesAreIgnoredWithoutThrowing()
    {
        var graph = new CommitGraphRowVisual(
            NodeLane: 7,
            NodeTrackId: 0,
            LaneCount: 2,
            IncomingSegments: [new(7, 0, 3), new(1, 1, 1)],
            OutgoingSegments: [new(0, -1, 2)]);

        var exception = Record.Exception(() => CommitGraphGeometryBuilder.Build(graph, 32));
        var geometry = CommitGraphGeometryBuilder.Build(graph, 32);

        Assert.Null(exception);
        Assert.Null(geometry.Node);
        Assert.Single(geometry.Lines);
        Assert.Empty(geometry.Beziers);
    }

    [Fact]
    public void NullAndEmptyGraphProduceNoPrimitives()
    {
        var nullGeometry = CommitGraphGeometryBuilder.Build(null, 32);
        var emptyGeometry = CommitGraphGeometryBuilder.Build(
            new CommitGraphRowVisual(0, 0, 0, [], []),
            32);

        Assert.Empty(nullGeometry.Lines);
        Assert.Empty(nullGeometry.Beziers);
        Assert.Null(nullGeometry.Node);
        Assert.Equal(0, nullGeometry.Width);
        Assert.Empty(emptyGeometry.Lines);
        Assert.Empty(emptyGeometry.Beziers);
        Assert.Null(emptyGeometry.Node);
        Assert.Equal(0, emptyGeometry.Width);
    }

    [Fact]
    public void LinesReachExactRowBoundaries()
    {
        var graph = new CommitGraphRowVisual(
            NodeLane: 0,
            NodeTrackId: 0,
            LaneCount: 1,
            IncomingSegments: [new(0, 0, 0)],
            OutgoingSegments: [new(0, 0, 0)]);

        var geometry = CommitGraphGeometryBuilder.Build(graph, 37);

        Assert.Equal(0, geometry.Lines[0].Start.Y);
        Assert.Equal(37, geometry.Lines[1].End.Y);
    }
}
