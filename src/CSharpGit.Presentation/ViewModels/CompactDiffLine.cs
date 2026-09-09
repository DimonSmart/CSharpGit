using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed record CompactDiffLine(
    string Text,
    DiffLineKind Kind,
    int? OldLineNumber,
    int? NewLineNumber)
{
    public static IReadOnlyList<CompactDiffLine> Build(IEnumerable<DiffLine> sourceLines)
    {
        ArgumentNullException.ThrowIfNull(sourceLines);

        var result = new List<CompactDiffLine>();
        var inHunk = false;
        var oldLine = 0;
        var newLine = 0;

        foreach (var source in sourceLines)
        {
            var text = source.Text;
            if (TryReadHunkStarts(text, out var oldStart, out var newStart))
            {
                inHunk = true;
                oldLine = oldStart;
                newLine = newStart;
                result.Add(new CompactDiffLine(text, DiffLineKind.Header, null, null));
                continue;
            }

            if (!inHunk)
            {
                if (IsNoiseHeader(text)) continue;
                if (text.Length > 0)
                    result.Add(new CompactDiffLine(text, DiffLineKind.Header, null, null));
                continue;
            }

            if (text.StartsWith("\\ No newline at end of file", StringComparison.Ordinal))
            {
                result.Add(new CompactDiffLine(text, DiffLineKind.Header, null, null));
                continue;
            }

            switch (source.Kind)
            {
                case DiffLineKind.Added:
                    result.Add(new CompactDiffLine(text, DiffLineKind.Added, null, newLine));
                    newLine++;
                    break;
                case DiffLineKind.Removed:
                    result.Add(new CompactDiffLine(text, DiffLineKind.Removed, oldLine, null));
                    oldLine++;
                    break;
                case DiffLineKind.Context:
                    result.Add(new CompactDiffLine(text, DiffLineKind.Context, oldLine, newLine));
                    oldLine++;
                    newLine++;
                    break;
                default:
                    result.Add(new CompactDiffLine(text, DiffLineKind.Header, null, null));
                    break;
            }
        }

        return result;
    }

    private static bool IsNoiseHeader(string text) =>
        text.StartsWith("diff --git ", StringComparison.Ordinal) ||
        text.StartsWith("index ", StringComparison.Ordinal) ||
        text.StartsWith("--- ", StringComparison.Ordinal) ||
        text.StartsWith("+++ ", StringComparison.Ordinal);

    private static bool TryReadHunkStarts(string text, out int oldStart, out int newStart)
    {
        oldStart = 0;
        newStart = 0;
        if (!text.StartsWith("@@ -", StringComparison.Ordinal)) return false;

        var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 3 || fields[0] != "@@" || fields[1].Length < 2 || fields[2].Length < 2 ||
            fields[1][0] != '-' || fields[2][0] != '+') return false;

        return TryReadRangeStart(fields[1].AsSpan(1), out oldStart) &&
               TryReadRangeStart(fields[2].AsSpan(1), out newStart);
    }

    private static bool TryReadRangeStart(ReadOnlySpan<char> range, out int start)
    {
        var comma = range.IndexOf(',');
        if (comma >= 0) range = range[..comma];
        return int.TryParse(range, out start);
    }
}
