using CSharpGit.Presentation.Threading;

namespace CSharpGit.Desktop.Tests;

public sealed class UiPageDialogGateTests
{
    [Fact]
    public async Task ConcurrentDialogsRunOneAtATimeAndKeepTheirOwnResults()
    {
        var gate = new UiPageDialogGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCalls = 0;

        var first = gate.ExecuteAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
            return 10;
        }, -1, CancellationToken.None);
        await entered.Task;
        var second = gate.ExecuteAsync(() =>
        {
            secondCalls++;
            return Task.FromResult(20);
        }, -1, CancellationToken.None);

        Assert.Equal(0, secondCalls);
        Assert.False(second.IsCompleted);
        release.SetResult();
        Assert.Equal(10, await first);
        Assert.Equal(20, await second);
        Assert.Equal(1, secondCalls);
    }

    [Fact]
    public async Task ShutdownCancelsQueuedDialogWithoutShowingIt()
    {
        var gate = new UiPageDialogGate();
        using var shutdown = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = gate.ExecuteAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
            return true;
        }, false, shutdown.Token);
        await entered.Task;

        var showCount = 0;
        var queued = gate.ExecuteAsync(() =>
        {
            showCount++;
            return Task.FromResult(true);
        }, false, shutdown.Token);

        shutdown.Cancel();
        Assert.False(await queued.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(0, showCount);
        release.SetResult();
        Assert.False(await first);
        Assert.False(await gate.ExecuteAsync(() =>
        {
            showCount++;
            return Task.FromResult(true);
        }, false, shutdown.Token));
        Assert.Equal(0, showCount);
    }

    [Fact]
    public async Task ExceptionReleasesGateAndDoesNotMaskOriginalFailure()
    {
        var gate = new UiPageDialogGate();
        var failure = new InvalidOperationException("ShowAsync failed");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.ExecuteAsync<int>(() => Task.FromException<int>(failure), -1, CancellationToken.None));
        Assert.Same(failure, thrown);
        Assert.Equal(3, await gate.ExecuteAsync(() => Task.FromResult(3), -1, CancellationToken.None));
    }

    [Fact]
    public async Task UnexpectedCancellationPropagatesWhileLifecycleCancellationIsSilent()
    {
        var gate = new UiPageDialogGate();
        var failure = new OperationCanceledException("unexpected");
        Assert.Same(failure, await Assert.ThrowsAsync<OperationCanceledException>(() =>
            gate.ExecuteAsync<int>(() => Task.FromException<int>(failure), -1, CancellationToken.None)));
        using var shutdown = new CancellationTokenSource();
        shutdown.Cancel();
        Assert.Equal(-1, await gate.ExecuteAsync(() => Task.FromResult(1), -1, shutdown.Token));
    }
}
