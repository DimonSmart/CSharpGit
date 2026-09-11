using System.Diagnostics;
using CSharpGit.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel
{
    private const int ChangedFilesDebounceMilliseconds = 120;
    private const int ChangedFilesCacheEntries = 64;
    private const int DiffCacheEntries = 96;
    private const int DiffCacheCharacters = 4 * 1024 * 1024;

    private CancellationTokenSource? _changedFilesLoadCts;
    private CancellationTokenSource? _diffLoadCts;
    private long _changedFilesLoadGeneration;
    private IReadOnlyList<ChangedFile> _selectedChangedFiles = [];
    private bool _isChangedFilesLoading;
    private bool _isChangesViewActive;
    private readonly BoundedLruCache<ChangedFilesCacheKey, IReadOnlyList<ChangedFile>> _changedFilesCache =
        new(ChangedFilesCacheEntries, ChangedFilesCacheEntries, _ => 1);
    private readonly BoundedLruCache<DiffCacheKey, FileDiff> _diffCache =
        new(DiffCacheEntries, DiffCacheCharacters, EstimateDiffSize);

    public IReadOnlyList<ChangedFile> SelectedChangedFiles
    {
        get => _selectedChangedFiles;
        private set
        {
            if (ReferenceEquals(_selectedChangedFiles, value)) return;
            _selectedChangedFiles = value;
            Notify();
        }
    }

    public bool IsChangedFilesLoading
    {
        get => _isChangedFilesLoading;
        private set
        {
            if (_isChangedFilesLoading == value) return;
            _isChangedFilesLoading = value;
            Notify();
            Notify(nameof(ChangedFilesLoadingVisibility));
        }
    }

    public Visibility ChangedFilesLoadingVisibility => IsChangedFilesLoading ? Visibility.Visible : Visibility.Collapsed;
    public bool IsChangesViewActive => _isChangesViewActive;

    public void SetChangesViewActive(bool active)
    {
        if (_isChangesViewActive == active) return;
        _isChangesViewActive = active;
        Notify(nameof(IsChangesViewActive));

        if (!active)
        {
            InvalidateChangedFilesLoad();
            InvalidateDiffLoad();
            IsChangedFilesLoading = false;
            IsDiffLoading = false;
            return;
        }

        _ = EnsureChangedFilesLoadedAsync(debounce: false);
    }

    private void OnSelectedHistoryRowChanged()
    {
        InvalidateChangedFilesLoad();
        InvalidateDiffLoad();
        SelectedChangedFiles = [];
        SelectedFile = null;
        SelectedDiff = null;
        IsChangedFilesLoading = false;
        IsDiffLoading = false;

        if (_isChangesViewActive)
            _ = EnsureChangedFilesLoadedAsync(debounce: true);
    }

    private void OnSelectedFileChanged()
    {
        InvalidateDiffLoad();
        SelectedDiff = null;
        IsDiffLoading = false;
        if (_isChangesViewActive && SelectedFile is not null)
            _ = LoadSelectedDiffAsync();
    }

    private void ResetCommitChangesSession()
    {
        InvalidateChangedFilesLoad();
        InvalidateDiffLoad();
        _changedFilesCache.Clear();
        _diffCache.Clear();
        SelectedChangedFiles = [];
        SelectedFile = null;
        SelectedDiff = null;
        IsChangedFilesLoading = false;
        IsDiffLoading = false;
    }

    private async Task EnsureChangedFilesLoadedAsync(bool debounce)
    {
        var repository = Repository;
        var row = SelectedHistoryRow;
        if (!_isChangesViewActive || repository is null || row is null) return;

        var parentHash = row.Commit.Parents.FirstOrDefault();
        var cacheKey = new ChangedFilesCacheKey(repository.GitDirectory, row.Commit.Hash, parentHash);
        if (_changedFilesCache.TryGet(cacheKey, out var cached))
        {
            _logger.LogDebug("LoadChangedFiles commit={Commit} duration={Duration}ms cache={Cache}", row.Commit.Hash, 0, "hit");
            PublishChangedFiles(repository, row, cached);
            return;
        }

        var generation = Interlocked.Increment(ref _changedFilesLoadGeneration);
        var cancellation = new CancellationTokenSource();
        ReplaceCancellation(ref _changedFilesLoadCts, cancellation);
        IsChangedFilesLoading = true;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (debounce)
                await Task.Delay(ChangedFilesDebounceMilliseconds, cancellation.Token);

            var files = (await _historyService.ReadChangedFilesAsync(
                repository,
                row.Commit.Hash,
                parentHash,
                cancellation.Token)).ToArray();
            stopwatch.Stop();

            if (!IsCurrentChangedFilesRequest(generation, cancellation, repository, row)) return;
            _changedFilesCache.Set(cacheKey, files);
            _logger.LogDebug("LoadChangedFiles commit={Commit} duration={Duration}ms cache={Cache}", row.Commit.Hash, stopwatch.ElapsedMilliseconds, "miss");
            PublishChangedFiles(repository, row, files);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (IsCurrentChangedFilesRequest(generation, cancellation, repository, row))
            {
                SelectedChangedFiles = [];
                SelectedFile = null;
                SelectedDiff = null;
                ErrorMessage = $"Could not read changes: {exception.Message}";
                _logger.LogWarning(exception, "Changed-files loading failed for {Commit}", row.Commit.Hash);
            }
        }
        finally
        {
            stopwatch.Stop();
            if (IsCurrentChangedFilesRequest(generation, cancellation, repository, row))
                IsChangedFilesLoading = false;
        }
    }

    private void PublishChangedFiles(Repository repository, HistoryRow row, IReadOnlyList<ChangedFile> files)
    {
        if (!_isChangesViewActive || !ReferenceEquals(repository, Repository) || !ReferenceEquals(row, SelectedHistoryRow)) return;
        SelectedChangedFiles = files;
        var first = files.FirstOrDefault();
        if (!ReferenceEquals(SelectedFile, first)) SelectedFile = first;
        else if (first is not null) _ = LoadSelectedDiffAsync();
    }

    private async Task LoadSelectedDiffAsync()
    {
        var repository = Repository;
        var row = SelectedHistoryRow;
        var file = SelectedFile;
        if (!_isChangesViewActive || repository is null || row is null || file is null) return;

        var parentHash = row.Commit.Parents.FirstOrDefault();
        var cacheKey = new DiffCacheKey(repository.GitDirectory, row.Commit.Hash, parentHash, file.Path, file.OriginalPath);
        if (_diffCache.TryGet(cacheKey, out var cached))
        {
            _logger.LogDebug("LoadDiff commit={Commit} path={Path} duration={Duration}ms cache={Cache}", row.Commit.Hash, file.Path, 0, "hit");
            if (_isChangesViewActive && ReferenceEquals(repository, Repository) && ReferenceEquals(row, SelectedHistoryRow) && ReferenceEquals(file, SelectedFile))
                SelectedDiff = cached;
            return;
        }

        var generation = Interlocked.Increment(ref _diffLoadGeneration);
        var cancellation = new CancellationTokenSource();
        ReplaceCancellation(ref _diffLoadCts, cancellation);
        SelectedDiff = null;
        IsDiffLoading = true;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var diff = await _historyService.ReadDiffAsync(
                repository,
                row.Commit.Hash,
                parentHash,
                file,
                cancellation.Token);
            stopwatch.Stop();

            if (!IsCurrentDiffRequest(generation, cancellation, repository, row, file)) return;
            _diffCache.Set(cacheKey, diff);
            _logger.LogDebug("LoadDiff commit={Commit} path={Path} duration={Duration}ms cache={Cache}", row.Commit.Hash, file.Path, stopwatch.ElapsedMilliseconds, "miss");
            SelectedDiff = diff;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (IsCurrentDiffRequest(generation, cancellation, repository, row, file))
            {
                SelectedDiff = null;
                ErrorMessage = $"Could not read change: {exception.Message}";
                _logger.LogWarning(exception, "Diff loading failed for {Commit} {Path}", row.Commit.Hash, file.Path);
            }
        }
        finally
        {
            stopwatch.Stop();
            if (IsCurrentDiffRequest(generation, cancellation, repository, row, file))
                IsDiffLoading = false;
        }
    }

    private bool IsCurrentChangedFilesRequest(
        long generation,
        CancellationTokenSource cancellation,
        Repository repository,
        HistoryRow row) =>
        !cancellation.IsCancellationRequested &&
        generation == Volatile.Read(ref _changedFilesLoadGeneration) &&
        _isChangesViewActive &&
        ReferenceEquals(repository, Repository) &&
        ReferenceEquals(row, SelectedHistoryRow);

    private bool IsCurrentDiffRequest(
        long generation,
        CancellationTokenSource cancellation,
        Repository repository,
        HistoryRow row,
        ChangedFile file) =>
        !cancellation.IsCancellationRequested &&
        generation == Volatile.Read(ref _diffLoadGeneration) &&
        _isChangesViewActive &&
        ReferenceEquals(repository, Repository) &&
        ReferenceEquals(row, SelectedHistoryRow) &&
        ReferenceEquals(file, SelectedFile);

    private void InvalidateChangedFilesLoad()
    {
        Interlocked.Increment(ref _changedFilesLoadGeneration);
        CancelAndDispose(ref _changedFilesLoadCts);
    }

    private void InvalidateDiffLoad()
    {
        Interlocked.Increment(ref _diffLoadGeneration);
        CancelAndDispose(ref _diffLoadCts);
    }

    private static void ReplaceCancellation(ref CancellationTokenSource? field, CancellationTokenSource replacement)
    {
        var previous = Interlocked.Exchange(ref field, replacement);
        if (previous is null) return;
        previous.Cancel();
        previous.Dispose();
    }

    private static void CancelAndDispose(ref CancellationTokenSource? field)
    {
        var cancellation = Interlocked.Exchange(ref field, null);
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private static int EstimateDiffSize(FileDiff diff)
    {
        long size = diff.Path.Length;
        foreach (var line in diff.Lines) size += line.Text.Length + 1L;
        return (int)Math.Min(size, int.MaxValue);
    }

    private readonly record struct ChangedFilesCacheKey(string Repository, string Commit, string? Parent);
    private readonly record struct DiffCacheKey(string Repository, string Commit, string? Parent, string Path, string? OriginalPath);

    private sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _maxEntries;
        private readonly int _maxCost;
        private readonly Func<TValue, int> _cost;
        private readonly Dictionary<TKey, LinkedListNode<Entry>> _index = [];
        private readonly LinkedList<Entry> _lru = [];
        private int _currentCost;

        public BoundedLruCache(int maxEntries, int maxCost, Func<TValue, int> cost)
        {
            _maxEntries = maxEntries;
            _maxCost = maxCost;
            _cost = cost;
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (!_index.TryGetValue(key, out var node))
            {
                value = default!;
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        public void Set(TKey key, TValue value)
        {
            var cost = Math.Max(1, _cost(value));
            if (cost > _maxCost) return;
            if (_index.Remove(key, out var existing))
            {
                _lru.Remove(existing);
                _currentCost -= existing.Value.Cost;
            }

            var node = new LinkedListNode<Entry>(new Entry(key, value, cost));
            _lru.AddFirst(node);
            _index[key] = node;
            _currentCost += cost;

            while (_index.Count > _maxEntries || _currentCost > _maxCost)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _index.Remove(last.Value.Key);
                _currentCost -= last.Value.Cost;
            }
        }

        public void Clear()
        {
            _index.Clear();
            _lru.Clear();
            _currentCost = 0;
        }

        private sealed record Entry(TKey Key, TValue Value, int Cost);
    }
}
