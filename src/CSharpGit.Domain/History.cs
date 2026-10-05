namespace CSharpGit.Domain;

public enum HistoryScope { AllReferences, CurrentBranch }

public sealed record HistoryQuery(
    HistoryScope Scope,
    string? Filter,
    int Skip,
    int Take = 100,
    bool IncludeReflog = false,
    bool? HeadExists = null,
    GitReferences? RepositoryReferences = null,
    string? HeadReference = null,
    string? HeadCommit = null,
    bool IsDetachedHead = false,
    long ReflogSessionId = 0);

public enum HistoryReferenceKind
{
    CurrentLocalBranch,
    LocalBranch,
    RemoteTrackingBranch,
    Tag,
    DetachedHead,
    Other
}

public sealed record HistoryReferenceDecoration(
    string DisplayName,
    HistoryReferenceKind Kind,
    bool IsDefault = false,
    string? Upstream = null,
    int Ahead = 0,
    int Behind = 0);

public sealed record ReflogPresentation(
    string? Selector,
    string? EventKind,
    string? Subject)
{
    public string Display =>
        string.IsNullOrWhiteSpace(Selector)
            ? "◌ reflog"
            : string.IsNullOrWhiteSpace(EventKind)
                ? $"◌ {Selector}"
                : $"◌ {Selector} · {EventKind}";

    public string ToolTip =>
        string.IsNullOrWhiteSpace(Subject)
            ? "This commit is reachable only through Git reflog."
            : $"This commit is reachable only through Git reflog.\n{Selector}: {Subject}";
}

public sealed record CommitHistoryItem(
    string Hash,
    IReadOnlyList<string> Parents,
    string Subject,
    string Message,
    string Author,
    DateTimeOffset AuthoredAt,
    IReadOnlyList<string> References,
    string AuthorEmail = "")
{
    public string ShortHash => Hash[..Math.Min(8, Hash.Length)];
    public bool IsRootCommit => Parents.Count == 0;
    public string ParentsDisplay => IsRootCommit ? "—" : string.Join(", ", Parents);
    public string ReferencesDisplay => string.Join(", ", References);
}

public sealed record TopologyEdge(int FromLane, int ToLane, int TrackId = 0);

public sealed record CommitTopology(int Lane, IReadOnlyList<TopologyEdge> Edges)
{
    public int NodeTrackId { get; init; } = Lane;
    public int LaneCount { get; init; } = Lane + 1;
    public IReadOnlyList<TopologyEdge> IncomingEdges { get; init; } = [];
    public bool HasExactGraphTopology { get; init; }

    public string Display => string.Concat(Enumerable.Range(0, LaneCount)
        .Select(index => index == Lane ? "● " : "│ "));
}

public sealed record HistoryRow(
    CommitHistoryItem Commit,
    CommitTopology Topology,
    bool IsReflogOnly = false,
    ReflogPresentation? Reflog = null,
    IReadOnlyList<HistoryReferenceDecoration>? SemanticReferences = null)
{
    public IReadOnlyList<HistoryReferenceDecoration> ReferenceDetails { get; } =
        SemanticReferences
        ?? Commit.References
            .Select(reference => new HistoryReferenceDecoration(reference, HistoryReferenceKind.Other))
            .ToArray();

    public string ReflogGhostDisplay => Reflog?.Display ?? "◌ reflog";
    public string ReflogToolTip => Reflog?.ToolTip ?? "This commit is reachable only through Git reflog.";
}

public sealed record HistoryPage(IReadOnlyList<HistoryRow> Rows, bool HasMore);

public sealed record ChangedFile(
    string Path,
    int? AddedLines,
    int? RemovedLines,
    bool IsBinary,
    string? OriginalPath = null,
    string Status = "M");

public sealed record CommitDetails(CommitHistoryItem Commit, IReadOnlyList<ChangedFile> Files);

public enum DiffLineKind { Header, Context, Added, Removed }

public enum DiffTextLineEnding { Lf, CrLf }

public enum DiffDiagnosticKind
{
    Utf8BomAdded,
    Utf8BomRemoved,
    LineEndingsChanged,
    NoFinalNewline
}

public sealed record DiffDiagnostic(DiffDiagnosticKind Kind)
{
    public DiffTextLineEnding? OriginalLineEnding { get; init; }
    public DiffTextLineEnding? ChangedLineEnding { get; init; }
}

public sealed record DiffLine(string Text, DiffLineKind Kind)
{
    public bool HadTrailingCarriageReturn { get; init; }
}

public sealed record FileDiff(string Path, bool IsBinary, IReadOnlyList<DiffLine> Lines)
{
    public IReadOnlyList<DiffDiagnostic> Diagnostics { get; init; } = [];
}
