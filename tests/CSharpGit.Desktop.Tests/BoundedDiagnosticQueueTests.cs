using System.Text;
using CSharpGit.Presentation.Diagnostics;

namespace CSharpGit.Desktop.Tests;

public sealed class BoundedDiagnosticQueueTests
{
    [Fact]
    public async Task ManyProducersNeverGrowQueueAndAccountForDroppedEntries()
    {
        var queue = new BoundedDiagnosticQueue();
        const int producers = 16;
        const int writesPerProducer = 1000;
        await Task.WhenAll(Enumerable.Range(0, producers).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < writesPerProducer; i++)
                queue.TryWrite("entry");
        })));

        Assert.Equal(producers * writesPerProducer - BoundedDiagnosticQueue.MaximumQueuedEntries,
            queue.TotalDropped);
        var received = 0;
        while (queue.Reader.TryRead(out _))
            received++;
        Assert.Equal(BoundedDiagnosticQueue.MaximumQueuedEntries, received);
        Assert.Equal(queue.TotalDropped, queue.TakePendingDropped());
        Assert.Equal(0, queue.TakePendingDropped());
    }

    [Fact]
    public void OversizedEntryHasUtf8ByteBoundAndDoesNotSplitUnicode()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 10000));
        var limited = BoundedDiagnosticQueue.LimitEntry(text);

        Assert.True(Encoding.UTF8.GetByteCount(limited) <= BoundedDiagnosticQueue.MaximumEntryBytes);
        Assert.EndsWith(" [log entry truncated]", limited, StringComparison.Ordinal);
        Assert.DoesNotContain("\uFFFD", limited, StringComparison.Ordinal);
        var prefix = limited[..^" [log entry truncated]".Length];
        Assert.Equal(0, prefix.Length % 2);
        foreach (var rune in prefix.EnumerateRunes())
            Assert.Equal(0x1F600, rune.Value);
    }

    [Fact]
    public void ClosedAndFailedQueueDropsWithoutBlocking()
    {
        var queue = new BoundedDiagnosticQueue();
        Assert.True(queue.TryWrite("before"));
        queue.Fail();
        Assert.True(queue.HasFailed);
        Assert.False(queue.TryWrite("after"));
        queue.Complete();
        Assert.False(queue.TryWrite("after completion"));
        Assert.Equal(2, queue.TotalDropped);
        Assert.True(queue.Reader.TryRead(out var retained));
        Assert.Equal("before", retained);
    }

    [Fact]
    public void DropCountCanBeRestoredAfterWriteFailureWithoutDoubleCounting()
    {
        var queue = new BoundedDiagnosticQueue();
        for (var i = 0; i <= BoundedDiagnosticQueue.MaximumQueuedEntries; i++)
            queue.TryWrite("line");
        var count = queue.TakePendingDropped();
        Assert.Equal(1, count);
        queue.RestorePendingDropped(count);
        Assert.Equal(1, queue.TakePendingDropped());
        Assert.Equal(1, queue.TotalDropped);
    }
}
