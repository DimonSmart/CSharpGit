using CSharpGit.Presentation.Controls.CommitGraph;

namespace CSharpGit.Desktop.Tests;

public sealed class CommitGraphTrackPresentationTests
{
    [Theory]
    [InlineData(false, 4, 4, false)]
    [InlineData(false, 4, 7, false)]
    [InlineData(true, 4, 4, true)]
    [InlineData(true, 4, 7, false)]
    public void MutedStyleBelongsOnlyToReflogNodeTrack(
        bool isReflogOnly,
        int nodeTrackId,
        int trackId,
        bool expected)
    {
        Assert.Equal(
            expected,
            CommitGraphTrackPresentation.ShouldUseMutedStyle(
                isReflogOnly,
                nodeTrackId,
                trackId));
    }
}
