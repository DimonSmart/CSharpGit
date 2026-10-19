using CSharpGit.Domain;

namespace CSharpGit.Presentation.Core.History;

/// <summary>Pure text projection; keeps Git semantics out of layout calculations.</summary>
public static class HistoryReferenceBadgeContent
{
    public readonly record struct Annotations(string Counters, string Upstream);

    public static Annotations Create(HistoryReferenceDecoration reference)
    {
        if (reference.Kind != HistoryReferenceKind.CurrentLocalBranch
            || string.IsNullOrWhiteSpace(reference.Upstream))
            return new Annotations(string.Empty, string.Empty);

        var counters = reference.Ahead > 0 && reference.Behind > 0
            ? $"↑{reference.Ahead} ↓{reference.Behind}"
            : reference.Ahead > 0
                ? $"↑{reference.Ahead}"
                : reference.Behind > 0
                    ? $"↓{reference.Behind}"
                    : string.Empty;

        var upstream = reference.IsSameNameRemoteUpstream
            ? string.Empty
            : $"· upstream {reference.Upstream}";
        return new Annotations(counters, upstream);
    }
}
