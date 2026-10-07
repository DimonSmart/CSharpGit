using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using Microsoft.Extensions.Logging;

namespace CSharpGit.Presentation.ViewModels;

public interface ICommitDetailsRepositoryContext
{
    Repository? Repository { get; }
    void ReportCommitDetailsError(string message);
}

public sealed class CommitDetailsViewModel :
    INotifyPropertyChanged,
    IRepositoryFilesContext,
    IDisposable
{
    private const int ChangedFilesDebounceMilliseconds = 120;
    private const int ChangedFilesCacheEntries = 64;
    private const int DiffCacheEntries = 96;
    private const int DiffCacheCharacters = 4 * 1024 * 1024;

    private readonly IHistoryService _historyService;
    private readonly IStashService _stashService;
    private readonly ILogger<CommitDetailsViewModel> _logger;
    private readonly BoundedLruCache<ChangedFilesCacheKey, IReadOnlyList<ChangedFile>> _changedFilesCache =
        new(ChangedFilesCacheEntries, ChangedFilesCacheEntries, _ => 1);
    private readonly BoundedLruCache<DiffCacheKey, FileDiff> _diffCache =
        new(DiffCacheEntries, DiffCacheCharacters, EstimateDiffSize);

    private ICommitDetailsRepositoryContext? _context;
    private HistoryRow? _selectedHistoryRow;
    private GitStash? _selectedStash;
    private StashDetails? _selectedStashDetails;
    private CancellationTokenSource? _changedFilesLoadCts;
    private CancellationTokenSource? _diffLoadCts;
    private CancellationTokenSource? _stashDetailsCts;
    private long _changedFilesLoadGeneration;
    private long _diffLoadGeneration;
    private long _stashDetailsGeneration;
    private IReadOnlyList<ChangedFile> _changedFiles = [];
    private ChangedFile? _selectedFile;
    private FileDiff? _selectedDiff;
    private bool _isChangedFilesLoading;
    private bool _isDiffLoading;
    private bool _isChangesViewActive;
    private string? _diffLoadErrorMessage;
    private bool _isDiffPreviewDeferred;
    private string? _diffPreviewDeferredMessage;
    private string _diffPreviewActionText = "Load diff";
    private bool _isNoNetStashDiff;
    private ChangedFileSelectionKey? _selectedChangedFileRestoreKey;
    private int _disposed;

    public CommitDetailsViewModel(
        IHistoryService historyService,
        IStashService stashService,
        ILogger<CommitDetailsViewModel> logger)
    {
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _stashService = stashService ?? throw new ArgumentNullException(nameof(stashService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Repository? Repository => _context?.Repository;
    public IReadOnlyList<ChangedFile> ChangedFiles => _changedFiles;
    public StashDetails? SelectedStashDetails => _selectedStashDetails;
    public bool HasSelectedStash => _selectedStash is not null;
    public bool HasSelectedDetailsObject => _selectedStash is not null || _selectedHistoryRow is not null;
    public string? SelectedObjectCommit => _selectedStash?.Commit ?? _selectedHistoryRow?.Commit.Hash;
    public string SelectedDetailsTitle => _selectedStash is null ? "Commit" : "Stash";
    public bool IsChangesViewActive => _isChangesViewActive;
    public bool HasTextDiff => SelectedDiff is { IsBinary: false };
    public bool HasBinaryDiff => SelectedDiff?.IsBinary == true;

    public ChangedFile? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (ReferenceEquals(_selectedFile, value)) return;
            _selectedFile = value;
            Notify();
            Notify(nameof(SelectedDiffCommitHash));
            OnSelectedFileChanged();
        }
    }

    public FileDiff? SelectedDiff
    {
        get => _selectedDiff;
        private set
        {
            if (ReferenceEquals(_selectedDiff, value)) return;
            _selectedDiff = value;
            Notify();
            Notify(nameof(HasTextDiff));
            Notify(nameof(HasBinaryDiff));
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

    public bool IsDiffLoading
    {
        get => _isDiffLoading;
        private set
        {
            if (_isDiffLoading == value) return;
            _isDiffLoading = value;
            Notify();
        }
    }

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

    public bool IsNoNetStashDiff
    {
        get => _isNoNetStashDiff;
        private set
        {
            if (_isNoNetStashDiff == value) return;
            _isNoNetStashDiff = value;
            Notify();
        }
    }

    public string SelectedStashDisplay => _selectedStash?.Display ?? string.Empty;

    public string SelectedStashHashDisplay =>
        _selectedStash is null ? string.Empty : $"Stash: {ShortHash(_selectedStash.Commit)}";

    public string SelectedStashBaseDisplay =>
        _selectedStashDetails is null
            ? "Base: loading…"
            : $"Base: {ShortHash(_selectedStashDetails.BaseCommit)}";

    public string SelectedStashStatsDisplay =>
        _selectedStashDetails is null
            ? string.Empty
            : $"{_selectedStashDetails.Changes.Count} files changed    +{_selectedStashDetails.AddedLines} -{_selectedStashDetails.RemovedLines}";

    public string SelectedStashDetailsStatus =>
        _selectedStashDetails is null
            ? "Loading stash structure…"
            : _selectedStashDetails.UntrackedCommit is null
                ? "Tracked repository snapshot saved in this stash."
                : "Tracked repository snapshot saved in this stash. Untracked stash files are shown in Changes.";

    public string? SelectedDiffCommitHash
    {
        get
        {
            if (_selectedStash is null)
                return _selectedHistoryRow?.Commit.Hash;

            if (_selectedStashDetails is null || SelectedFile is null)
                return _selectedStash.Commit;

            var change = FindSelectedStashChange(SelectedFile);
            return change?.State == StashChangeState.Untracked
                ? _selectedStashDetails.UntrackedCommit
                : _selectedStash.Commit;
        }
    }

    internal void Attach(ICommitDetailsRepositoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _context = context;
        Notify(nameof(Repository));
    }

    internal void OnRepositoryChanged(Repository? repository)
    {
        ResetSession(clearSelection: true, clearCaches: true);
        Notify(nameof(Repository));
    }

    internal void ClearRepositoryState() =>
        ResetSession(clearSelection: true, clearCaches: true);

    public void ShowCommit(HistoryRow? row)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var previous = _selectedHistoryRow;
        ChangedFileSelectionKey? restoreKey =
            _selectedStash is null
            && previous is not null
            && row is not null
            && string.Equals(previous.Commit.Hash, row.Commit.Hash, StringComparison.Ordinal)
            && SelectedFile is { } selected
                ? new ChangedFileSelectionKey(selected.Path, selected.OriginalPath)
                : null;

        InvalidateChangedFilesLoad();
        InvalidateDiffLoad();
        InvalidateStashDetailsLoad();
        _selectedHistoryRow = row;
        _selectedStash = null;
        SetSelectedStashDetails(null);
        ClearFilePresentation();
        _selectedChangedFileRestoreKey = restoreKey;
        NotifySelectionState();

        if (_isChangesViewActive && row is not null)
            _ = EnsureChangedFilesLoadedAsync(debounce: true);
    }

    public async Task ShowStashAsync(GitStash? stash)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var sameIdentity = stash is not null
            && string.Equals(_selectedStash?.Commit, stash.Commit, StringComparison.Ordinal);

        _selectedHistoryRow = null;
        if (sameIdentity)
        {
            _selectedStash = stash;
            NotifySelectionState();
            if (_selectedStashDetails is { } current)
            {
                if (!ReferenceEquals(current.Stash, stash))
                    SetSelectedStashDetails(current with { Stash = stash! });
                if (_isChangesViewActive)
                    PublishSelectedStashChanges(_selectedStashDetails!);
                return;
            }
        }
        else
        {
            InvalidateChangedFilesLoad();
            InvalidateDiffLoad();
            InvalidateStashDetailsLoad();
            _selectedStash = stash;
            SetSelectedStashDetails(null);
            ClearFilePresentation();
            _selectedChangedFileRestoreKey = null;
            NotifySelectionState();
        }

        if (stash is not null)
            await EnsureSelectedStashDetailsLoadedAsync();
    }

    public void ClearSelection()
    {
        InvalidateChangedFilesLoad();
        InvalidateDiffLoad();
        InvalidateStashDetailsLoad();
        _selectedHistoryRow = null;
        _selectedStash = null;
        SetSelectedStashDetails(null);
        ClearFilePresentation();
        _selectedChangedFileRestoreKey = null;
        NotifySelectionState();
    }

    public void SetActive(bool active)
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

        _ = _selectedStash is null
            ? EnsureChangedFilesLoadedAsync(debounce: false)
            : EnsureSelectedStashDetailsLoadedAsync();
    }

    public void LoadSelectedDiffAnyway()
    {
        if (!_isChangesViewActive || SelectedFile is null || IsDiffLoading) return;
        ClearDiffPreviewDeferred();
        _ = LoadSelectedDiffAsync(DiffLoadMode.Full);
    }

    public void Invalidate() =>
        ResetSession(clearSelection: true, clearCaches: true);

    internal string GetChangedFileDisplayStatus(ChangedFile file)
    {
        var stashChange = FindSelectedStashChange(file);
        if (stashChange is null)
            return file.Status;

        var gitStatus = stashChange.State == StashChangeState.Untracked ? "?" : file.Status;
        return $"{gitStatus}  {stashChange.StateLabel}";
    }

    internal StashChangedFile? FindSelectedStashChange(ChangedFile file)
    {
        if (_selectedStashDetails is null) return null;
        return _selectedStashDetails.Changes.FirstOrDefault(change =>
            SameLogicalPath(change.File, file));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        ResetSession(clearSelection: true, clearCaches: true);
        _context = null;
    }

    private void OnSelectedFileChanged()
    {
        InvalidateDiffLoad();
        SelectedDiff = null;
        DiffLoadErrorMessage = null;
        IsDiffLoading = false;
        ClearDiffPreviewDeferred();
        ClearNoNetStashDiff();
        StartSelectedDiffPreview();
    }

    private void StartSelectedDiffPreview()
    {
        if (!_isChangesViewActive || SelectedFile is not { } file) return;
        if (TryDeferLargeHistoricalDiff(file)) return;
        _ = LoadSelectedDiffAsync(DiffLoadMode.Preview);
    }

    private async Task EnsureChangedFilesLoadedAsync(bool debounce)
    {
        var repository = Repository;
        var row = _selectedHistoryRow;
        if (!_isChangesViewActive || repository is null || row is null || _selectedStash is not null)
            return;

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
                SetChangedFiles([]);
                SelectedFile = null;
                SelectedDiff = null;
                _selectedChangedFileRestoreKey = null;
                ReportError($"Could not read changes: {exception.Message}");
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

    private void PublishChangedFiles(
        Repository repository,
        HistoryRow row,
        IReadOnlyList<ChangedFile> files)
    {
        if (!_isChangesViewActive
            || !ReferenceEquals(repository, Repository)
            || !ReferenceEquals(row, _selectedHistoryRow))
            return;

        var restored = _selectedChangedFileRestoreKey is { } restoreKey
            ? files.FirstOrDefault(restoreKey.Matches)
            : null;
        _selectedChangedFileRestoreKey = null;
        var target = restored ?? files.FirstOrDefault();

        if (!ReferenceEquals(SelectedFile, target))
            SelectedFile = target;
        SetChangedFiles(files);
        if (target is not null && SelectedDiff is null && !IsDiffLoading && !IsDiffPreviewDeferred)
            StartSelectedDiffPreview();
    }

    private async Task EnsureSelectedStashDetailsLoadedAsync()
    {
        var repository = Repository;
        var stash = _selectedStash;
        if (repository is null || stash is null)
            return;

        if (_selectedStashDetails is { } current
            && string.Equals(current.Stash.Commit, stash.Commit, StringComparison.Ordinal))
        {
            if (_isChangesViewActive)
                PublishSelectedStashChanges(current);
            return;
        }

        var generation = Interlocked.Increment(ref _stashDetailsGeneration);
        var cancellation = new CancellationTokenSource();
        ReplaceCancellation(ref _stashDetailsCts, cancellation);

        if (_isChangesViewActive)
            IsChangedFilesLoading = true;

        try
        {
            var stashDetails = await _stashService.ReadAsync(repository, stash, cancellation.Token);
            if (!IsCurrentStashDetailsRequest(generation, cancellation, repository, stash))
                return;

            SetSelectedStashDetails(stashDetails with { Stash = _selectedStash! });
            if (_isChangesViewActive)
                PublishSelectedStashChanges(_selectedStashDetails!);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (IsCurrentStashDetailsRequest(generation, cancellation, repository, stash))
            {
                SetSelectedStashDetails(null);
                SetChangedFiles([]);
                SelectedFile = null;
                SelectedDiff = null;
                ReportError($"Could not read stash: {exception.Message}");
                _logger.LogWarning(exception, "Stash details loading failed for {StashCommit}", stash.Commit);
            }
        }
        finally
        {
            if (IsCurrentStashDetailsRequest(generation, cancellation, repository, stash)
                && _isChangesViewActive)
                IsChangedFilesLoading = false;
        }
    }

    private void PublishSelectedStashChanges(StashDetails stashDetails)
    {
        if (!_isChangesViewActive
            || !string.Equals(stashDetails.Stash.Commit, _selectedStash?.Commit, StringComparison.Ordinal))
            return;

        var files = stashDetails.Changes.Select(change => change.File).ToArray();
        var restored = _selectedChangedFileRestoreKey is { } restoreKey
            ? files.FirstOrDefault(restoreKey.Matches)
            : null;
        _selectedChangedFileRestoreKey = null;
        var target = restored ?? files.FirstOrDefault();

        if (!ReferenceEquals(SelectedFile, target))
            SelectedFile = target;
        SetChangedFiles(files);

        if (target is not null
            && SelectedDiff is null
            && !IsDiffLoading
            && !IsDiffPreviewDeferred
            && !IsNoNetStashDiff)
            StartSelectedDiffPreview();
    }

    private async Task LoadSelectedDiffAsync(DiffLoadMode mode)
    {
        var repository = Repository;
        var row = _selectedHistoryRow;
        var stash = _selectedStash;
        var file = SelectedFile;
        if (!_isChangesViewActive
            || repository is null
            || file is null
            || row is null && stash is null)
            return;

        DiffLoadErrorMessage = null;
        ClearNoNetStashDiff();

        string commitHash;
        string? parentHash;
        var selectionIdentity = stash?.Commit ?? row!.Commit.Hash;
        if (stash is not null)
        {
            var stashDetails = _selectedStashDetails;
            var stashChange = stashDetails is null ? null : FindSelectedStashChange(file);
            if (stashDetails is null || stashChange is null)
                return;

            if (stashChange.State != StashChangeState.Untracked && !stashChange.HasCombinedDiff)
            {
                SetNoNetStashDiff();
                return;
            }

            commitHash = stashChange.State == StashChangeState.Untracked
                ? stashDetails.UntrackedCommit
                    ?? throw new InvalidOperationException("The selected untracked stash file has no saved untracked tree.")
                : stash.Commit;
            parentHash = stashChange.State == StashChangeState.Untracked
                ? null
                : stashDetails.BaseCommit;
        }
        else
        {
            commitHash = row!.Commit.Hash;
            parentHash = row.Commit.Parents.FirstOrDefault();
        }

        var cacheKey = new DiffCacheKey(repository.GitDirectory, commitHash, parentHash, file.Path, file.OriginalPath);
        if (_diffCache.TryGet(cacheKey, out var cached))
        {
            _logger.LogDebug("LoadDiff commit={Commit} path={Path} duration={Duration}ms cache={Cache}", commitHash, file.Path, 0, "hit");
            if (_isChangesViewActive
                && ReferenceEquals(repository, Repository)
                && string.Equals(selectionIdentity, SelectedObjectCommit, StringComparison.Ordinal)
                && ReferenceEquals(file, SelectedFile))
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
                commitHash,
                parentHash,
                file,
                mode,
                cancellation.Token);
            stopwatch.Stop();

            if (!IsCurrentDiffRequest(generation, cancellation, repository, selectionIdentity, file))
                return;

            _diffCache.Set(cacheKey, diff);
            _logger.LogDebug("LoadDiff commit={Commit} path={Path} duration={Duration}ms cache={Cache}", commitHash, file.Path, stopwatch.ElapsedMilliseconds, "miss");
            SelectedDiff = diff;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (DiffPreviewTooLargeException exception) when (mode == DiffLoadMode.Preview)
        {
            if (IsCurrentDiffRequest(generation, cancellation, repository, selectionIdentity, file))
            {
                SelectedDiff = null;
                DiffLoadErrorMessage = null;
                SetDiffPreviewDeferred(
                    $"The diff is larger than the {FormatByteSize(exception.LimitBytes)} automatic preview limit. Generating and displaying the full diff may take some time.",
                    "Load full diff anyway");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (IsCurrentDiffRequest(generation, cancellation, repository, selectionIdentity, file))
            {
                SelectedDiff = null;
                DiffLoadErrorMessage = exception.Message;
                ReportError($"Could not read change: {exception.Message}");
                _logger.LogWarning(exception, "Diff loading failed for {Commit} {Path}", commitHash, file.Path);
            }
        }
        finally
        {
            stopwatch.Stop();
            if (IsCurrentDiffRequest(generation, cancellation, repository, selectionIdentity, file))
                IsDiffLoading = false;
        }
    }

    private bool TryDeferLargeHistoricalDiff(ChangedFile file)
    {
        if (file.IsBinary) return false;
        var changedLines = (long)(file.AddedLines ?? 0) + (file.RemovedLines ?? 0);
        if (changedLines < DiffPreviewPolicy.LargeChangedLines) return false;

        SetDiffPreviewDeferred(
            $"This change contains {changedLines:N0} changed lines. Generating and displaying the full diff may take some time.",
            "Load diff");
        return true;
    }

    private void SetChangedFiles(IReadOnlyList<ChangedFile> value)
    {
        if (ReferenceEquals(_changedFiles, value)) return;
        _changedFiles = value;
        Notify(nameof(ChangedFiles));
    }

    private void SetSelectedStashDetails(StashDetails? value)
    {
        if (ReferenceEquals(_selectedStashDetails, value)) return;
        _selectedStashDetails = value;
        Notify(nameof(SelectedStashDetails));
        Notify(nameof(SelectedStashBaseDisplay));
        Notify(nameof(SelectedStashStatsDisplay));
        Notify(nameof(SelectedStashDetailsStatus));
        Notify(nameof(SelectedDiffCommitHash));
    }

    private void NotifySelectionState()
    {
        Notify(nameof(HasSelectedStash));
        Notify(nameof(HasSelectedDetailsObject));
        Notify(nameof(SelectedObjectCommit));
        Notify(nameof(SelectedDetailsTitle));
        Notify(nameof(SelectedStashDisplay));
        Notify(nameof(SelectedStashHashDisplay));
        Notify(nameof(SelectedStashBaseDisplay));
        Notify(nameof(SelectedStashStatsDisplay));
        Notify(nameof(SelectedStashDetailsStatus));
        Notify(nameof(SelectedDiffCommitHash));
    }

    private void ClearFilePresentation()
    {
        SetChangedFiles([]);
        SelectedFile = null;
        SelectedDiff = null;
        DiffLoadErrorMessage = null;
        ClearDiffPreviewDeferred();
        ClearNoNetStashDiff();
        IsChangedFilesLoading = false;
        IsDiffLoading = false;
    }

    private void ResetSession(bool clearSelection, bool clearCaches)
    {
        InvalidateChangedFilesLoad();
        InvalidateDiffLoad();
        InvalidateStashDetailsLoad();
        if (clearCaches)
        {
            _changedFilesCache.Clear();
            _diffCache.Clear();
        }

        if (clearSelection)
        {
            _selectedHistoryRow = null;
            _selectedStash = null;
            SetSelectedStashDetails(null);
        }

        _selectedChangedFileRestoreKey = null;
        ClearFilePresentation();
        if (clearSelection)
            NotifySelectionState();
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

    private void SetNoNetStashDiff()
    {
        SelectedDiff = null;
        DiffLoadErrorMessage = null;
        IsDiffLoading = false;
        ClearDiffPreviewDeferred();
        IsNoNetStashDiff = true;
    }

    private void ClearNoNetStashDiff() => IsNoNetStashDiff = false;

    private bool IsCurrentChangedFilesRequest(
        long generation,
        CancellationTokenSource cancellation,
        Repository repository,
        HistoryRow row) =>
        !cancellation.IsCancellationRequested
        && generation == Volatile.Read(ref _changedFilesLoadGeneration)
        && _isChangesViewActive
        && ReferenceEquals(repository, Repository)
        && ReferenceEquals(row, _selectedHistoryRow)
        && _selectedStash is null;

    private bool IsCurrentDiffRequest(
        long generation,
        CancellationTokenSource cancellation,
        Repository repository,
        string selectionIdentity,
        ChangedFile file) =>
        !cancellation.IsCancellationRequested
        && generation == Volatile.Read(ref _diffLoadGeneration)
        && _isChangesViewActive
        && ReferenceEquals(repository, Repository)
        && string.Equals(selectionIdentity, SelectedObjectCommit, StringComparison.Ordinal)
        && ReferenceEquals(file, SelectedFile);

    private bool IsCurrentStashDetailsRequest(
        long generation,
        CancellationTokenSource cancellation,
        Repository repository,
        GitStash stash) =>
        !cancellation.IsCancellationRequested
        && generation == Volatile.Read(ref _stashDetailsGeneration)
        && ReferenceEquals(repository, Repository)
        && string.Equals(stash.Commit, _selectedStash?.Commit, StringComparison.Ordinal);

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

    private void InvalidateStashDetailsLoad()
    {
        Interlocked.Increment(ref _stashDetailsGeneration);
        CancelAndDispose(ref _stashDetailsCts);
    }

    private void ReportError(string message) =>
        _context?.ReportCommitDetailsError(message);

    private static void ReplaceCancellation(
        ref CancellationTokenSource? field,
        CancellationTokenSource replacement)
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

    private static bool SameLogicalPath(ChangedFile left, ChangedFile right)
    {
        var leftPaths = new[] { left.Path, left.OriginalPath }
            .Where(path => path is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        return new[] { right.Path, right.OriginalPath }
            .Where(path => path is not null)
            .Cast<string>()
            .Any(leftPaths.Contains);
    }

    private static string ShortHash(string hash) =>
        hash[..Math.Min(8, hash.Length)];

    private static string FormatByteSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (1024d * 1024d):0.#} MB"
            : $"{Math.Max(1, bytes / 1024d):0.#} KB";

    private static int EstimateDiffSize(FileDiff diff)
    {
        long size = diff.Path.Length;
        foreach (var line in diff.Lines) size += line.Text.Length + 1L;
        size += diff.Diagnostics.Count * 32L;
        return (int)Math.Min(size, int.MaxValue);
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private readonly record struct ChangedFileSelectionKey(string Path, string? OriginalPath)
    {
        public bool Matches(ChangedFile file) =>
            string.Equals(file.Path, Path, StringComparison.Ordinal)
            || OriginalPath is not null
                && (string.Equals(file.Path, OriginalPath, StringComparison.Ordinal)
                    || string.Equals(file.OriginalPath, OriginalPath, StringComparison.Ordinal))
            || file.OriginalPath is not null
                && string.Equals(file.OriginalPath, Path, StringComparison.Ordinal);
    }

    private readonly record struct ChangedFilesCacheKey(
        string Repository,
        string Commit,
        string? Parent);

    private readonly record struct DiffCacheKey(
        string Repository,
        string Commit,
        string? Parent,
        string Path,
        string? OriginalPath);

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
