using CSharpGit.Domain;
using CSharpGit.Presentation;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryChangeMonitorTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task WorkingTreeChangePublishesPathAwareBatch()
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Root, "tracked.txt");
        await File.WriteAllTextAsync(path, "one");

        using var monitor = new RepositoryChangeMonitor();
        var signal = NewSignal();
        RepositoryInvalidationBatch? observed = null;
        monitor.RepositoryChanged += (_, args) =>
        {
            observed = args.Batch;
            signal.TrySetResult(true);
        };
        monitor.Start(repository.Repository);
        var generation = monitor.Generation;

        await File.AppendAllTextAsync(path, " two");
        await signal.Task.WaitAsync(EventTimeout);

        Assert.NotNull(observed);
        Assert.True(observed.HasWorkingTreeChanges);
        Assert.False(observed.HasRelevantMetadataChanges);
        Assert.False(observed.HasUnknownOrOverflow);
        Assert.Contains("tracked.txt", observed.WorkingTreePaths);
        Assert.True(observed.Generation > generation);
    }

    [Fact]
    public async Task MultipleWorkingTreeEventsAreDebouncedAndPathsAreAccumulated()
    {
        using var repository = TestRepository.Create();
        var first = Path.Combine(repository.Root, "first.txt");
        var second = Path.Combine(repository.Root, "second.txt");
        await File.WriteAllTextAsync(first, "one");
        await File.WriteAllTextAsync(second, "two");

        using var monitor = new RepositoryChangeMonitor();
        var received = new List<RepositoryInvalidationBatch>();
        var signal = NewSignal();
        monitor.RepositoryChanged += (_, args) =>
        {
            lock (received)
            {
                received.Add(args.Batch);
                var paths = received.SelectMany(batch => batch.WorkingTreePaths)
                    .ToHashSet(RepositoryChangeMonitor.PathComparer);
                if (paths.Contains("first.txt") && paths.Contains("second.txt"))
                    signal.TrySetResult(true);
            }
        };
        monitor.Start(repository.Repository);
        var initialGeneration = monitor.Generation;

        await File.AppendAllTextAsync(first, " changed");
        await File.AppendAllTextAsync(second, " changed");
        await signal.Task.WaitAsync(EventTimeout);

        lock (received)
        {
            Assert.NotEmpty(received);
            Assert.All(received, batch =>
            {
                Assert.True(batch.HasWorkingTreeChanges);
                Assert.False(batch.HasRelevantMetadataChanges);
                Assert.False(batch.HasUnknownOrOverflow);
                Assert.True(batch.Generation > initialGeneration);
            });
            var combined = received.SelectMany(batch => batch.WorkingTreePaths)
                .ToHashSet(RepositoryChangeMonitor.PathComparer);
            Assert.Contains("first.txt", combined);
            Assert.Contains("second.txt", combined);
        }
    }

    [Fact]
    public void DebounceAccumulatesDistinctPathsAndResetsQuietPeriod()
    {
        using var repository = TestRepository.Create();
        var clock = new ManualTimeProvider();
        using var monitor = new RepositoryChangeMonitor(clock);
        var published = new List<RepositoryInvalidationBatch>();
        monitor.RepositoryChanged += (_, args) => published.Add(args.Batch);
        monitor.Start(repository.Repository);
        var initialGeneration = monitor.Generation;

        monitor.EnqueueInvalidation(RepositoryInvalidationSource.WorkingTree, ["first.txt"]);
        clock.Advance(TimeSpan.FromMilliseconds(400));
        monitor.EnqueueInvalidation(
            RepositoryInvalidationSource.WorkingTree,
            ["second.txt", "first.txt"]);

        clock.Advance(TimeSpan.FromMilliseconds(100));
        clock.FireTimerEarly(); // A callback queued before the reset must not publish early.
        Assert.Empty(published);
        clock.Advance(TimeSpan.FromMilliseconds(399));
        Assert.Empty(published);
        clock.Advance(TimeSpan.FromMilliseconds(1));

        var firstBatch = Assert.Single(published);
        Assert.True(firstBatch.HasWorkingTreeChanges);
        Assert.False(firstBatch.HasRelevantMetadataChanges);
        Assert.False(firstBatch.HasUnknownOrOverflow);
        Assert.Equal(2, firstBatch.WorkingTreePaths.Count);
        Assert.Contains("first.txt", firstBatch.WorkingTreePaths);
        Assert.Contains("second.txt", firstBatch.WorkingTreePaths);
        Assert.Equal(initialGeneration + 2, firstBatch.Generation);

        var sinceStart = monitor.GetInvalidationsSince(initialGeneration);
        Assert.Equal(firstBatch.Generation, sinceStart.Generation);
        Assert.Equal(2, sinceStart.WorkingTreePaths.Count);

        monitor.EnqueueInvalidation(RepositoryInvalidationSource.WorkingTree, ["third.txt"]);
        clock.Advance(RepositoryChangeMonitor.DebounceDelay);

        Assert.Equal(2, published.Count);
        Assert.Equal(["third.txt"], published[1].WorkingTreePaths);
        Assert.Equal(firstBatch.Generation + 1, published[1].Generation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopOrDisposeCancelsPendingPublication(bool dispose)
    {
        using var repository = TestRepository.Create();
        var clock = new ManualTimeProvider();
        using var monitor = new RepositoryChangeMonitor(clock);
        var notifications = 0;
        monitor.RepositoryChanged += (_, _) => notifications++;
        monitor.Start(repository.Repository);
        monitor.EnqueueInvalidation(RepositoryInvalidationSource.WorkingTree, ["pending.txt"]);

        var generationBeforeStop = monitor.Generation;
        if (dispose) monitor.Dispose();
        else monitor.Stop();

        clock.Advance(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromSeconds(1));
        Assert.Equal(0, notifications);
        Assert.False(monitor.GetInvalidationsSince(generationBeforeStop).HasAny);
    }

    [Fact]
    public void RestartDiscardsOldRepositoryBatchEvenIfTimerCallbackWasQueued()
    {
        using var previous = TestRepository.Create();
        using var current = TestRepository.Create();
        var clock = new ManualTimeProvider();
        using var monitor = new RepositoryChangeMonitor(clock);
        var published = new List<RepositoryInvalidationBatch>();
        monitor.RepositoryChanged += (_, args) => published.Add(args.Batch);

        monitor.Start(previous.Repository);
        monitor.EnqueueInvalidation(RepositoryInvalidationSource.WorkingTree, ["old.txt"]);
        monitor.Start(current.Repository);
        monitor.EnqueueInvalidation(RepositoryInvalidationSource.WorkingTree, ["new.txt"]);
        clock.FireTimerEarly();
        Assert.Empty(published);

        clock.Advance(RepositoryChangeMonitor.DebounceDelay);
        var batch = Assert.Single(published);
        Assert.Equal(["new.txt"], batch.WorkingTreePaths);
        Assert.False(batch.HasUnknownOrOverflow);
    }

    [Fact]
    public async Task GenerationAdvancesBeforeDebouncedPublication()
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Root, "tracked.txt");
        await File.WriteAllTextAsync(path, "one");

        using var monitor = new RepositoryChangeMonitor();
        monitor.Start(repository.Repository);
        var generation = monitor.Generation;

        await File.AppendAllTextAsync(path, " external");
        await WaitUntilAsync(() => monitor.Generation > generation);

        var batch = monitor.GetInvalidationsSince(generation);
        Assert.True(batch.HasWorkingTreeChanges);
        Assert.Contains("tracked.txt", batch.WorkingTreePaths);
    }

    [Fact]
    public async Task WatcherRemainsActiveAfterPublishedInvalidation()
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Root, "tracked.txt");
        await File.WriteAllTextAsync(path, "one");

        using var monitor = new RepositoryChangeMonitor();
        var signal = NewSignal();
        monitor.RepositoryChanged += (_, _) => signal.TrySetResult(true);
        monitor.Start(repository.Repository);

        await File.AppendAllTextAsync(path, " first");
        await signal.Task.WaitAsync(EventTimeout);
        var generation = monitor.Generation;

        await File.AppendAllTextAsync(path, " second");
        await WaitUntilAsync(() => monitor.Generation > generation);
    }

    [Fact]
    public async Task GitMetadataChangePublishesWithoutWorkingTreeFlag()
    {
        using var repository = TestRepository.Create();
        var head = Path.Combine(repository.GitDirectory, "HEAD");
        await File.WriteAllTextAsync(head, "ref: refs/heads/main\n");

        using var monitor = new RepositoryChangeMonitor();
        var signal = NewSignal();
        RepositoryInvalidationBatch? observed = null;
        monitor.RepositoryChanged += (_, args) =>
        {
            observed = args.Batch;
            signal.TrySetResult(true);
        };
        monitor.Start(repository.Repository);

        await File.WriteAllTextAsync(head, "ref: refs/heads/feature\n");
        await signal.Task.WaitAsync(EventTimeout);

        Assert.NotNull(observed);
        Assert.True(observed.HasRelevantMetadataChanges);
        Assert.False(observed.HasWorkingTreeChanges);
        Assert.Contains("HEAD", observed.MetadataPaths);
    }

    [Fact]
    public async Task LockFileNoiseDoesNotAdvanceGeneration()
    {
        using var repository = TestRepository.Create();
        using var monitor = new RepositoryChangeMonitor();
        monitor.Start(repository.Repository);
        var generation = monitor.Generation;

        var lockPath = Path.Combine(repository.GitDirectory, "index.lock");
        await File.WriteAllTextAsync(lockPath, "temporary");
        File.Delete(lockPath);
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(300));

        Assert.Equal(generation, monitor.Generation);
    }

    [Fact]
    public async Task ObjectDatabaseChurnDoesNotAdvanceGeneration()
    {
        using var repository = TestRepository.Create();
        using var monitor = new RepositoryChangeMonitor();
        monitor.Start(repository.Repository);
        var generation = monitor.Generation;

        var directory = Path.Combine(repository.GitDirectory, "objects", "aa");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "object"), "temporary");
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(300));

        Assert.Equal(generation, monitor.Generation);
    }

    [Fact]
    public async Task LinkedWorktreeCommonRefsAreObserved()
    {
        using var repository = TestRepository.CreateLinkedWorktree();
        var refsDirectory = Path.Combine(repository.GitCommonDirectory, "refs", "heads");
        Directory.CreateDirectory(refsDirectory);

        using var monitor = new RepositoryChangeMonitor();
        var signal = NewSignal();
        RepositoryInvalidationBatch? observed = null;
        monitor.RepositoryChanged += (_, args) =>
        {
            observed = args.Batch;
            signal.TrySetResult(true);
        };
        monitor.Start(repository.Repository);

        await File.WriteAllTextAsync(Path.Combine(refsDirectory, "main"), "abc\n");
        await signal.Task.WaitAsync(EventTimeout);

        Assert.NotNull(observed);
        Assert.True(observed.HasRelevantMetadataChanges);
        Assert.Contains("refs/heads/main", observed.MetadataPaths);
    }

    [Fact]
    public async Task LinkedWorktreeCurrentPrivateMetadataIsObserved()
    {
        using var repository = TestRepository.CreateLinkedWorktree();
        using var monitor = new RepositoryChangeMonitor();
        var signal = NewSignal();
        monitor.RepositoryChanged += (_, _) => signal.TrySetResult(true);
        monitor.Start(repository.Repository);

        await File.WriteAllTextAsync(
            Path.Combine(repository.GitDirectory, "index"),
            "current");
        await signal.Task.WaitAsync(EventTimeout);
    }

    [Fact]
    public async Task LinkedWorktreeSiblingPrivateMetadataIsIgnored()
    {
        using var repository = TestRepository.CreateLinkedWorktree();
        var sibling = Path.Combine(
            repository.GitCommonDirectory,
            "worktrees",
            "other");
        Directory.CreateDirectory(sibling);

        using var monitor = new RepositoryChangeMonitor();
        monitor.Start(repository.Repository);
        var generation = monitor.Generation;

        await File.WriteAllTextAsync(Path.Combine(sibling, "HEAD"), "ref: refs/heads/other\n");
        await File.WriteAllTextAsync(Path.Combine(sibling, "index"), "sibling");
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(300));

        Assert.Equal(generation, monitor.Generation);
    }

    [Fact]
    public async Task StopPreventsFurtherGenerationChanges()
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Root, "tracked.txt");
        await File.WriteAllTextAsync(path, "one");

        using var monitor = new RepositoryChangeMonitor();
        monitor.Start(repository.Repository);
        monitor.Stop();
        var generation = monitor.Generation;

        await File.AppendAllTextAsync(path, " after stop");
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(300));

        Assert.Equal(generation, monitor.Generation);
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(EventTimeout);
        while (!predicate())
            await Task.Delay(25, timeout.Token);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;
        private readonly List<ManualTimer> _timers = [];

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
            var target = checked(_ticks + duration.Ticks);

            while (true)
            {
                var next = _timers
                    .Where(timer => timer.DueTicks is long due && due <= target)
                    .OrderBy(timer => timer.DueTicks)
                    .FirstOrDefault();
                if (next is null) break;

                _ticks = next.DueTicks!.Value;
                next.Fire();
            }

            _ticks = target;
        }

        public void FireTimerEarly()
        {
            var next = _timers.FirstOrDefault(timer => timer.DueTicks.HasValue);
            Assert.NotNull(next);
            next.Fire();
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            public long? DueTicks { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed) return false;
                if (period != Timeout.InfiniteTimeSpan)
                    throw new NotSupportedException("Only one-shot timers are supported.");
                DueTicks = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : checked(owner._ticks + dueTime.Ticks);
                return true;
            }

            public void Fire()
            {
                DueTicks = null;
                callback(state);
            }

            public void Dispose()
            {
                _disposed = true;
                DueTicks = null;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class TestRepository : IDisposable
    {
        private TestRepository(
            string root,
            string gitDirectory,
            string gitCommonDirectory,
            bool isWorktree)
        {
            Root = root;
            GitDirectory = gitDirectory;
            GitCommonDirectory = gitCommonDirectory;
            Directory.CreateDirectory(GitDirectory);
            Directory.CreateDirectory(GitCommonDirectory);
            Repository = new Repository(root, root, GitDirectory, isWorktree)
            {
                GitCommonDirectory = GitCommonDirectory
            };
        }

        public string Root { get; }
        public string GitDirectory { get; }
        public string GitCommonDirectory { get; }
        public Repository Repository { get; }

        public static TestRepository Create()
        {
            var root = CreateRoot();
            var gitDirectory = Path.Combine(root, ".git");
            return new TestRepository(
                root,
                gitDirectory,
                gitDirectory,
                isWorktree: false);
        }

        public static TestRepository CreateLinkedWorktree()
        {
            var root = CreateRoot();
            var common = Path.Combine(
                Path.GetTempPath(),
                "CSharpGit.RepositoryChangeMonitorTests.common",
                Guid.NewGuid().ToString("N"));
            var gitDirectory = Path.Combine(common, "worktrees", "linked");
            return new TestRepository(
                root,
                gitDirectory,
                common,
                isWorktree: true);
        }

        private static string CreateRoot()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "CSharpGit.RepositoryChangeMonitorTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        public void Dispose()
        {
            TryDelete(Root);
            if (!IsWithin(GitCommonDirectory, Root))
                TryDelete(GitCommonDirectory);
        }

        private static bool IsWithin(string path, string root) =>
            Path.GetFullPath(path).StartsWith(
                Path.GetFullPath(root) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);

        private static void TryDelete(string path)
        {
            try { Directory.Delete(path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
