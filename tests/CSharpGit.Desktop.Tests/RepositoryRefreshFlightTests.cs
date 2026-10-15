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

    [Fact]
    public async Task CompletedCallbackCanReenterWithoutLosingNewFlight()
    {
        var flight = new RepositoryRefreshFlight();
        Task? followUp = null;
        var reads = 0;
        var completions = 0;

        var first = flight.RunAsync(false, (_, publish) =>
        {
            ++reads;
            Assert.True(publish(() => { }));
            return Task.CompletedTask;
        }, () => { }, () =>
        {
            ++completions;
            followUp = flight.RunAsync(false, (_, publish) =>
            {
                ++reads;
                Assert.True(publish(() => { }));
                return Task.CompletedTask;
            }, () => { }, () => ++completions);
        });

        await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(followUp);
        Assert.NotSame(first, followUp);
        await followUp!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, reads);
        Assert.Equal(2, completions);
    }

    [Fact]
    public async Task ReentrantStartingAndPublicationSubscribersDoNotDeadlock()
    {
        var flight = new RepositoryRefreshFlight();
        var reads = 0;
        Task? nested = null;
        async Task Pass(bool _, Func<Action, bool> publish)
        {
            ++reads;
            Assert.True(publish(() => { }));
            await Task.CompletedTask;
        }

        var first = flight.RunAsync(false, Pass,
            () => nested = flight.RunAsync(true, Pass, () => { }, () => { }),
            () => { });
        await first.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(first, nested);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task CoalescesFullAndHistoryRequirementsFromLaterRequests()
    {
        var flight = new RepositoryRefreshFlight();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var passes = new List<(bool History, bool LocalOnly)>();
        var published = 0;
        async Task Pass(bool history, bool localOnly, Func<Action, bool> publish)
        {
            passes.Add((history, localOnly));
            if (passes.Count == 1)
            {
                entered.TrySetResult();
                await release.Task;
            }
            publish(() => published = passes.Count);
        }

        var first = flight.RunAsync(false, true, Pass, () => { }, () => { });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var full = flight.RunAsync(false, false, Pass, () => { }, () => { });
        var history = flight.RunAsync(true, true, Pass, () => { }, () => { });
        Assert.Same(first, full);
        Assert.Same(first, history);
        release.SetResult();
        await Task.WhenAll(first, full, history).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([(false, true), (true, false)], passes);
        Assert.Equal(2, published);
    }

    [Fact]
    public async Task LocalRequestCannotDowngradeAnActiveFullRefresh()
    {
        var flight = new RepositoryRefreshFlight();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var passes = new List<(bool History, bool LocalOnly)>();
        async Task Pass(bool history, bool localOnly, Func<Action, bool> publish)
        {
            passes.Add((history, localOnly));
            if (passes.Count == 1)
            {
                entered.SetResult();
                await release.Task;
            }
            publish(() => { });
        }

        var first = flight.RunAsync(true, false, Pass, () => { }, () => { });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = flight.RunAsync(false, true, Pass, () => { }, () => { });
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([(true, false), (true, false)], passes);
    }

    [Fact]
    public async Task CompletedHandlerFailureDoesNotStrandAwaitersOrNextFlight()
    {
        var flight = new RepositoryRefreshFlight();
        var first = flight.RunAsync(false, (_, _) => Task.CompletedTask,
            () => { }, () => throw new InvalidOperationException("event handler"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        var reads = 0;
        await flight.RunAsync(false, (_, _) =>
        {
            ++reads;
            return Task.CompletedTask;
        }, () => { }, () => { }).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task PendingFullRefreshWinsEvenWhenOriginalLocalReadFails()
    {
        var flight = new RepositoryRefreshFlight();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = new List<bool>();

        async Task Pass(bool _, bool localOnly, Func<Action, bool> publish)
        {
            reads.Add(localOnly);
            if (reads.Count == 1)
            {
                started.SetResult();
                await resume.Task;
                throw new IOException("Superseded local read failed");
            }
            Assert.True(publish(() => { }));
        }

        var first = flight.RunAsync(false, true, Pass, () => { }, () => { });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = flight.RunAsync(false, false, Pass, () => { }, () => { });
        resume.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([true, false], reads);
    }

    [Fact]
    public async Task PublicationSubscriberCanRequestSubsequentPass()
    {
        var flight = new RepositoryRefreshFlight();
        var reads = 0;
        Task? nested = null;
        Task Pass(bool _, Func<Action, bool> publish)
        {
            reads++;
            Assert.True(publish(() =>
            {
                if (nested is null)
                    nested = flight.RunAsync(true, Pass, () => { }, () => { });
            }));
            return Task.CompletedTask;
        }

        var first = flight.RunAsync(false, Pass, () => { }, () => { });
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(first, nested);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task FailedStartingSubscriberSettlesAwaitersAndAllowsAnotherFlight()
    {
        var flight = new RepositoryRefreshFlight();
        var ends = 0;
        var calls = 0;
        var rejected = flight.RunAsync(false, (_, _) =>
        {
            ++calls;
            return Task.CompletedTask;
        }, () => throw new InvalidOperationException("starting subscriber"),
           () => ++ends);

        await Assert.ThrowsAsync<InvalidOperationException>(() => rejected);
        await flight.RunAsync(false, (_, _) =>
        {
            ++calls;
            return Task.CompletedTask;
        }, () => { }, () => ++ends).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, calls);
        Assert.Equal(2, ends);
    }

    [Fact]
    public async Task CanceledOneCallerWaitDoesNotCancelSharedRefresh()
    {
        var flight = new RepositoryRefreshFlight();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        async Task Pass(bool _, Func<Action, bool> publish)
        {
            ++reads;
            if (reads == 1)
            {
                started.TrySetResult();
                await resume.Task;
            }
            Assert.True(publish(() => { }));
        }

        var first = flight.RunAsync(false, Pass, () => { }, () => { });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        var second = flight.RunAsync(true, Pass, () => { }, () => { });
        var canceledWait = second.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait);
        resume.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, reads);
    }
}
