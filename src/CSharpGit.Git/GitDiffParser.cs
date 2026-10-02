using CSharpGit.Domain;

namespace CSharpGit.Git;

internal static class GitDiffParser
{
    public static bool IsBinary(string output) =>
        output.Contains("Binary files ", StringComparison.Ordinal) ||
        output.Contains("GIT binary patch", StringComparison.Ordinal);

    public static ParsedGitDiff Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var rawLines = output.Split('\n');
        var count = rawLines.Length;
        if (count > 0 && rawLines[^1].Length == 0)
            count--;

        var lines = rawLines
            .Take(count)
            .Select(ParseLine)
            .ToArray();
        return new ParsedGitDiff(lines, BuildDiagnostics(lines));
    }

    public static IReadOnlyList<DiffLine> ParseLines(string output) => Parse(output).Lines;

    private static DiffLine ParseLine(string rawLine)
    {
        var hasTrailingCarriageReturn = rawLine.EndsWith('\r');
        var text = hasTrailingCarriageReturn ? rawLine[..^1] : rawLine;
        return new DiffLine(text, Classify(text))
        {
            HadTrailingCarriageReturn = hasTrailingCarriageReturn
        };
    }

    private static DiffLineKind Classify(string line) =>
        line.StartsWith("+++") || line.StartsWith("---") || line.StartsWith("@@") ||
        line.StartsWith("diff ") || line.StartsWith("index ")
            ? DiffLineKind.Header
            : line.StartsWith('+')
                ? DiffLineKind.Added
                : line.StartsWith('-')
                    ? DiffLineKind.Removed
                    : DiffLineKind.Context;

    private static IReadOnlyList<DiffDiagnostic> BuildDiagnostics(IReadOnlyList<DiffLine> lines)
    {
        var content = new List<DiffContentLine>();
        var diagnostics = new List<DiffDiagnostic>();
        var inHunk = false;
        var oldLine = 0;
        var newLine = 0;
        var lastContentIndex = -1;

        foreach (var line in lines)
        {
            var text = line.Text;
            if (text.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                inHunk = false;
                lastContentIndex = -1;
                continue;
            }

            if (TryReadHunkStarts(text, out var oldStart, out var newStart))
            {
                inHunk = true;
                oldLine = oldStart;
                newLine = newStart;
                lastContentIndex = -1;
                continue;
            }

            if (!inHunk) continue;

            if (text.StartsWith("\\ No newline at end of file", StringComparison.Ordinal))
            {
                if (lastContentIndex >= 0)
                    content[lastContentIndex] = content[lastContentIndex] with { LineEnding = null };
                if (!diagnostics.Any(diagnostic => diagnostic.Kind == DiffDiagnosticKind.NoFinalNewline))
                    diagnostics.Add(new DiffDiagnostic(DiffDiagnosticKind.NoFinalNewline));
                continue;
            }

            if (text.StartsWith('+'))
            {
                content.Add(CreateContentLine(
                    DiffContentSide.Changed,
                    newLine,
                    text.AsSpan(1),
                    line.HadTrailingCarriageReturn));
                lastContentIndex = content.Count - 1;
                newLine++;
                continue;
            }

            if (text.StartsWith('-'))
            {
                content.Add(CreateContentLine(
                    DiffContentSide.Original,
                    oldLine,
                    text.AsSpan(1),
                    line.HadTrailingCarriageReturn));
                lastContentIndex = content.Count - 1;
                oldLine++;
                continue;
            }

            oldLine++;
            newLine++;
            lastContentIndex = -1;
        }

        AddBomDiagnostic(content, diagnostics);
        AddLineEndingDiagnostic(content, diagnostics);
        return diagnostics;
    }

    private static DiffContentLine CreateContentLine(
        DiffContentSide side,
        int lineNumber,
        ReadOnlySpan<char> content,
        bool hadTrailingCarriageReturn)
    {
        var hasBom = content.StartsWith("\uFEFF", StringComparison.Ordinal);
        if (hasBom) content = content[1..];
        return new DiffContentLine(
            side,
            lineNumber,
            content.ToString(),
            hasBom,
            hadTrailingCarriageReturn ? DiffTextLineEnding.CrLf : DiffTextLineEnding.Lf);
    }

    private static void AddBomDiagnostic(
        IReadOnlyList<DiffContentLine> content,
        ICollection<DiffDiagnostic> diagnostics)
    {
        var original = content.FirstOrDefault(line =>
            line.Side == DiffContentSide.Original && line.LineNumber == 1);
        var changed = content.FirstOrDefault(line =>
            line.Side == DiffContentSide.Changed && line.LineNumber == 1);
        if (original is null || changed is null ||
            !string.Equals(original.Text, changed.Text, StringComparison.Ordinal) ||
            original.HasBom == changed.HasBom)
            return;

        diagnostics.Add(new DiffDiagnostic(
            changed.HasBom ? DiffDiagnosticKind.Utf8BomAdded : DiffDiagnosticKind.Utf8BomRemoved));
    }

    private static void AddLineEndingDiagnostic(
        IReadOnlyList<DiffContentLine> content,
        ICollection<DiffDiagnostic> diagnostics)
    {
        var original = content.Where(line => line.Side == DiffContentSide.Original).ToArray();
        var changed = content.Where(line => line.Side == DiffContentSide.Changed).ToArray();
        if (original.Length == 0 || original.Length != changed.Length) return;
        if (!original.Select(line => line.Text).SequenceEqual(changed.Select(line => line.Text), StringComparer.Ordinal))
            return;
        if (original.Any(line => line.LineEnding is null) || changed.Any(line => line.LineEnding is null))
            return;

        var originalEndings = original.Select(line => line.LineEnding!.Value).Distinct().ToArray();
        var changedEndings = changed.Select(line => line.LineEnding!.Value).Distinct().ToArray();
        if (originalEndings.Length != 1 || changedEndings.Length != 1 ||
            originalEndings[0] == changedEndings[0])
            return;

        diagnostics.Add(new DiffDiagnostic(DiffDiagnosticKind.LineEndingsChanged)
        {
            OriginalLineEnding = originalEndings[0],
            ChangedLineEnding = changedEndings[0]
        });
    }

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

    private enum DiffContentSide { Original, Changed }

    private sealed record DiffContentLine(
        DiffContentSide Side,
        int LineNumber,
        string Text,
        bool HasBom,
        DiffTextLineEnding? LineEnding);
}

internal sealed record ParsedGitDiff(
    IReadOnlyList<DiffLine> Lines,
    IReadOnlyList<DiffDiagnostic> Diagnostics);
