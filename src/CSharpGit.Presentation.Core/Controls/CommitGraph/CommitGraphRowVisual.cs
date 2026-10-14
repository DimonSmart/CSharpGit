namespace CSharpGit.Presentation.Controls.CommitGraph;

public sealed record CommitGraphRowVisual(
    int NodeLane,
    int NodeTrackId,
    int LaneCount,
    IReadOnlyList<CommitGraphSegment> IncomingSegments,
    IReadOnlyList<CommitGraphSegment> OutgoingSegments);

public sealed record CommitGraphSegment(
    int FromLane,
    int ToLane,
    int TrackId);
