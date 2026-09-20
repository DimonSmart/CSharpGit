using System.Text;

namespace CSharpGit.Application;

internal sealed class BoundedUtf8TextBuffer
{
    private readonly int _maximumBytes;
    private readonly string _truncationMarker;
    private readonly int _truncationMarkerBytes;
    private readonly StringBuilder _builder = new();
    private int _byteCount;

    public BoundedUtf8TextBuffer(int maximumBytes, string truncationMarker)
    {
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        ArgumentNullException.ThrowIfNull(truncationMarker);

        var markerBytes = Encoding.UTF8.GetByteCount(truncationMarker);
        if (markerBytes > maximumBytes)
            throw new ArgumentException("The truncation marker must fit inside the buffer limit.", nameof(truncationMarker));

        _maximumBytes = maximumBytes;
        _truncationMarker = truncationMarker;
        _truncationMarkerBytes = markerBytes;
    }

    public int ByteCount => _byteCount;
    public bool Truncated { get; private set; }

    public BoundedUtf8AppendResult Append(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0 || Truncated) return BoundedUtf8AppendResult.None;

        var incomingBytes = Encoding.UTF8.GetByteCount(value);
        if (_byteCount + incomingBytes <= _maximumBytes)
        {
            _builder.Append(value);
            _byteCount += incomingBytes;
            return new BoundedUtf8AppendResult(true, value, false);
        }

        var payloadBudget = _maximumBytes - _truncationMarkerBytes;
        var requiresResync = false;
        string publishedPrefix;

        if (_byteCount > payloadBudget)
        {
            TrimExistingToUtf8Bytes(payloadBudget);
            requiresResync = true;
            publishedPrefix = string.Empty;
        }
        else
        {
            publishedPrefix = TakeUtf8Prefix(value, payloadBudget - _byteCount);
            if (publishedPrefix.Length > 0)
            {
                _builder.Append(publishedPrefix);
                _byteCount += Encoding.UTF8.GetByteCount(publishedPrefix);
            }
        }

        _builder.Append(_truncationMarker);
        _byteCount += _truncationMarkerBytes;
        Truncated = true;

        return new BoundedUtf8AppendResult(
            true,
            requiresResync ? _truncationMarker : publishedPrefix + _truncationMarker,
            requiresResync);
    }

    public override string ToString() => _builder.ToString();

    private void TrimExistingToUtf8Bytes(int maximumBytes)
    {
        var value = _builder.ToString();
        var prefix = TakeUtf8Prefix(value, maximumBytes);
        _builder.Clear();
        _builder.Append(prefix);
        _byteCount = Encoding.UTF8.GetByteCount(prefix);
    }

    private static string TakeUtf8Prefix(string value, int maximumBytes)
    {
        if (maximumBytes <= 0 || value.Length == 0) return string.Empty;

        var byteCount = 0;
        var charCount = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (byteCount + rune.Utf8SequenceLength > maximumBytes) break;
            byteCount += rune.Utf8SequenceLength;
            charCount += rune.Utf16SequenceLength;
        }

        return charCount == value.Length ? value : value[..charCount];
    }
}

internal readonly record struct BoundedUtf8AppendResult(
    bool Changed,
    string PublishedChunk,
    bool RequiresResync)
{
    public static BoundedUtf8AppendResult None { get; } = new(false, string.Empty, false);
}
