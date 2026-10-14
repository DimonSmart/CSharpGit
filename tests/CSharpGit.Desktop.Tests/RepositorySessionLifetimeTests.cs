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
