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
        var notifications = 0;
        var signal = NewSignal();
        RepositoryInvalidationBatch? observed = null;
        monitor.RepositoryChanged += (_, args) =>
        {
            Interlocked.Increment(ref notifications);
            observed = args.Batch;
            signal.TrySetResult(true);
        };
        monitor.Start(repository.Repository);

        await File.AppendAllTextAsync(first, " changed");
        await File.AppendAllTextAsync(second, " changed");
        await signal.Task.WaitAsync(EventTimeout);
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(300));

        Assert.Equal(1, Volatile.Read(ref notifications));
        Assert.NotNull(observed);
        Assert.Contains("first.txt", observed.WorkingTreePaths);
        Assert.Contains("second.txt", observed.WorkingTreePaths);
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
