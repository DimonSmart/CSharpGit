using System.Text;

namespace CSharpGit.Presentation.Controls;

internal sealed class GitOutputDisplayBuffer
{
    private readonly StringBuilder _text = new();
    private int _lineStart;
    private bool _pendingCarriageReturn;

    public string Text => _text.ToString();

    public string Reset(string rawText)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        _text.Clear();
        _lineStart = 0;
        _pendingCarriageReturn = false;
        Append(rawText);
        return _text.ToString();
    }

    public GitOutputDisplayDelta Append(string chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.Length == 0) return GitOutputDisplayDelta.None;

        var originalLength = _text.Length;
        int? replaceFrom = null;

        foreach (var character in chunk)
        {
            if (_pendingCarriageReturn)
            {
                _pendingCarriageReturn = false;
                if (character == '\n')
                {
                    _text.Append('\n');
                    _lineStart = _text.Length;
                    continue;
                }

                replaceFrom = replaceFrom is { } current
                    ? Math.Min(current, _lineStart)
                    : _lineStart;
                _text.Remove(_lineStart, _text.Length - _lineStart);
            }

            if (character == '\r')
            {
                _pendingCarriageReturn = true;
                continue;
            }

            if (character == '\n')
            {
                _text.Append('\n');
                _lineStart = _text.Length;
                continue;
            }

            _text.Append(character);
        }

        if (replaceFrom is { } index)
            return new GitOutputDisplayDelta(index, _text.ToString(index, _text.Length - index));

        if (_text.Length == originalLength) return GitOutputDisplayDelta.None;
        return new GitOutputDisplayDelta(null, _text.ToString(originalLength, _text.Length - originalLength));
    }
}

internal readonly record struct GitOutputDisplayDelta(int? ReplaceFrom, string Text)
{
    public static GitOutputDisplayDelta None { get; } = new(null, string.Empty);
    public bool HasChange => ReplaceFrom is not null || Text.Length > 0;
    public bool IsAppend => ReplaceFrom is null && Text.Length > 0;
}
