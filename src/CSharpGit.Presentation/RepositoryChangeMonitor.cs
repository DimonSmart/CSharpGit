using CSharpGit.Domain;

namespace CSharpGit.Presentation;

internal enum RepositoryInvalidationSource
{
    WorkingTree,
    GitMetadata
}

internal sealed class RepositoryInvalidationBatch
{
    private const int MaxTrackedPaths = 128;

    internal RepositoryInvalidationBatch(
        long generation,
        bool hasWorkingTreeChanges,
        bool hasRelevantMetadataChanges,
        bool hasUnknownOrOverflow,
        IEnumerable<string>? workingTreePaths = null,
        IEnumerable<string>? metadataPaths = null)
    {
        Generation = generation;
        HasWorkingTreeChanges = hasWorkingTreeChanges;
        HasRelevantMetadataChanges = hasRelevantMetadataChanges;

        var working = DistinctPaths(workingTreePaths);
        var metadata = DistinctPaths(metadataPaths);
        HasUnknownOrOverflow = hasUnknownOrOverflow
                               || working.Overflow
                               || metadata.Overflow;
        WorkingTreePaths = working.Paths;
        MetadataPaths = metadata.Paths;
    }

    public long Generation { get; }
    public bool HasWorkingTreeChanges { get; }
    public bool HasRelevantMetadataChanges { get; }
    public bool HasUnknownOrOverflow { get; }
    public IReadOnlyList<string> WorkingTreePaths { get; }
    public IReadOnlyList<string> MetadataPaths { get; }
    public bool HasAny =>
        HasWorkingTreeChanges || HasRelevantMetadataChanges || HasUnknownOrOverflow;

    public static RepositoryInvalidationBatch Empty(long generation) =>
        new(generation, false, false, false);

    public static RepositoryInvalidationBatch Merge(
        RepositoryInvalidationBatch? left,
        RepositoryInvalidationBatch right)
    {
        ArgumentNullException.ThrowIfNull(right);
        if (left is null) return right;

        return new RepositoryInvalidationBatch(
            Math.Max(left.Generation, right.Generation),
            left.HasWorkingTreeChanges || right.HasWorkingTreeChanges,
            left.HasRelevantMetadataChanges || right.HasRelevantMetadataChanges,
            left.HasUnknownOrOverflow || right.HasUnknownOrOverflow,
            left.WorkingTreePaths.Concat(right.WorkingTreePaths),
            left.MetadataPaths.Concat(right.MetadataPaths));
    }

    public RepositoryInvalidationBatch WithUnknownOrOverflow() =>
        HasUnknownOrOverflow
            ? this
            : new RepositoryInvalidationBatch(
                Generation,
                HasWorkingTreeChanges,
                HasRelevantMetadataChanges,
                true,
                WorkingTreePaths,
                MetadataPaths);

    private static (IReadOnlyList<string> Paths, bool Overflow) DistinctPaths(
        IEnumerable<string>? paths)
    {
        if (paths is null) return ([], false);

        var result = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(RepositoryChangeMonitor.PathComparer)
            .Take(MaxTrackedPaths + 1)
            .ToArray();
        return result.Length > MaxTrackedPaths
            ? (result[..MaxTrackedPaths], true)
            : (result, false);
    }
}

internal sealed class RepositoryInvalidatedEventArgs(
    RepositoryInvalidationBatch batch) : EventArgs
{
    public RepositoryInvalidationBatch Batch { get; } =
        batch ?? throw new ArgumentNullException(nameof(batch));
}

