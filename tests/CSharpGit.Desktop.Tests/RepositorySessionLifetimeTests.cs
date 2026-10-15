using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositorySessionLifetimeTests
{
    [Fact]
    public void SessionCannotAcceptReadsAfterEnd()
    {
        var session = new RepositorySessionLifetime(1);
        Assert.True(session.TryAcquire(out var token));
        session.End();
        Assert.False(session.IsActive);
        Assert.True(token.IsCancellationRequested);
        Assert.False(session.TryAcquire(out _));
        session.Release();
        session.End();
    }

    [Fact]
    public void SeparateSessionRemainsActiveWhenEarlierSessionEnds()
    {
        var original = new RepositorySessionLifetime(1);
        var restored = new RepositorySessionLifetime(2);
        Assert.True(original.TryAcquire(out var cancelled));
        Assert.True(restored.TryAcquire(out var active));
        original.End();

        Assert.True(cancelled.IsCancellationRequested);
        Assert.False(active.IsCancellationRequested);
        Assert.Equal(2, restored.Generation);
        original.Release();
        restored.Release();
        restored.End();
    }

    [Fact]
    public async Task ReopeningSameRepositoryRejectsEarlierGenerationPublication()
    {
        var older = new RepositorySessionLifetime(1);
        var middle = new RepositorySessionLifetime(2);
        var latest = new RepositorySessionLifetime(3);
        var flight = new RepositoryRefreshFlight();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<long>();

        Assert.True(older.TryAcquire(out var oldToken));
        var oldRead = flight.RunAsync(false, async (_, publish) =>
        {
            entered.TrySetResult();
            await release.Task;
            if (!oldToken.IsCancellationRequested && older.IsActive)
                publish(() => published.Add(older.Generation));
        }, () => { }, () => { });

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        older.End();
        middle.End();

        Assert.True(latest.TryAcquire(out var newToken));
        Assert.False(newToken.IsCancellationRequested);
        var newestFlight = new RepositoryRefreshFlight();
        await newestFlight.RunAsync(false, (_, publish) =>
        {
            Assert.True(publish(() => published.Add(latest.Generation)));
            return Task.CompletedTask;
        }, () => { }, () => { });

        release.SetResult();
        await oldRead.WaitAsync(TimeSpan.FromSeconds(10));
        older.Release();
        latest.Release();
        latest.End();
        Assert.Equal([3L], published);
    }

    [Fact]
    public async Task ConcurrentEndAndCompletionIsSafe()
    {
        for (var i = 0; i < 100; i++)
        {
            var session = new RepositorySessionLifetime(i);
            Assert.True(session.TryAcquire(out _));
            await Task.WhenAll(
                Task.Run(session.End),
                Task.Run(session.Release));
            session.End();
            Assert.False(session.TryAcquire(out _));
        }
    }
}
