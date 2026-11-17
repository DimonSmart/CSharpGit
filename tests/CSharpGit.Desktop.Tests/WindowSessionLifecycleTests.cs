using CSharpGit.Presentation;

namespace CSharpGit.Desktop.Tests;

public sealed class WindowSessionLifecycleTests
{
    [Fact]
    public async Task InitializesOnceAndTransitionsToActive()
    {
        var lifetime = new WindowSessionLifecycle();
        var calls = 0;
        Task Initialize(CancellationToken _) { calls++; return Task.CompletedTask; }

        var first = lifetime.InitializeAsync(Initialize, () => { }, CancellationToken.None);
        var second = lifetime.InitializeAsync(Initialize, () => { }, CancellationToken.None);
        await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.Equal(WindowSessionState.Active, lifetime.State);
    }

    [Fact]
    public async Task ClosePreparationIsIdempotentAndDisallowsReinitialization()
    {
        var lifetime = new WindowSessionLifecycle();
        await lifetime.InitializeAsync(_ => Task.CompletedTask, () => { }, CancellationToken.None);
        var calls = 0;
        Task Prepare(CancellationToken _) { calls++; return Task.CompletedTask; }
        await Task.WhenAll(
            lifetime.PrepareAsync(Prepare, CancellationToken.None),
            lifetime.PrepareAsync(Prepare, CancellationToken.None));
        Assert.Equal(1, calls);
        Assert.Equal(WindowSessionState.PreparingForShutdown, lifetime.State);
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = lifetime.InitializeAsync(_ => Task.CompletedTask, () => { }, CancellationToken.None);
        });
        Assert.True(lifetime.TryComplete());
        Assert.False(lifetime.TryComplete());
        Assert.Equal(WindowSessionState.Disposed, lifetime.State);
        await lifetime.PrepareAsync(Prepare, CancellationToken.None);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FailedInitializationInvokesCleanupAndCannotRevive()
    {
        var lifetime = new WindowSessionLifecycle();
        var cleanup = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifetime.InitializeAsync(
                _ => Task.FromException(new InvalidOperationException("startup")),
                () => { cleanup++; lifetime.TryComplete(); },
                CancellationToken.None));

        Assert.Equal(1, cleanup);
        Assert.Equal(WindowSessionState.Disposed, lifetime.State);
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = lifetime.InitializeAsync(_ => Task.CompletedTask, () => { }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task LateInitializationCannotReactivateClosingWindow()
    {
        var lifetime = new WindowSessionLifecycle();
        var continuation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initialization = lifetime.InitializeAsync(_ => continuation.Task, () => { }, CancellationToken.None);
        await lifetime.PrepareAsync(_ => Task.CompletedTask, CancellationToken.None);
        continuation.SetResult();
        await initialization;
        Assert.Equal(WindowSessionState.PreparingForShutdown, lifetime.State);
        Assert.True(lifetime.TryComplete());
        Assert.Equal(WindowSessionState.Disposed, lifetime.State);
    }

    [Fact]
    public async Task ConcurrentShutdownRequestsShareOnePreparation()
    {
        var lifetime = new WindowSessionLifecycle();
        var continuation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task Prepare(CancellationToken _) { Interlocked.Increment(ref calls); return continuation.Task; }
        var first = lifetime.PrepareAsync(Prepare, CancellationToken.None);
        var second = lifetime.PrepareAsync(Prepare, CancellationToken.None);
        Assert.Same(first, second);
        Assert.Equal(1, calls);
        continuation.SetResult();
        await Task.WhenAll(first, second);
        Assert.True(lifetime.TryComplete());
        Assert.False(lifetime.TryComplete());
    }
}
