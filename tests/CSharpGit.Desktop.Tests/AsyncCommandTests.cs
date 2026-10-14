using System.Windows.Input;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class AsyncCommandTests
{
    [Fact]
    public async Task ExecuteAsyncSucceedsAndReenablesCommand()
    {
        var called = 0;
        var command = new AsyncCommand(() =>
        {
            called++;
            return Task.CompletedTask;
        }, () => true);
        await command.ExecuteAsync();
        Assert.Equal(1, called);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task ExecuteAsyncPropagatesOriginalException()
    {
        var failure = new InvalidOperationException("failure");
        var command = new AsyncCommand(() => Task.FromException(failure), () => true);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(command.ExecuteAsync));
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task ICommandExecuteReportsErrorOnceAndReenablesCommand()
    {
        var failure = new InvalidOperationException("failure");
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var command = new AsyncCommand(
            () => Task.FromException(failure),
            () => true,
            errorHandler: exception =>
            {
                Interlocked.Increment(ref count);
                reported.TrySetResult(exception);
            });

        ((ICommand)command).Execute(null);
        Assert.Same(failure, await reported.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, count);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task CancellationDoesNotReportError()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = 0;
        var command = new AsyncCommand(
            () => Task.FromCanceled(new CancellationToken(true)),
            () => true,
            errorHandler: _ => Interlocked.Increment(ref errors));
        command.CanExecuteChanged += (_, _) =>
        {
            if (command.CanExecute(null))
                completed.TrySetResult();
        };
        ((ICommand)command).Execute(null);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, errors);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task ConcurrentInvocationIsBlocked()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var command = new AsyncCommand(() =>
        {
            calls++;
            return pending.Task;
        }, () => true);
        var execution = command.ExecuteAsync();
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.Equal(1, calls);
        pending.SetResult();
        await execution;
        Assert.True(command.CanExecute(null));
    }
}
