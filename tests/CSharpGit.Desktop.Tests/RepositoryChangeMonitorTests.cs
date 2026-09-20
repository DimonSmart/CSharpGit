using CSharpGit.Domain;
using CSharpGit.Presentation;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryChangeMonitorTests
{
    [Fact]
    public async Task WorkingTreeChangesAreDebouncedAndDetectedAgainAfterAcknowledge()
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
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(150));

        Assert.Equal(1, Volatile.Read(ref notifications));

        signal = NewSignal();
        monitor.Acknowledge();
        await File.AppendAllTextAsync(path, " four");
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, Volatile.Read(ref notifications));
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
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(observed);
        Assert.Equal(RepositoryInvalidationSource.GitMetadata, observed.Source);
        Assert.Equal("HEAD", observed.Path);
    }

    [Fact]
    public async Task AcknowledgeDoesNotDropQueuedInvalidation()
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Root, "tracked.txt");
        await File.WriteAllTextAsync(path, "one");

        using var monitor = new RepositoryChangeMonitor();
        var signal = NewSignal();
        monitor.RepositoryChanged += (_, _) => signal.TrySetResult(true);
        monitor.Start(repository.Repository);

        await File.AppendAllTextAsync(path, " external change");
        monitor.Acknowledge();

        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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
