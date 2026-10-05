using CSharpGit.Presentation.Controls.CommitGraph;

namespace CSharpGit.Desktop.Tests;

public sealed class CommitGraphTrackPresentationTests
{
    [Theory]
    [InlineData(false, 4, 4, false)]
    [InlineData(false, 4, 7, false)]
    [InlineData(true, 4, 4, true)]
    [InlineData(true, 4, 7, false)]
    public void NodeMutedStyleBelongsOnlyToReflogNodeTrack(
        bool isReflogOnly,
        int nodeTrackId,
        int trackId,
        bool expected)
    {
        Assert.Equal(
            expected,
            CommitGraphTrackPresentation.ShouldUseMutedNodeStyle(
                isReflogOnly,
                nodeTrackId,
                trackId));
    }

    [Fact]
    public void ReflogPrimitiveOnNodeTrackIsMuted()
    {
        Assert.True(CommitGraphTrackPresentation.ShouldUseMutedPrimitiveStyle(
            isReflogOnly: true,
            nodeTrackId: 4,
            trackId: 4,
            start: new GraphPoint(10, 0),
            end: new GraphPoint(10, 10),
            nodeCenter: new GraphPoint(10, 20)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReflogPrimitiveTouchingNodeIsMutedEvenWhenTrackIdDiffers(bool nodeIsAtEnd)
    {
        var nodeCenter = new GraphPoint(10, 20);
        var otherPoint = new GraphPoint(30, 0);

        Assert.True(CommitGraphTrackPresentation.ShouldUseMutedPrimitiveStyle(
            isReflogOnly: true,
            nodeTrackId: 4,
            trackId: 7,
            start: nodeIsAtEnd ? otherPoint : nodeCenter,
            end: nodeIsAtEnd ? nodeCenter : otherPoint,
            nodeCenter));
    }

    [Fact]
    public void ReflogPassThroughPrimitiveOnOtherTrackKeepsPalette()
    {
        Assert.False(CommitGraphTrackPresentation.ShouldUseMutedPrimitiveStyle(
            isReflogOnly: true,
            nodeTrackId: 4,
            trackId: 7,
            start: new GraphPoint(30, 0),
            end: new GraphPoint(30, 40),
            nodeCenter: new GraphPoint(10, 20)));
    }

    [Fact]
    public void NormalRowPrimitiveTouchingNodeKeepsPalette()
    {
        var nodeCenter = new GraphPoint(10, 20);

        Assert.False(CommitGraphTrackPresentation.ShouldUseMutedPrimitiveStyle(
            isReflogOnly: false,
            nodeTrackId: 4,
            trackId: 7,
            start: new GraphPoint(30, 0),
            end: nodeCenter,
            nodeCenter));
    }
}
