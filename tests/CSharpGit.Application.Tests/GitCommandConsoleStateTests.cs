using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;

namespace CSharpGit.Application.Tests;

public sealed class GitCommandConsoleStateTests
{
    [Fact]
    public void LifecycleUpdatesSamePresentationItem()
    {
        var state = new GitCommandConsoleState();
        var running = Activity(Guid.NewGuid(), GitCommandKind.User, GitCommandStatus.Running);
        state.Reset(GitCommandFilter.UserCommands, []);
        state.ApplyStarted(running, null);
        var item = Assert.Single(state.Items);

        state.ApplyLifecycle(running with
        {
            Status = GitCommandStatus.Succeeded,
            ExitCode = 0,
            Duration = TimeSpan.FromSeconds(3),
            CompletedAt = DateTimeOffset.UtcNow
        });

        Assert.Same(item, Assert.Single(state.Items));
        Assert.Equal(GitCommandStatus.Succeeded, item.Status);
        Assert.Equal(0, item.ExitCode);
    }

    [Theory]
    [InlineData(GitCommandStatus.Failed)]
    [InlineData(GitCommandStatus.Cancelled)]
    public void FinalStatesDoNotRecreateItem(GitCommandStatus status)
    {
        var state = new GitCommandConsoleState();
        var running = Activity(Guid.NewGuid(), GitCommandKind.User, GitCommandStatus.Running);
        state.Reset(GitCommandFilter.UserCommands, [running]);
        var item = Assert.Single(state.Items);

        state.ApplyLifecycle(running with { Status = status, ExitCode = 1 });

        Assert.Same(item, Assert.Single(state.Items));
        Assert.Equal(status, item.Status);
    }

    [Fact]
    public void HiddenInternalActivityDoesNotChangeUserCollection()
    {
        var state = new GitCommandConsoleState();
        var user = Activity(Guid.NewGuid(), GitCommandKind.User, GitCommandStatus.Running);
        state.Reset(GitCommandFilter.UserCommands, [user]);
        var existing = Assert.Single(state.Items);

        state.ApplyStarted(Activity(Guid.NewGuid(), GitCommandKind.Internal, GitCommandStatus.Running), null);

        Assert.Same(existing, Assert.Single(state.Items));
    }

    [Fact]
    public void InternalActivityMayOnlyRemoveEvictedVisibleUser()
    {
        var state = new GitCommandConsoleState();
        var user = Activity(Guid.NewGuid(), GitCommandKind.User, GitCommandStatus.Running);
        state.Reset(GitCommandFilter.UserCommands, [user]);

        state.ApplyStarted(
            Activity(Guid.NewGuid(), GitCommandKind.Internal, GitCommandStatus.Running),
            user.Id);

        Assert.Empty(state.Items);
    }

    [Fact]
    public void RunningDurationUpdatesOnlyRunningItems()
    {
        var now = DateTimeOffset.UtcNow;
        var running = Activity(Guid.NewGuid(), GitCommandKind.User, GitCommandStatus.Running, now - TimeSpan.FromSeconds(18.4));
        var done = Activity(Guid.NewGuid(), GitCommandKind.User, GitCommandStatus.Succeeded, now - TimeSpan.FromSeconds(10)) with
        {
            Duration = TimeSpan.FromSeconds(2)
        };
        var state = new GitCommandConsoleState();
        state.Reset(GitCommandFilter.UserCommands, [running, done]);

        foreach (var item in state.Items)
            item.UpdateRunningDuration(now);

        Assert.InRange(state.Find(running.Id)!.Duration.TotalSeconds, 18.39, 18.41);
        Assert.Equal(TimeSpan.FromSeconds(2), state.Find(done.Id)!.Duration);
    }

    private static GitCommandActivity Activity(
        Guid id,
        GitCommandKind kind,
        GitCommandStatus status,
        DateTimeOffset? startedAt = null) =>
        new(
            id,
            startedAt ?? DateTimeOffset.UtcNow,
            null,
            TimeSpan.Zero,
            "/repo",
            "git",
            ["fetch"],
            "git fetch",
            kind,
            null,
            string.Empty,
            string.Empty,
            status,
            false,
            false);
}
