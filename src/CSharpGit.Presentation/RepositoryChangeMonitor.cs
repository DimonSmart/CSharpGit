using CSharpGit.Domain;

namespace CSharpGit.Presentation;

internal enum RepositoryInvalidationSource
{
    WorkingTree,
    GitMetadata
}

internal sealed class RepositoryInvalidatedEventArgs(
    long generation,
    RepositoryInvalidationSource source,
    string? path) : EventArgs
{
    public long Generation { get; } = generation;
    public RepositoryInvalidationSource Source { get; } = source;
    public string? Path { get; } = path;
}

internal sealed class RepositoryChangeMonitor : IDisposable
{
    internal static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _debounceTimer;
    private Repository? _repository;
    private RepositoryInvalidatedEventArgs? _pendingInvalidation;
    private long _watcherEpoch;
    private long _generation;
    private bool _suspended;
    private bool _disposed;

    public event EventHandler<RepositoryInvalidatedEventArgs>? RepositoryChanged;

    public long Generation
    {
        get
        {
            lock (_gate) return _generation;
        }
    }

    public void Start(Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        lock (_gate)
        {
            ThrowIfDisposed();
            _repository = repository;
            _suspended = false;
            RestartWatchersNoLock();
        }
    }

    public void Suspend()
    {
        lock (_gate)
        {
            if (_disposed || _repository is null || _suspended) return;

            _suspended = true;
            _watcherEpoch++;
            _pendingInvalidation = null;
            _debounceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            DisposeWatchersNoLock();
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_disposed || _repository is null || !_suspended) return;

            _suspended = false;
            RestartWatchersNoLock();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _repository = null;
            _suspended = false;
            _watcherEpoch++;
            _pendingInvalidation = null;
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
            _watcherEpoch++;
            _pendingInvalidation = null;
            DisposeWatchersNoLock();
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }

    private void RestartWatchersNoLock()
    {
        var repository = _repository;
        _watcherEpoch++;
        var epoch = _watcherEpoch;
        _pendingInvalidation = null;
        _debounceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        DisposeWatchersNoLock();

        if (repository is null) return;

        if (Directory.Exists(repository.WorkingDirectory))
            _watchers.Add(CreateWatcher(
                repository.WorkingDirectory,
                epoch,
                RepositoryInvalidationSource.WorkingTree));

        foreach (var metadataRoot in GetMetadataRoots(repository))
            _watchers.Add(CreateWatcher(
                metadataRoot,
                epoch,
                RepositoryInvalidationSource.GitMetadata));
    }

    private FileSystemWatcher CreateWatcher(
        string path,
        long epoch,
        RepositoryInvalidationSource source)
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

        watcher.Changed += (_, args) => QueueChange(epoch, source, path, args.FullPath);
        watcher.Created += (_, args) => QueueChange(epoch, source, path, args.FullPath);
        watcher.Deleted += (_, args) => QueueChange(epoch, source, path, args.FullPath);
        watcher.Renamed += (_, args) => QueueChange(epoch, source, path, args.FullPath);
        watcher.Error += (_, _) => QueueChange(epoch, source, path, fullPath: null);
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void QueueChange(
        long epoch,
        RepositoryInvalidationSource source,
        string watcherRoot,
        string? fullPath)
    {
        lock (_gate)
        {
            if (_disposed || _suspended || _repository is null || epoch != _watcherEpoch) return;
            if (source == RepositoryInvalidationSource.WorkingTree &&
                fullPath is not null &&
                IsMetadataPath(fullPath, _repository))
                return;
            if (source == RepositoryInvalidationSource.GitMetadata && IsLockNoise(fullPath))
                return;

            var generation = ++_generation;
            _pendingInvalidation = new RepositoryInvalidatedEventArgs(
                generation,
                source,
                GetRelativePath(watcherRoot, fullPath));
            _debounceTimer ??= new Timer(PublishRepositoryChanged);
            _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void PublishRepositoryChanged(object? state)
    {
        EventHandler<RepositoryInvalidatedEventArgs>? handler;
        RepositoryInvalidatedEventArgs? invalidation;
        lock (_gate)
        {
            if (_disposed || _suspended || _pendingInvalidation is null) return;
            invalidation = _pendingInvalidation;
            _pendingInvalidation = null;
            handler = RepositoryChanged;
        }

        handler?.Invoke(this, invalidation);
    }

    private void DisposeWatchersNoLock()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }

    private static IReadOnlyList<string> GetMetadataRoots(Repository repository)
    {
        var roots = new List<string>();
        foreach (var candidate in new[] { repository.GitDirectory, repository.GitCommonDirectory })
        {
            if (!Directory.Exists(candidate)) continue;

            var normalized = NormalizePath(candidate);
            if (roots.Any(root => IsPathWithin(normalized, root))) continue;

            roots.RemoveAll(root => IsPathWithin(root, normalized));
            roots.Add(normalized);
        }

        return roots;
    }

    private static bool IsMetadataPath(string path, Repository repository) =>
        IsPathWithin(path, repository.GitDirectory) ||
        IsPathWithin(path, repository.GitCommonDirectory);

    private static bool IsLockNoise(string? path) =>
        path is not null &&
        Path.GetFileName(path).EndsWith(".lock", StringComparison.OrdinalIgnoreCase);

    private static string? GetRelativePath(string root, string? path)
    {
        if (path is null) return null;
        try
        {
            return Path.GetRelativePath(root, path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Path.TrimEndingDirectorySeparator(path);
        }
    }

    private static bool IsPathWithin(string path, string directory)
    {
        try
        {
            var normalizedPath = NormalizePath(path);
            var normalizedDirectory = NormalizePath(directory);
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
