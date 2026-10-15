using CSharpGit.Presentation.Threading;

namespace CSharpGit.Desktop.Tests;

public sealed class UiDispatchCompletionTests
{
    [Fact]
    public async Task DispatcherRejectionAlwaysCompletesAwaitingTask()
    {
        var called = false;
        var task = UiDispatchCompletion.EnqueueAsync(_ => false, () => called = true);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Contains("dispatcher rejected", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(called);
    }

    [Fact]
    public async Task DispatcherThrowsBeforeQueueingIsObserved()
    {
        var failure = new IOException("dispatcher disposed");
        var task = UiDispatchCompletion.EnqueueAsync(_ => throw failure, () => {});
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => task));
    }

    [Fact]
    public async Task LateCallbackFromExpiredOwnerIsIgnoredAndCancelsTask()
    {
        Action? captured = null;
        var valid = true;
        var called = false;
        var task = UiDispatchCompletion.EnqueueAsync(action =>
        {
            captured = action;
            return true;
        }, () => called = true, () => valid);

        Assert.False(task.IsCompleted);
        valid = false;
        captured!();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(called);
        captured();
        Assert.False(called);
    }

    [Fact]
    public async Task SuccessfulAndFaultingCallbacksPropagateCorrectly()
    {
        Action? captured = null;
        var callCount = 0;
        var task = UiDispatchCompletion.EnqueueAsync(action =>
        {
            captured = action;
            return true;
        }, () => callCount++);
        captured!();
        await task;
        Assert.Equal(1, callCount);
        var error = new InvalidOperationException("callback");
        var failed = UiDispatchCompletion.EnqueueAsync(action =>
        {
            action();
            return true;
        }, () => throw error);
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => failed));
    }
}
