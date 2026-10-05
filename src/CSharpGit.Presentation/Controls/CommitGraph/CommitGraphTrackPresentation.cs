namespace CSharpGit.Presentation.Controls.CommitGraph;

internal static class CommitGraphTrackPresentation
{
    internal static bool ShouldUseMutedStyle(
        bool isReflogOnly,
        int nodeTrackId,
        int trackId) =>
        isReflogOnly && trackId == nodeTrackId;
}
