using System.Diagnostics;
using CSharpGit.Domain;
using Microsoft.Extensions.Logging;

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
    private string? _diffLoadErrorMessage;
    private bool _isDiffPreviewDeferred;
    private string? _diffPreviewDeferredMessage;
    private string _diffPreviewActionText = "Load diff";
    private ChangedFileSelectionKey? _selectedChangedFileRestoreKey;
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
        }
    }

    public bool IsChangesViewActive => _isChangesViewActive;

    public bool IsDiffPreviewDeferred
    {
        get => _isDiffPreviewDeferred;
        private set
        {
            if (_isDiffPreviewDeferred == value) return;
            _isDiffPreviewDeferred = value;
            Notify();
        }
    }

    public string? DiffPreviewDeferredMessage
    {
        get => _diffPreviewDeferredMessage;
        private set
        {
            if (string.Equals(_diffPreviewDeferredMessage, value, StringComparison.Ordinal)) return;
            _diffPreviewDeferredMessage = value;
            Notify();
        }
    }

    public string DiffPreviewActionText
    {
        get => _diffPreviewActionText;
        private set
        {
            if (string.Equals(_diffPreviewActionText, value, StringComparison.Ordinal)) return;
            _diffPreviewActionText = value;
            Notify();
        }
    }

    public string? DiffLoadErrorMessage
    {
        get => _diffLoadErrorMessage;
        private set
        {
            if (string.Equals(_diffLoadErrorMessage, value, StringComparison.Ordinal)) return;
            _diffLoadErrorMessage = value;
            Notify();
        }
    }

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

    private void OnSelectedHistoryRowChanged(HistoryRow? previous)
    {
        _selectedChangedFileRestoreKey =
            previous is not null &&
            SelectedHistoryRow is { } current &&
            string.Equals(previous.Commit.Hash, current.Commit.Hash, StringComparison.Ordinal) &&
            SelectedFile is { } selected
                ? new ChangedFileSelectionKey(selected.Path, selected.OriginalPath)
                : null;

        InvalidateChangedFilesLoad();
        InvalidateDiffLoad();
        SelectedChangedFiles = [];
        SelectedFile = null;
        SelectedDiff = null;
        DiffLoadErrorMessage = null;
        ClearDiffPreviewDeferred();
        IsChangedFilesLoading = false;
        IsDiffLoading = false;

        if (_isChangesViewActive)
            _ = EnsureChangedFilesLoadedAsync(debounce: true);
    }

    private void OnSelectedFileChanged()
    {
        InvalidateDiffLoad();
        SelectedDiff = null;
        DiffLoadErrorMessage = null;
        IsDiffLoading = false;
        ClearDiffPreviewDeferred();
        StartSelectedDiffPreview();
    }

    public void LoadSelectedDiffAnyway()
    {
        if (!_isChangesViewActive || SelectedFile is null || IsDiffLoading) return;
        ClearDiffPreviewDeferred();
        _ = LoadSelectedDiffAsync(DiffLoadMode.Full);
    }

    private void StartSelectedDiffPreview()
    {
        if (!_isChangesViewActive || SelectedFile is not { } file) return;
        if (TryDeferLargeHistoricalDiff(file)) return;
        _ = LoadSelectedDiffAsync(DiffLoadMode.Preview);
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
        DiffLoadErrorMessage = null;
        ClearDiffPreviewDeferred();
        _selectedChangedFileRestoreKey = null;
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
                _selectedChangedFileRestoreKey = null;
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
        var restored = _selectedChangedFileRestoreKey is { } restoreKey
            ? files.FirstOrDefault(restoreKey.Matches)
            : null;
        _selectedChangedFileRestoreKey = null;
        var target = restored ?? files.FirstOrDefault();

        if (!ReferenceEquals(SelectedFile, target)) SelectedFile = target;
        SelectedChangedFiles = files;
        if (target is not null && SelectedDiff is null && !IsDiffLoading && !IsDiffPreviewDeferred)
            StartSelectedDiffPreview();
    }

    private async Task LoadSelectedDiffAsync(DiffLoadMode mode)
    {
        var repository = Repository;
        var row = SelectedHistoryRow;
        var file = SelectedFile;
        if (!_isChangesViewActive || repository is null || row is null || file is null) return;

        DiffLoadErrorMessage = null;
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
                mode,
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
        catch (DiffPreviewTooLargeException exception) when (mode == DiffLoadMode.Preview)
        {
            if (IsCurrentDiffRequest(generation, cancellation, repository, row, file))
            {
                SelectedDiff = null;
                DiffLoadErrorMessage = null;
                SetDiffPreviewDeferred(
                    $"The diff is larger than the {FormatByteSize(exception.LimitBytes)} automatic preview limit. " +
                    "Generating and displaying the full diff may take some time.",
                    "Load full diff anyway");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (IsCurrentDiffRequest(generation, cancellation, repository, row, file))
            {
                SelectedDiff = null;
                DiffLoadErrorMessage = exception.Message;
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

    private bool TryDeferLargeHistoricalDiff(ChangedFile file)
    {
        if (file.IsBinary) return false;
        var changedLines = (long)(file.AddedLines ?? 0) + (file.RemovedLines ?? 0);
        if (changedLines < DiffPreviewPolicy.LargeChangedLines) return false;

        SetDiffPreviewDeferred(
            $"This change contains {changedLines:N0} changed lines. " +
            "Generating and displaying the full diff may take some time.",
            "Load diff");
        return true;
    }

    private void SetDiffPreviewDeferred(string message, string actionText)
    {
        DiffPreviewDeferredMessage = message;
        DiffPreviewActionText = actionText;
        IsDiffPreviewDeferred = true;
    }

    private void ClearDiffPreviewDeferred()
    {
        IsDiffPreviewDeferred = false;
        DiffPreviewDeferredMessage = null;
        DiffPreviewActionText = "Load diff";
    }

    private static string FormatByteSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (1024d * 1024d):0.#} MB"
            : $"{Math.Max(1, bytes / 1024d):0.#} KB";

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
        size += diff.Diagnostics.Count * 32L;
        return (int)Math.Min(size, int.MaxValue);
    }

    private readonly record struct ChangedFileSelectionKey(string Path, string? OriginalPath)
    {
        public bool Matches(ChangedFile file) =>
            string.Equals(file.Path, Path, StringComparison.Ordinal) ||
            OriginalPath is not null &&
                (string.Equals(file.Path, OriginalPath, StringComparison.Ordinal) ||
                 string.Equals(file.OriginalPath, OriginalPath, StringComparison.Ordinal)) ||
            file.OriginalPath is not null &&
                string.Equals(file.OriginalPath, Path, StringComparison.Ordinal);
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
