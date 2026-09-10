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
        monitor.RepositoryChanged += (_, _) => signal.TrySetResult(true);
        monitor.Start(repository.Repository);

        await File.WriteAllTextAsync(head, "ref: refs/heads/feature\n");
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AcknowledgeDropsQueuedEventsFromPreviousWatcherGeneration()
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

        await File.AppendAllTextAsync(path, " internal change");
        monitor.Acknowledge();
        await Task.Delay(RepositoryChangeMonitor.DebounceDelay + TimeSpan.FromMilliseconds(250));

        Assert.Equal(0, Volatile.Read(ref notifications));

        signal = NewSignal();
        var index = Path.Combine(repository.GitDirectory, "index");
        await File.WriteAllTextAsync(index, "external metadata change");
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, Volatile.Read(ref notifications));
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestRepository : IDisposable
    {
        private TestRepository(string root)
        {
            Root = root;
            GitDirectory = Path.Combine(root, ".git");
            Directory.CreateDirectory(GitDirectory);
            Repository = new Repository(root, root, GitDirectory, IsWorktree: false);
        }

        public string Root { get; }
        public string GitDirectory { get; }
        public Repository Repository { get; }

        public static TestRepository Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "CSharpGit.RepositoryChangeMonitorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TestRepository(root);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
