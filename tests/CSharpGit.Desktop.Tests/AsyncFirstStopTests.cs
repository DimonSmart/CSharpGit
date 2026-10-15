using CSharpGit.Presentation.Threading;

namespace CSharpGit.Desktop.Tests;

public sealed class AsyncFirstStopTests
{
    [Fact]
    public async Task FirstReasonWinsAndAllCallersAwaitSameStop()
    {
        var gate = new AsyncFirstStop<string, int>();
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        string? reason = null;
        Task<int> Stop(string why) => gate.StopAsync(why, value =>
        {
            calls++;
            reason = value;
            return pending.Task;
        });
        var first = Stop("manual");
        var second = Stop("shutdown");
        Assert.Same(first, second);
        Assert.Equal("manual", reason);
        Assert.Equal(1, calls);
        pending.SetResult(42);
        Assert.Equal(42, await first);
        Assert.Equal(42, await second);
        Assert.Same(first, Stop("timeout"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ReentrantStopDoesNotStartDuplicateOperation()
    {
        var gate = new AsyncFirstStop<int, int>();
        Task<int>? nested = null;
        var calls = 0;
        var first = gate.StopAsync(1, reason =>
        {
            calls++;
            nested = gate.StopAsync(2, _ => throw new InvalidOperationException("duplicate"));
            return Task.FromResult(reason);
        });
        Assert.Same(first, nested);
        Assert.Equal(1, await first);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SynchronousAndDelayedFailuresAreShared()
    {
        var error = new IOException("failed writer");
        var immediate = new AsyncFirstStop<int, bool>();
        var first = immediate.StopAsync(1, _ => throw error);
        Assert.Same(first, immediate.StopAsync(2, _ => Task.FromResult(true)));
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => first));

        var delayed = new AsyncFirstStop<int, bool>();
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var before = delayed.StopAsync(1, _ => pending.Task);
        var after = delayed.StopAsync(2, _ => Task.FromResult(true));
        Assert.Same(before, after);
        pending.SetException(error);
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => after));
    }
}
