using CSharpGit.Domain;
using CSharpGit.Presentation;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryChangeMonitorTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task WorkingTreeChangePublishesInvalidation()
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Root, "tracked.txt");
        await File.WriteAllTextAsync(path, "one");

        using var monitor = new RepositoryChangeMonitor();
        var signal = NewSignal();
        RepositoryInvalidatedEventArgs? observed = null;
        monitor.RepositoryChanged += (_, args) =>
        {
            observed = args;
            signal.TrySetResult(true);
        };
        monitor.Start(repository.Repository);

        await File.AppendAllTextAsync(path, " two");
        await signal.Task.WaitAsync(EventTimeout);

        Assert.NotNull(observed);
        Assert.Equal(RepositoryInvalidationSource.WorkingTree, observed.Source);
        Assert.Equal("tracked.txt", observed.Path);
    }

    [Fact]
    public async Task MultipleChangesAreDebounced()
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Root, "tracked.txt");
        await File.WriteAllTextAsync(path, "one");

        using var monitor = new RepositoryChangeMonitor();
        var notifications = 0;
        var signal = NewSignal();
        monitor.RepositoryChanged += (_, _) =>
        {
            Interlocked.Increment(ref notifications);
            signal.TrySetResult(true);
        };
        monitor.Start(repository.Repository);

        await File.AppendAllTextAsync(path, " two");
        await File.AppendAllTextAsync(path, " three");
        await signal.Task.WaitAsync(EventTimeout);
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(300));

        Assert.Equal(1, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task GitMetadataChangesAreReported()
    {
        using var repository = TestRepository.Create();
        var head = Path.Combine(repository.GitDirectory, "HEAD");
        await File.WriteAllTextAsync(head, "ref: refs/heads/main\n");

        using var monitor = new RepositoryChangeMonitor();
        var signal = NewSignal();
        RepositoryInvalidatedEventArgs? observed = null;
        monitor.RepositoryChanged += (_, args) =>
        {
            observed = args;
            signal.TrySetResult(true);
        };
        monitor.Start(repository.Repository);

        await File.WriteAllTextAsync(head, "ref: refs/heads/feature\n");
        await signal.Task.WaitAsync(EventTimeout);

        Assert.NotNull(observed);
        Assert.Equal(RepositoryInvalidationSource.GitMetadata, observed.Source);
        Assert.Equal("HEAD", observed.Path);
    }

    [Fact]
    public async Task SuspendStopsWorkingTreeNotifications()
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Root, "tracked.txt");
        await File.WriteAllTextAsync(path, "one");

        using var monitor = new RepositoryChangeMonitor();
        var notifications = 0;
        monitor.RepositoryChanged += (_, _) => Interlocked.Increment(ref notifications);
        monitor.Start(repository.Repository);
        var generation = monitor.Generation;

        monitor.Suspend();
        await File.AppendAllTextAsync(path, " two");
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(300));

        Assert.Equal(generation, monitor.Generation);
        Assert.Equal(0, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task SuspendStopsMetadataNotifications()
    {
        using var repository = TestRepository.Create();
        var head = Path.Combine(repository.GitDirectory, "HEAD");
        await File.WriteAllTextAsync(head, "ref: refs/heads/main\n");

        using var monitor = new RepositoryChangeMonitor();
        var notifications = 0;
        monitor.RepositoryChanged += (_, _) => Interlocked.Increment(ref notifications);
        monitor.Start(repository.Repository);
        var generation = monitor.Generation;

        monitor.Suspend();
        await File.WriteAllTextAsync(head, "ref: refs/heads/feature\n");
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(300));

        Assert.Equal(generation, monitor.Generation);
        Assert.Equal(0, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task SuspendCancelsPendingDebouncedInvalidation()
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Root, "tracked.txt");
        await File.WriteAllTextAsync(path, "one");

        using var monitor = new RepositoryChangeMonitor();
        var notifications = 0;
        monitor.RepositoryChanged += (_, _) => Interlocked.Increment(ref notifications);
        monitor.Start(repository.Repository);
        var generation = monitor.Generation;

        await File.AppendAllTextAsync(path, " external");
        await WaitUntilAsync(() => monitor.Generation > generation);

        monitor.Suspend();
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(300));

        Assert.Equal(0, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task ResumeDetectsNewChangesAgain()
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Root, "tracked.txt");
        await File.WriteAllTextAsync(path, "one");

        using var monitor = new RepositoryChangeMonitor();
        var signal = NewSignal();
        monitor.RepositoryChanged += (_, _) => signal.TrySetResult(true);
        monitor.Start(repository.Repository);

        monitor.Suspend();
        await File.AppendAllTextAsync(path, " during suspension");
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(300));

        monitor.Resume();
        await File.AppendAllTextAsync(path, " after resume");
        await signal.Task.WaitAsync(EventTimeout);
    }

    [Fact]
    public async Task StartRearmsSuspendedMonitor()
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Root, "tracked.txt");
        await File.WriteAllTextAsync(path, "one");

        using var monitor = new RepositoryChangeMonitor();
        var signal = NewSignal();
        monitor.RepositoryChanged += (_, _) => signal.TrySetResult(true);
        monitor.Start(repository.Repository);
        monitor.Suspend();

        monitor.Start(repository.Repository);
        await File.AppendAllTextAsync(path, " after restart");
        await signal.Task.WaitAsync(EventTimeout);
    }

    [Fact]
    public void RepeatedSuspendIsSafe()
    {
        using var repository = TestRepository.Create();
        using var monitor = new RepositoryChangeMonitor();
        monitor.Start(repository.Repository);

        monitor.Suspend();
        monitor.Suspend();
    }

    [Fact]
    public async Task RepeatedResumeIsSafe()
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Root, "tracked.txt");
        await File.WriteAllTextAsync(path, "one");

        using var monitor = new RepositoryChangeMonitor();
        var signal = NewSignal();
        monitor.RepositoryChanged += (_, _) => signal.TrySetResult(true);
        monitor.Start(repository.Repository);

        monitor.Resume();
        monitor.Resume();
        await File.AppendAllTextAsync(path, " two");
        await signal.Task.WaitAsync(EventTimeout);
    }

    [Fact]
    public async Task StopWhileSuspendedIsSafe()
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Root, "tracked.txt");
        await File.WriteAllTextAsync(path, "one");

        using var monitor = new RepositoryChangeMonitor();
        var notifications = 0;
        monitor.RepositoryChanged += (_, _) => Interlocked.Increment(ref notifications);
        monitor.Start(repository.Repository);
        monitor.Suspend();

        monitor.Stop();
        monitor.Stop();
        await File.AppendAllTextAsync(path, " two");
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(300));

        Assert.Equal(0, Volatile.Read(ref notifications));
    }

    [Fact]
    public void DisposeWhileSuspendedIsSafe()
    {
        using var repository = TestRepository.Create();
        var monitor = new RepositoryChangeMonitor();
        monitor.Start(repository.Repository);
        monitor.Suspend();

        monitor.Dispose();
        monitor.Dispose();
    }

    [Fact]
    public async Task LockFileNoiseDoesNotPublishInvalidation()
    {
        using var repository = TestRepository.Create();

        using var monitor = new RepositoryChangeMonitor();
        var notifications = 0;
        monitor.RepositoryChanged += (_, _) => Interlocked.Increment(ref notifications);
        monitor.Start(repository.Repository);

        var lockPath = Path.Combine(repository.GitDirectory, "index.lock");
        await File.WriteAllTextAsync(lockPath, "temporary");
        File.Delete(lockPath);
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(300));

        Assert.Equal(0, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task LinkedWorktreeCommonGitDirectoryIsObserved()
    {
        using var repository = TestRepository.CreateLinkedWorktree();
        var refsDirectory = Path.Combine(repository.GitCommonDirectory, "refs", "heads");
        Directory.CreateDirectory(refsDirectory);

        using var monitor = new RepositoryChangeMonitor();
        var signal = NewSignal();
        monitor.RepositoryChanged += (_, _) => signal.TrySetResult(true);
        monitor.Start(repository.Repository);

        await File.WriteAllTextAsync(Path.Combine(refsDirectory, "main"), "abc\n");
        await signal.Task.WaitAsync(EventTimeout);
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
        private TestRepository(string root, string gitDirectory, string gitCommonDirectory, bool isWorktree)
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
            return new TestRepository(root, gitDirectory, gitDirectory, isWorktree: false);
        }

        public static TestRepository CreateLinkedWorktree()
        {
            var root = CreateRoot();
            var common = Path.Combine(
                Path.GetTempPath(),
                "CSharpGit.RepositoryChangeMonitorTests.common",
                Guid.NewGuid().ToString("N"));
            var gitDirectory = Path.Combine(common, "worktrees", "linked");
            return new TestRepository(root, gitDirectory, common, isWorktree: true);
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
            if (!IsWithin(GitCommonDirectory, Root)) TryDelete(GitCommonDirectory);
        }

        private static bool IsWithin(string path, string root) =>
            Path.GetFullPath(path).StartsWith(
                Path.GetFullPath(root) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        private static void TryDelete(string path)
        {
            try { Directory.Delete(path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
