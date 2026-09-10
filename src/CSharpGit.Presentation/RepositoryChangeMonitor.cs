using CSharpGit.Domain;

namespace CSharpGit.Presentation;

internal sealed class RepositoryChangeMonitor : IDisposable
{
    internal static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private FileSystemWatcher? _workingTreeWatcher;
    private FileSystemWatcher? _gitWatcher;
    private Timer? _debounceTimer;
    private Repository? _repository;
    private long _generation;
    private long _pendingGeneration;
    private bool _disposed;

    public event EventHandler? RepositoryChanged;

    public void Start(Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        lock (_gate)
        {
            ThrowIfDisposed();
            _repository = repository;
            RestartWatchersNoLock();
        }
    }

    public void Acknowledge()
    {
        lock (_gate)
        {
            if (_disposed || _repository is null) return;
            RestartWatchersNoLock();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _repository = null;
            _generation++;
            _pendingGeneration = 0;
            _debounceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            DisposeWatchersNoLock();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _repository = null;
            _generation++;
            _pendingGeneration = 0;
            DisposeWatchersNoLock();
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }

    private void RestartWatchersNoLock()
    {
        var repository = _repository;
        _generation++;
        var generation = _generation;
        _pendingGeneration = 0;
        _debounceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        DisposeWatchersNoLock();

        if (repository is null) return;

        var hasSeparateGitWatcher = !PathsEqual(repository.WorkingDirectory, repository.GitDirectory)
                                    && Directory.Exists(repository.GitDirectory);

        if (Directory.Exists(repository.WorkingDirectory))
            _workingTreeWatcher = CreateWatcher(repository.WorkingDirectory, generation, hasSeparateGitWatcher);

        if (hasSeparateGitWatcher)
            _gitWatcher = CreateWatcher(repository.GitDirectory, generation, ignoreGitDirectory: false);
    }

    private FileSystemWatcher CreateWatcher(string path, long generation, bool ignoreGitDirectory)
    {
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                           | NotifyFilters.DirectoryName
                           | NotifyFilters.LastWrite
                           | NotifyFilters.Size
                           | NotifyFilters.CreationTime
        };

        watcher.Changed += (_, args) => QueueChange(generation, args.FullPath, ignoreGitDirectory);
        watcher.Created += (_, args) => QueueChange(generation, args.FullPath, ignoreGitDirectory);
        watcher.Deleted += (_, args) => QueueChange(generation, args.FullPath, ignoreGitDirectory);
        watcher.Renamed += (_, args) => QueueChange(generation, args.FullPath, ignoreGitDirectory);
        watcher.Error += (_, _) => QueueChange(generation, fullPath: null, ignoreGitDirectory: false);
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void QueueChange(long generation, string? fullPath, bool ignoreGitDirectory)
    {
        lock (_gate)
        {
            if (_disposed || _repository is null || generation != _generation) return;
            if (ignoreGitDirectory && fullPath is not null && IsPathWithin(fullPath, _repository.GitDirectory)) return;

            _pendingGeneration = generation;
            _debounceTimer ??= new Timer(PublishRepositoryChanged);
            _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void PublishRepositoryChanged(object? state)
    {
        EventHandler? handler;
        lock (_gate)
        {
            if (_disposed || _pendingGeneration == 0 || _pendingGeneration != _generation) return;
            _pendingGeneration = 0;
            handler = RepositoryChanged;
        }

        handler?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeWatchersNoLock()
    {
        _workingTreeWatcher?.Dispose();
        _workingTreeWatcher = null;
        _gitWatcher?.Dispose();
        _gitWatcher = null;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                PathComparison);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, PathComparison);
        }
    }

    private static bool IsPathWithin(string path, string directory)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(path);
            var normalizedDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (string.Equals(normalizedPath, normalizedDirectory, PathComparison)) return true;

            var prefix = normalizedDirectory + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(prefix, PathComparison);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RepositoryChangeMonitor));
    }
}
