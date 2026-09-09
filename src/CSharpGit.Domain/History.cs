namespace CSharpGit.Domain;

public enum HistoryScope { AllReferences, CurrentBranch }

public sealed record HistoryQuery(HistoryScope Scope, string? Filter, int Skip, int Take = 100);

public sealed record CommitHistoryItem(
    string Hash,
    IReadOnlyList<string> Parents,
    string Subject,
    string Message,
    string Author,
    DateTimeOffset AuthoredAt,
    IReadOnlyList<string> References)
{
    public string ShortHash => Hash[..Math.Min(8, Hash.Length)];
    public string ParentsDisplay => string.Join(", ", Parents);
    public string ReferencesDisplay => string.Join(", ", References);
}

public sealed record TopologyEdge(int FromLane, int ToLane, int TrackId = 0);

public sealed record CommitTopology(int Lane, IReadOnlyList<TopologyEdge> Edges)
{
    public int NodeTrackId { get; init; } = Lane;
    public IReadOnlyList<TopologyEdge> IncomingEdges { get; init; } = [];
    public bool HasExactGraphTopology { get; init; }

    public string Display => string.Concat(Enumerable.Range(0, Math.Max(Lane + 1, Edges.Select(x => x.ToLane + 1).DefaultIfEmpty(1).Max()))
        .Select(index => index == Lane ? "● " : "│ "));
}

public sealed record HistoryRow(CommitHistoryItem Commit, CommitTopology Topology);

public sealed record HistoryPage(IReadOnlyList<HistoryRow> Rows, bool HasMore);

public sealed record ChangedFile(string Path, int? AddedLines, int? RemovedLines, bool IsBinary);

public sealed record CommitDetails(CommitHistoryItem Commit, IReadOnlyList<ChangedFile> Files);

public enum DiffLineKind { Header, Context, Added, Removed }

public sealed record DiffLine(string Text, DiffLineKind Kind);

public sealed record FileDiff(string Path, bool IsBinary, IReadOnlyList<DiffLine> Lines);
