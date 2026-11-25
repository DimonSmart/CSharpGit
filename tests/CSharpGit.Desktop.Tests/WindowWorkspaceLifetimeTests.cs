using CSharpGit.Presentation.Windowing;

namespace CSharpGit.Desktop.Tests;

public sealed class WindowWorkspaceLifetimeTests
{
    [Fact]
    public async Task PreparationClosesAdmissionBeforeCallbacksAndSharesPendingDiagnostics()
    {
        var lifetime = new WindowWorkspaceLifetime();
        var diagnostics = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        Task? reentrant = null;
        lifetime.Configure(
            [() => { Assert.True(lifetime.IsShuttingDown); calls.Add("cancel"); reentrant = lifetime.PrepareAsync(); },
             () => calls.Add("detach")],
            () => { calls.Add("diagnostics"); return diagnostics.Task; },
            [() => calls.Add("dispose")]);

        Assert.False(lifetime.IsShuttingDown);
        var first = lifetime.PrepareAsync();
        Assert.Same(first, reentrant);
        Assert.Same(first, lifetime.PrepareAsync());
        Assert.False(first.IsCompleted);
        Assert.Equal(new[] { "cancel", "detach", "diagnostics" }, calls);
        diagnostics.SetResult();
        await first;
        lifetime.Complete();
        lifetime.Complete();
        await lifetime.PrepareAsync();
        Assert.Equal(new[] { "cancel", "detach", "diagnostics", "dispose" }, calls);
    }

    [Fact]
    public async Task PreparationRunsRemainingCallbacksAndWaitsForDiagnosticsAfterFailure()
    {
        var lifetime = new WindowWorkspaceLifetime();
        var diagnostics = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var cancellationFailure = new InvalidOperationException("cancel failed");
        var diagnosticsFailure = new IOException("diagnostics failed");
        lifetime.Configure(
            [() => throw cancellationFailure, () => calls.Add("detach")],
            () => { calls.Add("diagnostics"); return diagnostics.Task; },
            [() => calls.Add("dispose")]);
        var preparation = lifetime.PrepareAsync();
        Assert.False(preparation.IsCompleted);
        Assert.Equal(new[] { "detach", "diagnostics" }, calls);
        diagnostics.SetException(diagnosticsFailure);
        var error = await Assert.ThrowsAsync<AggregateException>(() => preparation);
        Assert.Equal(new Exception[] { cancellationFailure, diagnosticsFailure }, error.InnerExceptions);
        Assert.Same(preparation, lifetime.PrepareAsync());
        lifetime.Complete();
        Assert.Equal(new[] { "detach", "diagnostics", "dispose" }, calls);
    }

    [Fact]
    public async Task CompletionWithoutPreparationFencesLateWorkAndDoesNotRunPreparation()
    {
        var lifetime = new WindowWorkspaceLifetime();
        var completed = 0;
        lifetime.Configure(
            [() => throw new InvalidOperationException("unexpected preparation")],
            () => throw new InvalidOperationException("unexpected diagnostics"),
            [() => { Assert.True(lifetime.IsShuttingDown); completed++; }]);
        lifetime.Complete();
        lifetime.Complete();
        await lifetime.PrepareAsync();
        Assert.True(lifetime.IsShuttingDown);
        Assert.Equal(1, completed);
    }

    [Fact]
    public async Task CompletionRunsEveryCleanupOnceEvenWhenSeveralFail()
    {
        var lifetime = new WindowWorkspaceLifetime();
        var calls = new List<int>();
        var first = new InvalidOperationException("first");
        var last = new IOException("last");
        lifetime.Configure([], () => Task.CompletedTask,
            [() => { calls.Add(1); throw first; },
             () => { Assert.True(lifetime.IsShuttingDown); calls.Add(2); lifetime.Complete(); },
             () => { calls.Add(3); throw last; }]);

        await lifetime.PrepareAsync();
        var error = Assert.Throws<AggregateException>(lifetime.Complete);
        Assert.Equal(new Exception[] { first, last }, error.InnerExceptions);
        lifetime.Complete();
        Assert.Equal(new[] { 1, 2, 3 }, calls);
    }

    [Fact]
    public async Task BoundedShutdownMayCompleteWhileDiagnosticsAreStillPending()
    {
        var lifetime = new WindowWorkspaceLifetime();
        var diagnostics = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupCount = 0;
        lifetime.Configure([], () => diagnostics.Task, [() => cleanupCount++]);
        var pending = lifetime.PrepareAsync();
        lifetime.Complete();
        Assert.True(lifetime.IsShuttingDown);
        Assert.False(pending.IsCompleted);
        Assert.Equal(1, cleanupCount);
        diagnostics.SetResult();
        await pending;
        lifetime.Complete();
        Assert.Equal(1, cleanupCount);
    }
}
