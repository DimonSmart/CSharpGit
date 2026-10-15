using CSharpGit.Presentation.Threading;

namespace CSharpGit.Desktop.Tests;

public sealed class UiAsyncOperationRunnerTests
{
    [Fact]
    public async Task SuccessAndSynchronousFailureAreObserved()
    {
        var observed = new List<Exception>();
        await UiAsyncOperationRunner.RunAsync(() => Task.CompletedTask, observed.Add);
        Assert.Empty(observed);
        var failure = new InvalidOperationException("before await");
        await UiAsyncOperationRunner.RunAsync(() => throw failure, observed.Add);
        Assert.Same(failure, Assert.Single(observed));
    }

    [Fact]
    public async Task FaultedAndAsynchronouslyFaultingTasksAreObserved()
    {
        var errors = new List<Exception>();
        var first = new InvalidOperationException("faulted");
        await UiAsyncOperationRunner.RunAsync(() => Task.FromException(first), errors.Add);
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new InvalidOperationException("after await");
        async Task FailLater()
        {
            await source.Task;
            throw second;
        }
        var running = UiAsyncOperationRunner.RunAsync(FailLater, errors.Add);
        source.SetResult();
        await running;
        Assert.Equal(new[] { first, second }, errors);
    }

    [Fact]
    public async Task OnlyExpectedCancellationIsIgnored()
    {
        var exceptions = new List<Exception>();
        var canceled = new OperationCanceledException();
        await UiAsyncOperationRunner.RunAsync(() => Task.FromException(canceled), exceptions.Add, () => true);
        Assert.Empty(exceptions);
        await UiAsyncOperationRunner.RunAsync(() => Task.FromException(canceled), exceptions.Add, () => false);
        Assert.Same(canceled, Assert.Single(exceptions));
    }

    [Fact]
    public async Task ReporterFailureDoesNotBecomeAnUnobservedFailure()
    {
        await UiAsyncOperationRunner.RunAsync(
            () => Task.FromException(new InvalidOperationException("original")),
            _ => throw new InvalidOperationException("reporter"));
    }
}
