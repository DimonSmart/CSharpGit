namespace CSharpGit.Presentation.Controls.CommitGraph;

internal static class CommitGraphTrackPresentation
{
    internal static bool ShouldUseMutedNodeStyle(
        bool isReflogOnly,
        int nodeTrackId,
        int trackId) =>
        isReflogOnly && trackId == nodeTrackId;

    internal static bool ShouldUseMutedPrimitiveStyle(
        bool isReflogOnly,
        int nodeTrackId,
        int trackId,
        GraphPoint start,
        GraphPoint end,
        GraphPoint nodeCenter) =>
        isReflogOnly
        && (trackId == nodeTrackId
            || start == nodeCenter
            || end == nodeCenter);
}
