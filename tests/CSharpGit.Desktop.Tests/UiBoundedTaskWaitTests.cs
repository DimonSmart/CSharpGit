using CSharpGit.Presentation.Threading;

namespace CSharpGit.Desktop.Tests;

public sealed class UiBoundedTaskWaitTests
{
    [Fact]
    public async Task CompletedWriterReturnsSuccessWithoutNotification()
    {
        var errors = new List<Exception>();
        var outcome = await UiBoundedTaskWait.WaitAsync(
            Task.CompletedTask, TimeSpan.Zero, errors.Add);
        Assert.Equal(UiBoundedWaitOutcome.Completed, outcome);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task PendingWriterFinishingNormallyDoesNotBlockOrCauseLateError()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<Exception>();
        var outcome = await UiBoundedTaskWait.WaitAsync(pending.Task, TimeSpan.Zero, errors.Add);
        Assert.Equal(UiBoundedWaitOutcome.TimedOut, outcome);
        Assert.Single(errors);
        Assert.IsType<TimeoutException>(errors[0]);
        pending.SetResult();
        await pending.Task;
    }

    [Fact]
    public async Task FaultBeforeDeadlineIsObserved()
    {
        var errors = new List<Exception>();
        var original = new InvalidOperationException("writer failed");
        var outcome = await UiBoundedTaskWait.WaitAsync(
            Task.FromException(original), TimeSpan.FromSeconds(1), errors.Add);
        Assert.Equal(UiBoundedWaitOutcome.Faulted, outcome);
        Assert.Same(original, Assert.Single(errors));
    }

    [Fact]
    public async Task LateWriterFaultIsObservedAfterBudgetExpires()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateFault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = 0;
        var outcome = await UiBoundedTaskWait.WaitAsync(pending.Task, TimeSpan.Zero, exception =>
        {
            if (exception is not TimeoutException)
            {
                Interlocked.Increment(ref observed);
                lateFault.TrySetResult(exception);
            }
        });
        Assert.Equal(UiBoundedWaitOutcome.TimedOut, outcome);
        var failure = new IOException("late disk error");
        pending.SetException(failure);
        Assert.Same(failure, await lateFault.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, observed);
    }

    [Fact]
    public async Task ErrorReporterFailureCannotPreventShutdown()
    {
        var fault = new InvalidOperationException("writer");
        var result = await UiBoundedTaskWait.WaitAsync(
            Task.FromException(fault), TimeSpan.FromSeconds(1),
            _ => throw new IOException("logger"));
        Assert.Equal(UiBoundedWaitOutcome.Faulted, result);

        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = await UiBoundedTaskWait.WaitAsync(
            pending.Task, TimeSpan.Zero, _ => throw new IOException("logger"));
        Assert.Equal(UiBoundedWaitOutcome.TimedOut, timeout);
        pending.SetResult();
    }
}
