using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryRefreshFlightTests
{
    [Fact]
    public async Task ConcurrentRequestsRunOneReadAtATimeAndPublishOnlyLatest()
    {
        var flight = new RepositoryRefreshFlight();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var published = 0;
        var starts = 0;
        var completions = 0;
        var historyFlags = new List<bool>();

        async Task Pass(bool includeHistory, Func<Action, bool> tryPublish)
        {
            var pass = ++reads;
            historyFlags.Add(includeHistory);
            if (pass == 1)
            {
                entered.SetResult();
                await release.Task;
            }
            tryPublish(() => published = pass);
        }

        var first = flight.RunAsync(false, Pass, () => starts++, () => completions++);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = flight.RunAsync(true, Pass, () => starts++, () => completions++);
        var third = flight.RunAsync(false, Pass, () => starts++, () => completions++);
        Assert.Same(first, second);
        Assert.Same(first, third);
        release.SetResult();
        await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, reads);
        Assert.Equal(2, published);
        Assert.Equal([false, true], historyFlags);
        Assert.Equal(1, starts);
        Assert.Equal(1, completions);
    }

    [Fact]
    public async Task NewRequestAfterFailedReadCanStillPublish()
    {
        var flight = new RepositoryRefreshFlight();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var published = 0;
        async Task Pass(bool _, Func<Action, bool> tryPublish)
        {
            var pass = ++reads;
            if (pass == 1)
            {
                entered.SetResult();
                await release.Task;
                throw new IOException("The superseded read failed");
            }
            tryPublish(() => published = pass);
        }

        var first = flight.RunAsync(false, Pass, () => { }, () => { });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = flight.RunAsync(false, Pass, () => { }, () => { });
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, reads);
        Assert.Equal(2, published);
    }

    [Fact]
    public async Task FailedReadWithoutNewRequestDoesNotPublishAndReleasesFlight()
    {
        var flight = new RepositoryRefreshFlight();
        var ends = 0;
        var first = flight.RunAsync(false,
            (_, _) => throw new IOException("Read failed"),
            () => { }, () => ends++);
        await Assert.ThrowsAsync<IOException>(() => first);
        var published = false;
        await flight.RunAsync(false, (_, publish) =>
        {
            Assert.True(publish(() => published = true));
            return Task.CompletedTask;
        }, () => { }, () => ends++);

        Assert.True(published);
        Assert.Equal(2, ends);
    }

    [Fact]
    public async Task PendingRequestCannotInterruptAlreadyAtomicPublication()
    {
        var flight = new RepositoryRefreshFlight();
        var publications = new List<string>();
        await flight.RunAsync(false, (_, publish) =>
        {
            var success = publish(() =>
            {
                publications.Add("head");
                publications.Add("branches");
                publications.Add("baseline");
            });
            Assert.True(success);
            return Task.CompletedTask;
        }, () => { }, () => { });
        Assert.Equal(["head", "branches", "baseline"], publications);
    }
}
