using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Threading;
using Microsoft.Extensions.Logging;

namespace CSharpGit.Presentation.ViewModels;

internal enum HistoryDisplayMode
{
    CurrentBranch,
    AllReferences,
    AllReferencesWithReflog
}

public interface IHistoryRepositoryContext
{
    Repository? Repository { get; }
    bool? HeadExists { get; }
    string? HeadReference { get; }
    string? HeadCommit { get; }
    bool IsDetachedHead { get; }
    IReadOnlyList<GitBranch> LocalBranches { get; }
    IReadOnlyList<GitBranch> RemoteBranches { get; }
    IReadOnlyList<GitRemote> Remotes { get; }
    IReadOnlyList<GitTag> Tags { get; }
    bool CanUpdateHistorySelection { get; }

    void EnterHistoryBusy();
    void ExitHistoryBusy();
    void ReportHistoryError(string message);
}

public sealed class HistoryViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IHistoryService _historyService;
    private readonly IAppSettingsService _settings;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<HistoryViewModel> _logger;
    private readonly BulkObservableCollection<HistoryRow> _rows = [];
    private IHistoryRepositoryContext? _context;
    private CancellationTokenSource? _loadCts;
    private long _loadGeneration;
    private long _reflogSessionId;
    private string _filterText = string.Empty;
    private UiChoice<HistoryScope> _selectedScope;
    private HistoryRow? _selectedRow;
    private string? _activeReference;
    private string? _activeReferenceLabel;
    private bool _hasMore;
    private bool _isLoading;
    private bool _showReflog;
    private int _disposed;

    public HistoryViewModel(
        IHistoryService historyService,
        IAppSettingsService settings,
        IUiDispatcher uiDispatcher,
        ILogger<HistoryViewModel> logger)
    {
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _selectedScope = Scopes[0];
        _showReflog = _settings.ShowReflog;
        _settings.Changed += AppSettings_Changed;

        RefreshCommand = new AsyncCommand(
            RefreshAsync,
            () => _context?.Repository is not null && !IsLoading);
        LoadMoreCommand = new AsyncCommand(
            LoadMoreAsync,
            () => _context?.Repository is not null && HasMore && !IsLoading);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action<HistoryRow?>? SelectedRowChanged;

    public ObservableCollection<HistoryRow> Rows => _rows;

    public IReadOnlyList<UiChoice<HistoryScope>> Scopes { get; } =
    [
        new("All references", HistoryScope.AllReferences),
        new("Current branch", HistoryScope.CurrentBranch)
    ];

    public ICommand RefreshCommand { get; }
    public ICommand LoadMoreCommand { get; }

    public string FilterText
    {
        get => _filterText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_filterText, value, StringComparison.Ordinal)) return;
            _filterText = value;
            Notify();
            Invalidate();
        }
    }

    public UiChoice<HistoryScope> SelectedScope
    {
        get => _selectedScope;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_selectedScope == value) return;

            _selectedScope = value;
            Notify();
            if (value.Value != HistoryScope.AllReferences)
                SetShowReflogCore(false, persist: true);

            _ = RefreshAsync();
        }
    }

    public HistoryRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value)) return;
            var previous = _selectedRow;
            _selectedRow = value;
            Notify();
            SelectedRowChanged?.Invoke(previous);
        }
    }

    public bool ShowReflog
    {
        get => _showReflog;
        set
        {
            if (_showReflog == value) return;

            if (value && _selectedScope != Scopes[0])
            {
                _selectedScope = Scopes[0];
                Notify(nameof(SelectedScope));
            }

            SetShowReflogCore(value, persist: true);
            _ = RefreshAsync();
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
        _showReflog
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
        Invalidate();
        SetReferenceScope(null, null);
        _rows.ReplaceAll([]);
        HasMore = false;
        SelectedRow = null;
        RefreshAvailability();
    }

    internal void RefreshAvailability()
    {
        ((AsyncCommand)RefreshCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)LoadMoreCommand).RaiseCanExecuteChanged();
    }

    internal void SetHistoryDisplayMode(HistoryDisplayMode mode)
    {
        var (scope, showReflog) = mode switch
        {
            HistoryDisplayMode.CurrentBranch => (Scopes[1], false),
            HistoryDisplayMode.AllReferences => (Scopes[0], false),
            HistoryDisplayMode.AllReferencesWithReflog => (Scopes[0], true),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

        var scopeChanged = _selectedScope != scope;
        var reflogChanged = _showReflog != showReflog;
        if (!scopeChanged && !reflogChanged && !IsReferenceScoped) return;

        if (IsReferenceScoped)
            SetReferenceScope(null, null);

        _selectedScope = scope;
        _showReflog = showReflog;

        if (scopeChanged) Notify(nameof(SelectedScope));
        if (reflogChanged)
        {
            Notify(nameof(ShowReflog));
            _ = PersistShowReflogAsync(showReflog);
        }

        _ = RefreshAsync();
    }

    public Task RefreshAsync() => LoadAsync(reset: true);

    public Task LoadMoreAsync()
    {
        if (!HasMore || IsLoading) return Task.CompletedTask;
        return LoadAsync(reset: false);
    }

    public async Task ShowReferenceAsync(string reference, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        if (_showReflog)
        {
            _showReflog = false;
            Notify(nameof(ShowReflog));
            await PersistShowReflogAsync(false);
        }

        SetReferenceScope(reference, label);
        await LoadAsync(reset: true);
    }

    public async Task ShowAllAsync()
    {
        if (!IsReferenceScoped && _selectedScope == Scopes[0])
        {
            await RefreshAsync();
            return;
        }

        SetReferenceScope(null, null);
        if (_selectedScope != Scopes[0])
        {
            _selectedScope = Scopes[0];
            Notify(nameof(SelectedScope));
        }

        await LoadAsync(reset: true);
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

        if (IsReferenceScoped)
            SetReferenceScope(null, null);

        if (_selectedScope != Scopes[0])
        {
            _selectedScope = Scopes[0];
            Notify(nameof(SelectedScope));
        }

        if (!string.IsNullOrEmpty(_filterText))
        {
            _filterText = string.Empty;
            Notify(nameof(FilterText));
        }

        if (!requiresReload
            && _rows.FirstOrDefault(row =>
                string.Equals(row.Commit.Hash, commitHash, StringComparison.Ordinal)) is { } existing)
        {
            SelectedRow = existing;
            return existing;
        }

        var request = BeginRequest(repository);
        _context.EnterHistoryBusy();
        IsLoading = true;
        try
        {
            var reflogSessionId = Interlocked.Increment(ref _reflogSessionId);
            var query = CreateHistoryQuery(
                HistoryScope.AllReferences,
                filter: null,
                skip: 0,
                reflogSessionId,
                reference: null,
                includeReflog: _showReflog);

            var page = await _historyService.ReadHistoryThroughCommitAsync(
                repository,
                query,
                commitHash,
                trailingCount,
                request.Cancellation.Token);

            if (!CanPublish(request)) return null;

            _rows.ReplaceAll(page.Rows);
            HasMore = page.HasMore;

            var target = _rows.FirstOrDefault(row =>
                string.Equals(row.Commit.Hash, commitHash, StringComparison.Ordinal));
            if (target is null)
                throw new InvalidOperationException(
                    $"Commit {commitHash} was not present after history navigation load.");

            SelectedRow = target;
            return target;
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (CanReport(request))
            {
                _context.ReportHistoryError($"Could not navigate to commit: {exception.Message}");
                _logger.LogWarning(
                    exception,
                    "History reference navigation failed for {CommitHash}",
                    commitHash);
            }

            return null;
        }
        finally
        {
            CompleteRequest(request);
        }
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _loadGeneration);
        var cancellation = Interlocked.Exchange(ref _loadCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        if (IsLoading)
            IsLoading = false;
    }

    internal void ResetForRepositoryMutation()
    {
        Invalidate();
        SetReferenceScope(null, null);
        _rows.ReplaceAll([]);
        HasMore = false;
        SelectedRow = null;
    }

    internal void HandleReferenceDeleted(string reference)
    {
        if (!string.Equals(_activeReference, reference, StringComparison.Ordinal))
            return;

        Invalidate();
        SetReferenceScope(null, null);
        if (_selectedScope != Scopes[0])
        {
            _selectedScope = Scopes[0];
            Notify(nameof(SelectedScope));
        }
    }

    internal void HandleReferenceRenamed(
        string oldReference,
        string newReference,
        string label)
    {
        if (!string.Equals(_activeReference, oldReference, StringComparison.Ordinal))
            return;

        Invalidate();
        SetReferenceScope(newReference, label);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _settings.Changed -= AppSettings_Changed;
        Invalidate();
        _context = null;
        SelectedRowChanged = null;
    }

    private async Task LoadAsync(bool reset)
    {
        if (_context?.Repository is not { } repository) return;
        if (!reset && (!HasMore || IsLoading)) return;

        var selectedHash = reset ? SelectedRow?.Commit.Hash : null;
        var skip = reset ? 0 : _rows.Count;
        var reflogSessionId = reset
            ? Interlocked.Increment(ref _reflogSessionId)
            : Volatile.Read(ref _reflogSessionId);
        var scope = IsReferenceScoped ? HistoryScope.CurrentBranch : _selectedScope.Value;
        var filter = _filterText;
        var reference = _activeReference;
        var includeReflog = !IsReferenceScoped && _showReflog;
        var query = CreateHistoryQuery(
            scope,
            filter,
            skip,
            reflogSessionId,
            reference,
            includeReflog);

        var request = BeginRequest(repository);
        _context.EnterHistoryBusy();
        IsLoading = true;
        try
        {
            var page = await _historyService.ReadHistoryAsync(
                repository,
                query,
                request.Cancellation.Token);

            if (!CanPublish(request)) return;

            if (reset)
                _rows.ReplaceAll(page.Rows);
            else
                foreach (var row in page.Rows)
                    _rows.Add(row);

            HasMore = page.HasMore;

            if (reset)
            {
                if (_context.CanUpdateHistorySelection)
                {
                    SelectedRow = selectedHash is null
                        ? _rows.FirstOrDefault()
                        : _rows.FirstOrDefault(row =>
                            string.Equals(row.Commit.Hash, selectedHash, StringComparison.Ordinal))
                          ?? _rows.FirstOrDefault();
                }
            }
            else if (_context.CanUpdateHistorySelection
                     && SelectedRow is null
                     && _rows.FirstOrDefault() is { } first)
            {
                SelectedRow = first;
            }
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (CanReport(request))
            {
                var prefix = IsReferenceScoped
                    ? "Could not read the selected reference history"
                    : "Could not read history";
                _context.ReportHistoryError($"{prefix}: {exception.Message}");
                _logger.LogWarning(exception, "History loading failed");
            }
        }
        finally
        {
            CompleteRequest(request);
        }
    }

    private HistoryQuery CreateHistoryQuery(
        HistoryScope scope,
        string? filter,
        int skip,
        long reflogSessionId,
        string? reference,
        bool includeReflog)
    {
        if (_context is null)
            throw new InvalidOperationException("HistoryViewModel is not attached.");

        if (reference is not null)
        {
            return new HistoryQuery(
                HistoryScope.CurrentBranch,
                filter,
                skip,
                100,
                Reference: reference);
        }

        return new HistoryQuery(
            scope,
            filter,
            skip,
            IncludeReflog: includeReflog,
            HeadExists: _context.HeadExists,
            RepositoryReferences: new GitReferences(
                _context.LocalBranches.ToArray(),
                _context.RemoteBranches.ToArray(),
                _context.Remotes.ToArray(),
                _context.Tags.ToArray()),
            HeadReference: _context.HeadReference,
            HeadCommit: _context.HeadCommit,
            IsDetachedHead: _context.IsDetachedHead,
            ReflogSessionId: reflogSessionId);
    }

    private HistoryLoadRequest BeginRequest(Repository repository)
    {
        var generation = Interlocked.Increment(ref _loadGeneration);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, cancellation);
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        return new HistoryLoadRequest(repository, generation, cancellation);
    }

    private bool CanPublish(HistoryLoadRequest request) =>
        Volatile.Read(ref _disposed) == 0
        && !request.Cancellation.IsCancellationRequested
        && request.Generation == Volatile.Read(ref _loadGeneration)
        && ReferenceEquals(request.Repository, _context?.Repository);

    private bool CanReport(HistoryLoadRequest request) =>
        Volatile.Read(ref _disposed) == 0
        && request.Generation == Volatile.Read(ref _loadGeneration)
        && ReferenceEquals(request.Repository, _context?.Repository);

    private void CompleteRequest(HistoryLoadRequest request)
    {
        _context?.ExitHistoryBusy();

        if (request.Generation == Volatile.Read(ref _loadGeneration))
        {
            IsLoading = false;
            if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, request.Cancellation), request.Cancellation))
                request.Cancellation.Dispose();
        }
    }

    private void SetReferenceScope(string? reference, string? label)
    {
        var referenceChanged = !string.Equals(_activeReference, reference, StringComparison.Ordinal);
        var labelChanged = !string.Equals(_activeReferenceLabel, label, StringComparison.Ordinal);
        if (!referenceChanged && !labelChanged) return;

        _activeReference = reference;
        _activeReferenceLabel = label;
        if (referenceChanged)
        {
            Notify(nameof(ActiveReference));
            Notify(nameof(IsReferenceScoped));
        }

        if (labelChanged)
            Notify(nameof(ActiveReferenceLabel));
    }

    private void SetShowReflogCore(bool value, bool persist)
    {
        if (_showReflog == value) return;
        _showReflog = value;
        Notify(nameof(ShowReflog));
        if (persist)
            _ = PersistShowReflogAsync(value);
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
            _logger.LogWarning(exception, "Show reflog setting persistence failed");
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
        var value = _settings.ShowReflog;
        if (IsReferenceScoped && value)
        {
            _ = PersistShowReflogAsync(false);
            return;
        }

        if (_showReflog == value) return;

        _showReflog = value;
        if (value && _selectedScope != Scopes[0])
        {
            _selectedScope = Scopes[0];
            Notify(nameof(SelectedScope));
        }

        Notify(nameof(ShowReflog));
        _ = RefreshAsync();
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record HistoryLoadRequest(
        Repository Repository,
        long Generation,
        CancellationTokenSource Cancellation);
}
