using CSharpGit.Presentation.Controls.CommitGraph;

namespace CSharpGit.Desktop.Tests;

public sealed class CommitGraphAdaptiveLayoutTests
{
    [Theory]
    [InlineData(1, 120, 16)]
    [InlineData(7, 120, 16)]
    [InlineData(8, 136, 16)]
    [InlineData(9, 152, 16)]
    [InlineData(12, 200, 16)]
    public void LayoutUsesExpectedWidthBeforeCompression(int laneCount, double expectedWidth, double expectedLaneWidth)
    {
        var state = new CommitGraphLayoutState();

        state.ObserveLaneCount(laneCount);

        Assert.Equal(expectedWidth, state.GraphWidth, 6);
        Assert.Equal(expectedLaneWidth, state.LaneWidth, 6);
    }

    [Fact]
    public void ThirteenLanesKeepTwoHundredPixelColumnAndCompressSpacing()
    {
        var state = new CommitGraphLayoutState();

        state.ObserveLaneCount(13);

        Assert.Equal(200, state.GraphWidth, 6);
        Assert.Equal(176d / 12d, state.LaneWidth, 6);
        Assert.InRange(state.LaneWidth, CommitGraphMetrics.MinLaneWidth, CommitGraphMetrics.DefaultLaneWidth);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(100)]
    public void ExtremeTopologyNeverCompressesBelowMinimum(int laneCount)
    {
        var state = new CommitGraphLayoutState();

        state.ObserveLaneCount(laneCount);

        Assert.Equal(CommitGraphMetrics.MaxGraphWidth, state.GraphWidth);
        Assert.Equal(CommitGraphMetrics.MinLaneWidth, state.LaneWidth);
    }

    [Fact]
    public void LayoutIsMonotonicUntilSessionReset()
    {
        var state = new CommitGraphLayoutState();

        state.ObserveLaneCount(5);
        state.ObserveLaneCount(7);
        state.ObserveLaneCount(9);
        Assert.Equal(152, state.GraphWidth);
        Assert.Equal(16, state.LaneWidth);

        state.ObserveLaneCount(4);
        Assert.Equal(9, state.ObservedMaxLaneCount);
        Assert.Equal(152, state.GraphWidth);
        Assert.Equal(16, state.LaneWidth);

        state.ObserveLaneCount(13);
        var compressedLaneWidth = state.LaneWidth;
        state.ObserveLaneCount(16);
        Assert.Equal(200, state.GraphWidth);
        Assert.True(state.LaneWidth < compressedLaneWidth);

        state.Reset();
        Assert.Equal(0, state.ObservedMaxLaneCount);
        Assert.Equal(120, state.GraphWidth);
        Assert.Equal(16, state.LaneWidth);
    }

    [Fact]
    public void SameEffectiveMetricsGiveSameLaneCoordinateAcrossRows()
    {
        var state = new CommitGraphLayoutState();
        state.ObserveLaneCount(13);
        var metrics = state.Metrics;
        var first = new CommitGraphRowVisual(3, 3, 13, [new(3, 3, 3)], [new(3, 3, 3)]);
        var second = new CommitGraphRowVisual(3, 7, 13, [new(3, 3, 7)], [new(3, 3, 7)]);

        var firstGeometry = CommitGraphGeometryBuilder.Build(first, 24, metrics);
        var secondGeometry = CommitGraphGeometryBuilder.Build(second, 24, metrics);

        Assert.Equal(firstGeometry.Node?.Center.X, secondGeometry.Node?.Center.X);
        Assert.Equal(metrics.GetLaneX(3), firstGeometry.Node?.Center.X);
    }
}
