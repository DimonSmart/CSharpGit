using System.Text;

namespace CSharpGit.Presentation.ViewModels;

public sealed class DiffLogicalText
{
    private readonly string[] _lines;
    private readonly int[] _lineStarts;

    public DiffLogicalText(IReadOnlyList<CompactDiffLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        _lines = lines.Select(line => line.Text ?? string.Empty).ToArray();
        _lineStarts = new int[_lines.Length];

        var builder = new StringBuilder();
        for (var index = 0; index < _lines.Length; index++)
        {
            if (index > 0) builder.Append(Environment.NewLine);
            _lineStarts[index] = builder.Length;
            builder.Append(_lines[index]);
        }

        Text = builder.ToString();
    }

    public string Text { get; }

    public int LineCount => _lines.Length;

    public int GetOffset(int lineIndex, int characterOffset)
    {
        if ((uint)lineIndex >= (uint)_lines.Length)
            throw new ArgumentOutOfRangeException(nameof(lineIndex));

        var line = _lines[lineIndex];
        if ((uint)characterOffset > (uint)line.Length)
            throw new ArgumentOutOfRangeException(nameof(characterOffset));

        return _lineStarts[lineIndex] + MoveBeforeSplitSurrogate(line, characterOffset);
    }

    public string GetSelectedText(
        int anchorLine,
        int anchorCharacter,
        int activeLine,
        int activeCharacter) =>
        GetSelectedText(
            GetOffset(anchorLine, anchorCharacter),
            GetOffset(activeLine, activeCharacter));

    public string GetSelectedText(int anchorOffset, int activeOffset)
    {
        ValidateOffset(anchorOffset, nameof(anchorOffset));
        ValidateOffset(activeOffset, nameof(activeOffset));

        if (anchorOffset == activeOffset) return string.Empty;

        var start = Math.Min(anchorOffset, activeOffset);
        var end = Math.Max(anchorOffset, activeOffset);

        start = MoveBeforeSplitSurrogate(Text, start);
        end = MoveAfterSplitSurrogate(Text, end);

        return Text[start..end];
    }

    private void ValidateOffset(int offset, string parameterName)
    {
        if ((uint)offset > (uint)Text.Length)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static int MoveBeforeSplitSurrogate(string text, int offset) =>
        offset > 0 &&
        offset < text.Length &&
        char.IsHighSurrogate(text[offset - 1]) &&
        char.IsLowSurrogate(text[offset])
            ? offset - 1
            : offset;

    private static int MoveAfterSplitSurrogate(string text, int offset) =>
        offset > 0 &&
        offset < text.Length &&
        char.IsHighSurrogate(text[offset - 1]) &&
        char.IsLowSurrogate(text[offset])
            ? offset + 1
            : offset;
}