internal sealed class RepositoryChangeMonitor : IDisposable
{
    internal static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);
    private const int MaxRecentInvalidations = 512;

    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Queue<InvalidationRecord> _recentInvalidations = [];
    private Timer? _debounceTimer;
    private Repository? _repository;
    private RepositoryInvalidationBatch? _pendingInvalidation;
    private long _watcherEpoch;
    private long _generation;
    private long _droppedThroughGeneration;
    private bool _disposed;

    public event EventHandler<RepositoryInvalidatedEventArgs>? RepositoryChanged;

    internal static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

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
            ResetInvalidationsNoLock();
            RestartWatchersNoLock();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _repository = null;
            _watcherEpoch++;
            ResetInvalidationsNoLock();
            DisposeWatchersNoLock();
        }
    }

    public RepositoryInvalidationBatch GetInvalidationsSince(long generation)
    {
        lock (_gate)
        {
            var result = RepositoryInvalidationBatch.Empty(_generation);
            if (_droppedThroughGeneration > generation)
                result = result.WithUnknownOrOverflow();

            foreach (var invalidation in _recentInvalidations)
            {
                if (invalidation.Generation <= generation) continue;
                result = RepositoryInvalidationBatch.Merge(
                    result,
                    invalidation.ToBatch());
            }

            return result;
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
            ResetInvalidationsNoLock();
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
                RepositoryInvalidationSource.WorkingTree,
                commonOnly: false));

        if (Directory.Exists(repository.GitDirectory))
            _watchers.Add(CreateWatcher(
                repository.GitDirectory,
                epoch,
                RepositoryInvalidationSource.GitMetadata,
                commonOnly: false));

        if (!PathsEqual(repository.GitDirectory, repository.GitCommonDirectory)
            && Directory.Exists(repository.GitCommonDirectory))
        {
            _watchers.Add(CreateWatcher(
                repository.GitCommonDirectory,
                epoch,
                RepositoryInvalidationSource.GitMetadata,
                commonOnly: true));
        }
    }

    private FileSystemWatcher CreateWatcher(
        string path,
        long epoch,
        RepositoryInvalidationSource source,
        bool commonOnly)
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

        watcher.Changed += (_, args) =>
            QueueChange(epoch, source, path, commonOnly, false, [args.FullPath]);
        watcher.Created += (_, args) =>
            QueueChange(epoch, source, path, commonOnly, false, [args.FullPath]);
        watcher.Deleted += (_, args) =>
            QueueChange(epoch, source, path, commonOnly, false, [args.FullPath]);
        watcher.Renamed += (_, args) =>
            QueueChange(
                epoch,
                source,
                path,
                commonOnly,
                false,
                [args.OldFullPath, args.FullPath]);
        watcher.Error += (_, _) =>
            QueueChange(epoch, source, path, commonOnly, true, []);
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void QueueChange(
        long epoch,
        RepositoryInvalidationSource source,
        string watcherRoot,
        bool commonOnly,
        bool unknownOrOverflow,
        IReadOnlyList<string> fullPaths)
    {
        lock (_gate)
        {
            var repository = _repository;
            if (_disposed || repository is null || epoch != _watcherEpoch) return;

            var workingPaths = new List<string>();
            var metadataPaths = new List<string>();

            if (!unknownOrOverflow)
            {
                foreach (var fullPath in fullPaths)
                {
                    if (source == RepositoryInvalidationSource.WorkingTree)
                    {
                        if (IsMetadataPath(fullPath, repository)) continue;
                        if (GetRelativePath(watcherRoot, fullPath) is { } relative)
                            workingPaths.Add(NormalizeRelativePath(relative));
                        else
                            unknownOrOverflow = true;
                        continue;
                    }

                    if (GetRelativePath(watcherRoot, fullPath) is not { } metadataRelative)
                    {
                        unknownOrOverflow = true;
                        continue;
                    }

                    metadataRelative = NormalizeRelativePath(metadataRelative);
                    if (commonOnly && IsWorktreePrivateMetadata(metadataRelative)) continue;
                    if (IsMetadataNoise(metadataRelative)) continue;
                    metadataPaths.Add(metadataRelative);
                }
            }

            if (!unknownOrOverflow && workingPaths.Count == 0 && metadataPaths.Count == 0)
                return;

            var generation = ++_generation;
            var invalidation = new InvalidationRecord(
                generation,
                source,
                unknownOrOverflow,
                source == RepositoryInvalidationSource.WorkingTree
                    ? workingPaths.ToArray()
                    : metadataPaths.ToArray());

            RememberInvalidationNoLock(invalidation);
            _pendingInvalidation = RepositoryInvalidationBatch.Merge(
                _pendingInvalidation,
                invalidation.ToBatch());
            _debounceTimer ??= new Timer(PublishRepositoryChanged);
            _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void PublishRepositoryChanged(object? state)
    {
        EventHandler<RepositoryInvalidatedEventArgs>? handler;
        RepositoryInvalidationBatch? invalidation;
        lock (_gate)
        {
            if (_disposed || _pendingInvalidation is null) return;
            invalidation = _pendingInvalidation;
            _pendingInvalidation = null;
            handler = RepositoryChanged;
        }

        handler?.Invoke(this, new RepositoryInvalidatedEventArgs(invalidation));
    }

    private void RememberInvalidationNoLock(InvalidationRecord invalidation)
    {
        _recentInvalidations.Enqueue(invalidation);
        while (_recentInvalidations.Count > MaxRecentInvalidations)
            _droppedThroughGeneration = _recentInvalidations.Dequeue().Generation;
    }

    private void ResetInvalidationsNoLock()
    {
        _pendingInvalidation = null;
        _recentInvalidations.Clear();
        _droppedThroughGeneration = _generation;
        _debounceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void DisposeWatchersNoLock()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }

    private static bool IsMetadataPath(string path, Repository repository) =>
        IsPathWithin(path, repository.GitDirectory)
        || IsPathWithin(path, repository.GitCommonDirectory);

    private static bool IsWorktreePrivateMetadata(string relativePath) =>
        relativePath.Equals("worktrees", StringComparison.OrdinalIgnoreCase)
        || relativePath.StartsWith("worktrees/", StringComparison.OrdinalIgnoreCase);

    private static bool IsMetadataNoise(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        var name = slash >= 0 ? relativePath[(slash + 1)..] : relativePath;
        if (name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) return true;
        if (relativePath.Equals("COMMIT_EDITMSG", StringComparison.OrdinalIgnoreCase)) return true;
        return relativePath.Equals("objects", StringComparison.OrdinalIgnoreCase)
               || relativePath.StartsWith("objects/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetRelativePath(string root, string? path)
    {
        if (path is null) return null;
        try
        {
            return Path.GetRelativePath(root, path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/');

    private static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), PathComparison);

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
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
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
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

    private sealed record InvalidationRecord(
        long Generation,
        RepositoryInvalidationSource Source,
        bool UnknownOrOverflow,
        IReadOnlyList<string> Paths)
    {
        public RepositoryInvalidationBatch ToBatch() =>
            Source == RepositoryInvalidationSource.WorkingTree
                ? new RepositoryInvalidationBatch(
                    Generation,
                    hasWorkingTreeChanges: Paths.Count > 0 || UnknownOrOverflow,
                    hasRelevantMetadataChanges: false,
                    hasUnknownOrOverflow: UnknownOrOverflow,
                    workingTreePaths: Paths)
                : new RepositoryInvalidationBatch(
                    Generation,
                    hasWorkingTreeChanges: false,
                    hasRelevantMetadataChanges: Paths.Count > 0 || UnknownOrOverflow,
                    hasUnknownOrOverflow: UnknownOrOverflow,
                    metadataPaths: Paths);
    }
}
