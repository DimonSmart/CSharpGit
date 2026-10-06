using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Threading;

namespace CSharpGit.Presentation.ViewModels;

public sealed record UiChoice<T>(string Label, T Value);

internal enum HistoryDisplayMode
{
    CurrentBranch,
    AllReferences,
    AllReferencesWithReflog,
    SpecificReference
}

public interface IHistoryRepositoryContext
{
    Repository? Repository { get; }
    bool? HeadExists { get; }
    string? CurrentBranchName { get; }
    string? CurrentHeadCommit { get; }
    bool IsDetachedHead { get; }
    IReadOnlyList<GitBranch> LocalBranches { get; }
    IReadOnlyList<GitBranch> RemoteBranches { get; }
    IReadOnlyList<GitRemote> Remotes { get; }
    IReadOnlyList<GitTag> Tags { get; }
    bool ShouldAutoSelectHistoryRow { get; }

    void EnterHistoryBusy();
    void ExitHistoryBusy();
    void ReportHistoryError(string message);
}

public sealed class HistoryViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IHistoryService _historyService;
    private readonly IAppSettingsService _settings;
    private readonly IUiDispatcher _uiDispatcher;
    private IHistoryRepositoryContext? _context;
    private CancellationTokenSource? _loadCts;
    private long _loadGeneration;
    private long _reflogSessionId;
    private string _filterText = string.Empty;
    private UiChoice<HistoryScope> _selectedScope;
    private HistoryRow? _selectedRow;
    private bool _showReflog;
    private string? _activeReference;
    private string? _activeReferenceLabel;
    private bool _hasMore;
    private bool _isLoading;
    private int _disposed;

    public HistoryViewModel(
        IHistoryService historyService,
        IAppSettingsService settings,
        IUiDispatcher uiDispatcher)
    {
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _selectedScope = Scopes[0];
        _showReflog = _settings.ShowReflog;
        _settings.Changed += AppSettings_Changed;

        RefreshCommand = new AsyncCommand(RefreshAsync, CanRefresh);
        LoadMoreCommand = new AsyncCommand(LoadMoreAsync, CanLoadMore);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<HistoryRow> Rows { get; } = new BulkObservableCollection<HistoryRow>();

    public IReadOnlyList<UiChoice<HistoryScope>> Scopes { get; } =
    [
        new("All references", HistoryScope.AllReferences),
        new("Current branch", HistoryScope.CurrentBranch)
    ];

    public ICommand RefreshCommand { get; }
    public ICommand LoadMoreCommand { get; }

    public HistoryRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value)) return;
            _selectedRow = value;
            Notify();
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_filterText, value, StringComparison.Ordinal)) return;
            _filterText = value;
            Notify();
            InvalidateCurrentRequest();
        }
    }

    public UiChoice<HistoryScope> SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (ReferenceEquals(_selectedScope, value) || _selectedScope == value) return;
            _ = SetScopeAsync(value);
        }
    }

    public bool ShowReflog
    {
        get => _showReflog;
        set
        {
            if (_showReflog == value) return;
            _ = SetShowReflogAsync(value, persist: true, refresh: true);
        }
    }

    public string? ActiveReference => _activeReference;
    public string? ActiveReferenceLabel => _activeReferenceLabel;
    public bool IsReferenceScoped => _activeReference is not null;

    public bool HasMore
    {
        get => _hasMore;
        private set
        {
            if (_hasMore == value) return;
            _hasMore = value;
            Notify();
            RefreshAvailability();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            Notify();
            RefreshAvailability();
        }
    }

    internal HistoryDisplayMode HistoryDisplayMode =>
        IsReferenceScoped
            ? HistoryDisplayMode.SpecificReference
            : _showReflog
                ? HistoryDisplayMode.AllReferencesWithReflog
                : _selectedScope == Scopes[1]
                    ? HistoryDisplayMode.CurrentBranch
                    : HistoryDisplayMode.AllReferences;

    internal void Attach(IHistoryRepositoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _context = context;
        RefreshAvailability();
    }

    internal void OnRepositoryChanged(Repository? repository)
    {
        InvalidateCurrentRequest();
        ReplaceRows([]);
        HasMore = false;
        SelectedRow = null;
        ClearReferenceState();

        if (repository is null)
        {
            SetScopeDirect(Scopes[0]);
            ApplyShowReflogDirect(_settings.ShowReflog);
        }

        RefreshAvailability();
    }

    internal void ClearRepositoryState() => OnRepositoryChanged(null);

    internal void Invalidate(bool clearRows = false, bool exitReferenceScope = false)
    {
        InvalidateCurrentRequest();
        if (exitReferenceScope)
            ClearReferenceState();
        if (clearRows)
        {
            ReplaceRows([]);
            HasMore = false;
            SelectedRow = null;
        }
        RefreshAvailability();
    }

    internal void ResetForRepositoryMutation(bool clearRows = false)
    {
        InvalidateCurrentRequest();
        ClearReferenceState();
        SetScopeDirect(Scopes[0]);
        if (clearRows)
        {
            ReplaceRows([]);
            HasMore = false;
            SelectedRow = null;
        }
        RefreshAvailability();
    }

    internal void RetargetReference(string oldReference, string newReference, string label)
    {
        if (!string.Equals(_activeReference, oldReference, StringComparison.Ordinal)) return;

        _activeReference = newReference;
        _activeReferenceLabel = label;
        Notify(nameof(ActiveReference));
        Notify(nameof(ActiveReferenceLabel));
        Notify(nameof(HistoryDisplayMode));
        InvalidateCurrentRequest();
    }

    internal Task SetHistoryDisplayModeAsync(HistoryDisplayMode mode) =>
        ApplyHistoryDisplayModeAsync(mode);

    public Task RefreshAsync() => LoadAsync(reset: true);

    public Task LoadMoreAsync() =>
        HasMore && !IsLoading
            ? LoadAsync(reset: false)
            : Task.CompletedTask;

    public async Task ShowReferenceAsync(string reference, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var selectedHash = SelectedRow?.Commit.Hash;
        if (_showReflog)
            await SetShowReflogAsync(false, persist: true, refresh: false);

        var referenceChanged = !string.Equals(_activeReference, reference, StringComparison.Ordinal);
        var labelChanged = !string.Equals(_activeReferenceLabel, label, StringComparison.Ordinal);
        _activeReference = reference;
        _activeReferenceLabel = label;
        if (referenceChanged)
        {
            Notify(nameof(ActiveReference));
            Notify(nameof(IsReferenceScoped));
            Notify(nameof(HistoryDisplayMode));
        }
        if (labelChanged)
            Notify(nameof(ActiveReferenceLabel));

        await LoadAsync(reset: true, preferredSelectionHash: selectedHash);
    }

    public async Task ShowAllAsync()
    {
        var selectedHash = SelectedRow?.Commit.Hash;
        var wasScoped = IsReferenceScoped;
        ClearReferenceState();
        var scopeChanged = SetScopeDirect(Scopes[0]);
        if (!wasScoped && !scopeChanged)
        {
            var existing = selectedHash is null
                ? Rows.FirstOrDefault()
                : Rows.FirstOrDefault(row => string.Equals(row.Commit.Hash, selectedHash, StringComparison.Ordinal))
                  ?? Rows.FirstOrDefault();
            if (_context?.ShouldAutoSelectHistoryRow != false)
                SelectedRow = existing;
            return;
        }

        await LoadAsync(reset: true, preferredSelectionHash: selectedHash);
    }

    public async Task<HistoryRow?> NavigateToCommitAsync(
        string commitHash,
        int trailingCount = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitHash);
        if (_context?.Repository is not { } repository) return null;

        var requiresReload =
            IsReferenceScoped
            || _selectedScope != Scopes[0]
            || !string.IsNullOrWhiteSpace(_filterText);

        ClearReferenceState();
        SetScopeDirect(Scopes[0]);
        if (_filterText.Length > 0)
        {
            _filterText = string.Empty;
            Notify(nameof(FilterText));
        }

        if (!requiresReload && Rows.FirstOrDefault(row =>
                string.Equals(row.Commit.Hash, commitHash, StringComparison.Ordinal)) is { } existing)
        {
            SelectedRow = existing;
            return existing;
        }

        var reflogSessionId = Interlocked.Increment(ref _reflogSessionId);
        var query = CreateNormalQuery(HistoryScope.AllReferences, null, 0, reflogSessionId);
        var request = BeginRequest(repository);
        if (request is null) return null;

        var (generation, cancellation) = request.Value;
        EnterLoading();
        try
        {
            var page = await _historyService.ReadHistoryThroughCommitAsync(
                repository,
                query,
                commitHash,
                trailingCount,
                cancellation.Token);

            if (!CanPublish(generation, cancellation, repository))
                return null;

            ReplaceRows(page.Rows);
            HasMore = page.HasMore;
            var target = Rows.FirstOrDefault(row =>
                string.Equals(row.Commit.Hash, commitHash, StringComparison.Ordinal));
            if (target is null)
                throw new InvalidOperationException($"Commit {commitHash} was not present after history navigation load.");

            SelectedRow = target;
            return target;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _loadGeneration))
                _context?.ReportHistoryError($"Could not navigate to commit: {exception.Message}");
            return null;
        }
        finally
        {
            ExitLoading(generation, cancellation);
        }
    }

    private async Task SetScopeAsync(UiChoice<HistoryScope> scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (_selectedScope == scope && !IsReferenceScoped) return;

        ClearReferenceState();
        SetScopeDirect(scope);
        if (scope.Value != HistoryScope.AllReferences && _showReflog)
            await SetShowReflogAsync(false, persist: true, refresh: false);

        await LoadAsync(reset: true);
    }

    private async Task ApplyHistoryDisplayModeAsync(HistoryDisplayMode mode)
    {
        if (mode == HistoryDisplayMode.SpecificReference) return;

        var targetScope = mode == HistoryDisplayMode.CurrentBranch ? Scopes[1] : Scopes[0];
        var targetReflog = mode == HistoryDisplayMode.AllReferencesWithReflog;
        if (!IsReferenceScoped
            && _selectedScope == targetScope
            && _showReflog == targetReflog)
            return;

        ClearReferenceState();
        SetScopeDirect(targetScope);
        if (_showReflog != targetReflog)
            await SetShowReflogAsync(targetReflog, persist: true, refresh: false);

        await LoadAsync(reset: true);
    }

    private async Task SetShowReflogAsync(bool value, bool persist, bool refresh)
    {
        var changed = ApplyShowReflogDirect(value);
        if (value && !IsReferenceScoped)
            changed |= SetScopeDirect(Scopes[0]);

        if (persist)
            await PersistShowReflogAsync(value);

        if (changed && refresh)
            await LoadAsync(reset: true);
    }

    private bool ApplyShowReflogDirect(bool value)
    {
        if (_showReflog == value) return false;
        _showReflog = value;
        Notify(nameof(ShowReflog));
        Notify(nameof(HistoryDisplayMode));
        return true;
    }

    private bool SetScopeDirect(UiChoice<HistoryScope> scope)
    {
        if (_selectedScope == scope) return false;
        _selectedScope = scope;
        Notify(nameof(SelectedScope));
        Notify(nameof(HistoryDisplayMode));
        return true;
    }

    private void ClearReferenceState()
    {
        if (_activeReference is null && _activeReferenceLabel is null) return;
        var wasScoped = _activeReference is not null;
        _activeReference = null;
        _activeReferenceLabel = null;
        Notify(nameof(ActiveReference));
        Notify(nameof(ActiveReferenceLabel));
        if (wasScoped)
        {
            Notify(nameof(IsReferenceScoped));
            Notify(nameof(HistoryDisplayMode));
        }
    }

    private async Task LoadAsync(bool reset, string? preferredSelectionHash = null)
    {
        if (_context?.Repository is not { } repository) return;
        if (reset && IsReferenceScoped && !IsActiveReferenceAvailable())
        {
            ClearReferenceState();
            SetScopeDirect(Scopes[0]);
        }

        var selectedHash = reset
            ? preferredSelectionHash ?? SelectedRow?.Commit.Hash
            : null;
        var skip = reset ? 0 : Rows.Count;
        var reference = _activeReference;
        var reflogSessionId = reference is null
            ? reset
                ? Interlocked.Increment(ref _reflogSessionId)
                : Volatile.Read(ref _reflogSessionId)
            : 0;
        var query = reference is null
            ? CreateNormalQuery(_selectedScope.Value, _filterText, skip, reflogSessionId)
            : new HistoryQuery(
                HistoryScope.CurrentBranch,
                _filterText,
                skip,
                100,
                Reference: reference);

        var request = BeginRequest(repository);
        if (request is null) return;
        var (generation, cancellation) = request.Value;
        EnterLoading();
        try
        {
            var page = await _historyService.ReadHistoryAsync(
                repository,
                query,
                cancellation.Token);

            if (!CanPublish(generation, cancellation, repository))
                return;

            if (reset)
                ReplaceRows(page.Rows);
            else
                AppendRows(page.Rows);
            HasMore = page.HasMore;

            if (reset)
            {
                if (_context?.ShouldAutoSelectHistoryRow != false)
                {
                    SelectedRow = selectedHash is null
                        ? Rows.FirstOrDefault()
                        : Rows.FirstOrDefault(row =>
                            string.Equals(row.Commit.Hash, selectedHash, StringComparison.Ordinal))
                          ?? Rows.FirstOrDefault();
                }
            }
            else if (_context?.ShouldAutoSelectHistoryRow != false
                     && SelectedRow is null
                     && Rows.FirstOrDefault() is { } first)
            {
                SelectedRow = first;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _loadGeneration))
            {
                var prefix = reference is null
                    ? "Could not read history"
                    : "Could not read the selected reference history";
                _context?.ReportHistoryError($"{prefix}: {exception.Message}");
            }
        }
        finally
        {
            ExitLoading(generation, cancellation);
        }
    }

    private HistoryQuery CreateNormalQuery(
        HistoryScope scope,
        string? filter,
        int skip,
        long reflogSessionId) =>
        new(
            scope,
            filter,
            skip,
            IncludeReflog: _showReflog,
            HeadExists: _context?.HeadExists,
            RepositoryReferences: new GitReferences(
                _context?.LocalBranches.ToArray() ?? [],
                _context?.RemoteBranches.ToArray() ?? [],
                _context?.Remotes.ToArray() ?? [],
                _context?.Tags.ToArray() ?? []),
            HeadReference: _context?.CurrentBranchName,
            HeadCommit: _context?.CurrentHeadCommit,
            IsDetachedHead: _context?.IsDetachedHead == true,
            ReflogSessionId: reflogSessionId);

    private bool IsActiveReferenceAvailable()
    {
        if (_activeReference is not { } reference || _context is null) return true;
        if (_context.LocalBranches.Any(branch => string.Equals(branch.Name, reference, StringComparison.Ordinal)))
            return true;
        if (_context.RemoteBranches.Any(branch => string.Equals(branch.Name, reference, StringComparison.Ordinal)))
            return true;
        if (reference.StartsWith("refs/tags/", StringComparison.Ordinal))
        {
            var tagName = reference["refs/tags/".Length..];
            return _context.Tags.Any(tag => string.Equals(tag.Name, tagName, StringComparison.Ordinal));
        }

        return false;
    }

    private (long Generation, CancellationTokenSource Cancellation)? BeginRequest(Repository repository)
    {
        if (Volatile.Read(ref _disposed) != 0
            || _context is null
            || !ReferenceEquals(repository, _context.Repository))
            return null;

        var generation = Interlocked.Increment(ref _loadGeneration);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, cancellation);
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        return (generation, cancellation);
    }

    private bool CanPublish(
        long generation,
        CancellationTokenSource cancellation,
        Repository repository) =>
        Volatile.Read(ref _disposed) == 0
        && !cancellation.IsCancellationRequested
        && generation == Volatile.Read(ref _loadGeneration)
        && _context is not null
        && ReferenceEquals(repository, _context.Repository);

    private void InvalidateCurrentRequest()
    {
        Interlocked.Increment(ref _loadGeneration);
        var cancellation = Interlocked.Exchange(ref _loadCts, null);
        IsLoading = false;
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void EnterLoading()
    {
        IsLoading = true;
        _context?.EnterHistoryBusy();
    }

    private void ExitLoading(long generation, CancellationTokenSource cancellation)
    {
        _context?.ExitHistoryBusy();
        if (generation == Volatile.Read(ref _loadGeneration))
            IsLoading = false;

        if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, cancellation), cancellation))
            cancellation.Dispose();
    }

    private void ReplaceRows(IReadOnlyList<HistoryRow> rows)
    {
        if (Rows is BulkObservableCollection<HistoryRow> bulk)
        {
            bulk.ReplaceAll(rows);
            return;
        }

        Rows.Clear();
        foreach (var row in rows)
            Rows.Add(row);
    }

    private void AppendRows(IEnumerable<HistoryRow> rows)
    {
        foreach (var row in rows)
            Rows.Add(row);
    }

    private bool CanRefresh() =>
        Volatile.Read(ref _disposed) == 0
        && _context?.Repository is not null
        && !IsLoading;

    private bool CanLoadMore() => CanRefresh() && HasMore;

    internal void RefreshAvailability()
    {
        ((AsyncCommand)RefreshCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)LoadMoreCommand).RaiseCanExecuteChanged();
    }

    private async Task PersistShowReflogAsync(bool value)
    {
        try
        {
            await _settings.SetShowReflogAsync(value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _context?.ReportHistoryError($"Could not save Show reflog setting: {exception.Message}");
        }
    }

    private void AppSettings_Changed(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        if (_uiDispatcher.HasThreadAccess)
        {
            ApplySettingsChangeOnUiThread();
            return;
        }

        _uiDispatcher.TryEnqueue(() =>
        {
            if (Volatile.Read(ref _disposed) == 0)
                ApplySettingsChangeOnUiThread();
        });
    }

    private void ApplySettingsChangeOnUiThread()
    {
        var showReflog = _settings.ShowReflog;
        if (IsReferenceScoped && showReflog)
        {
            _ = PersistShowReflogAsync(false);
            return;
        }

        if (_showReflog == showReflog) return;

        ApplyShowReflogDirect(showReflog);
        if (showReflog)
            SetScopeDirect(Scopes[0]);
        _ = LoadAsync(reset: true);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _settings.Changed -= AppSettings_Changed;
        InvalidateCurrentRequest();
        _context = null;
        IsLoading = false;
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
