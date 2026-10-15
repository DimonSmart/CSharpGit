using System.Text;
using System.Threading.Channels;

namespace CSharpGit.Presentation.Diagnostics;

/// <summary>Nonblocking bounded logging queue used by the file logger.</summary>
internal sealed class BoundedDiagnosticQueue
{
    internal const int MaximumQueuedEntries = 512;
    internal const int MaximumEntryBytes = 16 * 1024;

    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(MaximumQueuedEntries)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    private long _pendingDropped;
    private long _totalDropped;
    private int _closed;
    private int _failed;

    internal ChannelReader<string> Reader => _channel.Reader;
    internal long PendingDropped => Interlocked.Read(ref _pendingDropped);
    internal long TotalDropped => Interlocked.Read(ref _totalDropped);
    internal bool HasFailed => Volatile.Read(ref _failed) != 0;

    internal bool TryWrite(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Volatile.Read(ref _closed) == 0
            && Volatile.Read(ref _failed) == 0
            && _channel.Writer.TryWrite(LimitEntry(line)))
            return true;

        Interlocked.Increment(ref _pendingDropped);
        Interlocked.Increment(ref _totalDropped);
        return false;
    }

    internal long TakePendingDropped() => Interlocked.Exchange(ref _pendingDropped, 0);
    internal void RestorePendingDropped(long count)
    {
        if (count > 0) Interlocked.Add(ref _pendingDropped, count);
    }

    internal void Complete()
    {
        Interlocked.Exchange(ref _closed, 1);
        _channel.Writer.TryComplete();
    }

    internal void Fail()
    {
        Interlocked.Exchange(ref _failed, 1);
        Complete();
    }

    internal static string LimitEntry(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Encoding.UTF8.GetByteCount(text) <= MaximumEntryBytes) return text;

        const string marker = " [log entry truncated]";
        var budget = MaximumEntryBytes - Encoding.UTF8.GetByteCount(marker);
        var bytes = 0;
        var end = 0;
        while (end < text.Length)
        {
            if (!Rune.TryGetRuneAt(text, end, out var rune))
                rune = Rune.ReplacementChar;
            if (bytes + rune.Utf8SequenceLength > budget) break;
            bytes += rune.Utf8SequenceLength;
            end += rune.Utf16SequenceLength;
        }

        return string.Concat(text.AsSpan(0, end), marker);
    }
}
