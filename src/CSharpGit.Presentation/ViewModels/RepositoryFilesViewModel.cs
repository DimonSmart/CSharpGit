using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public interface IRepositoryFilesContext : INotifyPropertyChanged
{
    Repository? Repository { get; }
    string? SelectedObjectCommit { get; }
    bool HasSelectedStash { get; }
}

public sealed record RepositoryContentSearchRow(RepositoryContentSearchMatch Match)
{
    public override string ToString() => $"{Match.Path}\n  {Match.LineNumber}  {Match.Snippet}";
}

public sealed class RepositoryFilesViewModel : INotifyPropertyChanged, IDisposable
{
    public const string NameSearchMode = "Name";
    public const string ContentSearchMode = "Content";
    public const int ContentResultLimit = 500;
    private const int PresentationStateLimit = 12;

    private readonly IRepositorySnapshotService _repositorySnapshotService;
    private readonly RepositorySnapshotCache _snapshotCache = new();
    private readonly Dictionary<string, RepositoryFilesPresentationState> _states = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _stateLru = [];
    private readonly Dictionary<string, LinkedListNode<string>> _stateLruNodes = new(StringComparer.Ordinal);
    private IRepositoryFilesContext? _context;
    private CancellationTokenSource? _snapshotCts;
    private CancellationTokenSource? _contentSearchCts;
    private IReadOnlyList<RepositorySnapshotEntry> _snapshot = [];
    private IReadOnlyList<RepositoryContentSearchRow> _contentResults = [];
    private Repository? _snapshotRepository;
    private string? _snapshotCommit;
    private string? _contentCommit;
    private string? _lastSelectedPath;
    private string _nameQuery = string.Empty;
    private string _searchMode = NameSearchMode;
    private string _statusText = string.Empty;
    private bool _snapshotLoadedSuccessfully;
    private bool _treeSearchActive;
    private bool _isActive;
    private bool _isLoading;
    private bool _isRebuildingTree;
    private string? _errorMessage;
    private RepositorySnapshotTreeNode? _selectedTreeNode;
    private string? _selectedPath;
    private int? _selectedLineNumber;
    private RepositorySnapshotEntry? _selectedEntry;
    private long _snapshotGeneration;
    private long _contentSearchGeneration;
    private long _treeRevision;
    private long _selectionRevision;
    private int _disposed;

    public RepositoryFilesViewModel(IRepositorySnapshotService repositorySnapshotService)
    {
        _repositorySnapshotService = repositorySnapshotService
            ?? throw new ArgumentNullException(nameof(repositorySnapshotService));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RepositorySnapshotTreeNode> TreeRoots { get; } = [];
    public IReadOnlyList<RepositorySnapshotEntry> Snapshot => _snapshot;
    public IReadOnlyList<RepositoryContentSearchRow> ContentResults => _contentResults;
    public Repository? CurrentRepository => _snapshotRepository;
    public string? CurrentCommit => _snapshotCommit;
    public string NameQuery => _nameQuery;
    public string SearchMode => _searchMode;
    public bool IsContentSearchMode => string.Equals(_searchMode, ContentSearchMode, StringComparison.Ordinal);
    public string StatusText => _statusText;
    public string? ErrorMessage => _errorMessage;
    public bool IsLoading => _isLoading;
    public bool IsActive => _isActive;
    public bool IsRebuildingTree => _isRebuildingTree;
    public long TreeRevision => _treeRevision;
    public long SelectionRevision => _selectionRevision;
    public RepositorySnapshotTreeNode? SelectedTreeNode => _selectedTreeNode;
    public string? SelectedPath => _selectedPath;
    public int? SelectedLineNumber => _selectedLineNumber;
    public RepositorySnapshotEntry? SelectedEntry => _selectedEntry;

    public bool SnapshotMatchesSelection =>
        _context is not null
        && ReferenceEquals(_snapshotRepository, _context.Repository)
        && string.Equals(_snapshotCommit, _context.SelectedObjectCommit, StringComparison.Ordinal);

    internal void Attach(IRepositoryFilesContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (ReferenceEquals(_context, context)) return;

        Detach();
        _context = context;
        _context.PropertyChanged += Context_PropertyChanged;
    }

    internal void Detach()
    {
        if (_context is not null)
            _context.PropertyChanged -= Context_PropertyChanged;
        _context = null;
        _isActive = false;
        CancelRequests();
        ResetFeatureState(clearCache: true);
    }

    public async Task SetActiveAsync(bool active)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_isActive == active) return;

        _isActive = active;
        Notify(nameof(IsActive));
        if (!active)
        {
            SavePresentationState();
            CancelRequests();
            SetLoading(false);
            return;
        }

        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (!_isActive || Volatile.Read(ref _disposed) != 0) return;

        var context = _context;
        var repository = context?.Repository;
        var commitHash = context?.SelectedObjectCommit;
        if (repository is null || string.IsNullOrWhiteSpace(commitHash))
        {
            CancelRequests();
            _snapshot = [];
            _snapshotRepository = null;
            _snapshotCommit = null;
            _snapshotLoadedSuccessfully = false;
            SetContentResults([], null);
            SetSelectionCore(null, null, null, null);
            ClearTree();
            SetStatus("Select a commit or stash.", loading: false);
            return;
        }

        var contextChanged = !ReferenceEquals(repository, _snapshotRepository)
                             || !string.Equals(commitHash, _snapshotCommit, StringComparison.Ordinal);
        if (!contextChanged && _snapshotLoadedSuccessfully)
        {
            PublishSnapshot(repository, commitHash, _snapshot);
            return;
        }

        SavePresentationState();
        CancelSnapshotRequest();
        CancelContentSearch();
        if (contextChanged)
            SetSelectionCore(null, null, null, null);

        var generation = Interlocked.Increment(ref _snapshotGeneration);
        var cancellation = new CancellationTokenSource();
        _snapshotCts = cancellation;
        var repositoryIdentity = RepositoryIdentity(repository);
        SetContentResults([], null);

        if (_snapshotCache.TryGet(repositoryIdentity, commitHash, out var cached))
        {
            if (CanPublishSnapshot(repository, commitHash, generation, cancellation))
                PublishSnapshot(repository, commitHash, cached);
            return;
        }

        SetStatus("Loading repository files...", loading: true);
        try
        {
            var snapshot = await _repositorySnapshotService.ReadTreeAsync(
                repository,
                commitHash,
                cancellation.Token);
            if (!CanPublishSnapshot(repository, commitHash, generation, cancellation)) return;

            _snapshotCache.Set(repositoryIdentity, commitHash, snapshot);
            PublishSnapshot(repository, commitHash, snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!CanPublishSnapshot(repository, commitHash, generation, cancellation)) return;

            _snapshot = [];
            _snapshotRepository = repository;
            _snapshotCommit = commitHash;
            _snapshotLoadedSuccessfully = false;
            SetContentResults([], null);
            SetSelectionCore(null, null, null, null);
            ClearTree();
            SetStatus(
                $"Could not load repository files: {exception.Message}",
                loading: false,
                errorMessage: exception.Message);
        }
    }

    public void SetNameQuery(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!string.Equals(_searchMode, NameSearchMode, StringComparison.Ordinal)) return;
        if (string.Equals(_nameQuery, query, StringComparison.Ordinal)) return;

        _nameQuery = query;
        if (CurrentState(create: true) is { } state)
            state.NameQuery = query;
        Notify(nameof(NameQuery));
        if (_snapshotCommit is not null)
            RebuildTree();
    }

    public void SetSearchMode(string mode, string currentSearchText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        ArgumentNullException.ThrowIfNull(currentSearchText);
        if (mode is not NameSearchMode and not ContentSearchMode)
            throw new ArgumentOutOfRangeException(nameof(mode));

        if (string.Equals(_searchMode, mode, StringComparison.Ordinal)) return;

        _searchMode = mode;
        if (CurrentState(create: true) is { } state)
            state.SearchMode = mode;
        Notify(nameof(SearchMode));
        Notify(nameof(IsContentSearchMode));

        if (string.Equals(mode, NameSearchMode, StringComparison.Ordinal))
        {
            _nameQuery = currentSearchText;
            if (CurrentState(create: true) is { } nameState)
                nameState.NameQuery = currentSearchText;
            Notify(nameof(NameQuery));
            CancelContentSearch();
            RebuildTree();
            return;
        }

        CancelContentSearch();
        SetContentResults([], null);
        SetStatus("Press Enter to search file contents.", loading: false);
    }

    public async Task SearchContentAsync(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!_isActive || !IsContentSearchMode) return;

        if (string.IsNullOrWhiteSpace(query))
        {
            CancelContentSearch();
            SetContentResults([], null);
            SetStatus("Enter text and press Enter to search file contents.", loading: false);
            return;
        }

        var context = _context;
        var repository = context?.Repository;
        var commitHash = context?.SelectedObjectCommit;
        if (repository is null || string.IsNullOrWhiteSpace(commitHash) || !SnapshotMatchesSelection)
            return;

        CancelContentSearch();
        var generation = Interlocked.Increment(ref _contentSearchGeneration);
        var cancellation = new CancellationTokenSource();
        _contentSearchCts = cancellation;
        SetContentResults([], null);
        SetStatus("Searching file contents...", loading: true);

        try
        {
            var rawMatches = await _repositorySnapshotService.SearchContentAsync(
                repository,
                commitHash,
                query,
                cancellation.Token);
            if (!CanPublishContent(repository, commitHash, generation, cancellation)) return;

            var regularPaths = _snapshot
                .Where(entry => entry.Kind == RepositorySnapshotEntryKind.File)
                .Select(entry => entry.Path)
                .ToHashSet(StringComparer.Ordinal);
            var filteredMatches = rawMatches
                .Where(match => regularPaths.Contains(match.Path))
                .ToList();
            var rows = filteredMatches
                .Take(ContentResultLimit)
                .Select(match => new RepositoryContentSearchRow(match))
                .ToList();
            SetContentResults(rows, commitHash);
            SetStatus(filteredMatches.Count switch
            {
                0 => "No matches",
                > ContentResultLimit => $"Showing first {ContentResultLimit} of {filteredMatches.Count} matches.",
                _ => string.Empty
            }, loading: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!CanPublishContent(repository, commitHash, generation, cancellation)) return;
            SetContentResults([], commitHash);
            SetStatus(
                $"Content search failed: {exception.Message}",
                loading: false,
                errorMessage: exception.Message);
        }
    }

    public void SelectTreeNode(RepositorySnapshotTreeNode? node)
    {
        if (_isRebuildingTree || !SnapshotMatchesSelection)
        {
            if (!SnapshotMatchesSelection)
                SetSelectionCore(null, null, null, null);
            return;
        }

        if (node is null)
        {
            if (CurrentState(create: true) is { } emptyState)
                emptyState.SelectedPath = null;
            _lastSelectedPath = null;
            SetSelectionCore(null, null, null, null);
            return;
        }

        if (CurrentState(create: true) is { } state)
            state.SelectedPath = node.Path;
        _lastSelectedPath = node.Entry is null ? null : node.Path;
        SetSelectionCore(node, node.Path, null, node.Entry);
    }

    public RepositorySnapshotEntry? SelectContentResult(RepositoryContentSearchRow? row)
    {
        if (row is null
            || !SnapshotMatchesSelection
            || !string.Equals(_contentCommit, _context?.SelectedObjectCommit, StringComparison.Ordinal))
            return null;

        var entry = _snapshot.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, row.Match.Path, StringComparison.Ordinal));
        if (entry is not { Kind: RepositorySnapshotEntryKind.File }) return null;

        if (CurrentState(create: true) is { } state)
            state.SelectedPath = entry.Path;
        _lastSelectedPath = entry.Path;
        var node = FindPath(TreeRoots, entry.Path);
        SetSelectionCore(node, entry.Path, row.Match.LineNumber, entry);
        return entry;
    }

    public RepositorySnapshotEntry? FindEntry(string path) =>
        _snapshot.FirstOrDefault(candidate => string.Equals(candidate.Path, path, StringComparison.Ordinal));

    public void SetExpanded(RepositorySnapshotTreeNode node, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.IsExpanded = expanded;
        if (_isRebuildingTree
            || !string.IsNullOrWhiteSpace(_nameQuery)
            || !node.IsDirectory)
        {
            return;
        }

        if (CurrentState(create: true) is not { } state) return;
        if (expanded)
            state.ExpandedPaths.Add(node.Path);
        else
            state.ExpandedPaths.Remove(node.Path);
    }

    public async Task<DiffFileVersion?> ResolveFileVersionAsync(
        RepositorySnapshotEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var repository = _snapshotRepository;
        var commitHash = _snapshotCommit;
        if (repository is null || string.IsNullOrWhiteSpace(commitHash) || !SnapshotMatchesSelection)
            return null;

        var version = await _repositorySnapshotService.ResolveFileVersionAsync(
            repository,
            commitHash,
            entry.Path,
            cancellationToken);
        return SnapshotMatchesSelection
               && ReferenceEquals(repository, _snapshotRepository)
               && string.Equals(commitHash, _snapshotCommit, StringComparison.Ordinal)
            ? version
            : null;
    }

    public void Invalidate(string statusText, bool loading)
    {
        ArgumentNullException.ThrowIfNull(statusText);
        SavePresentationState();
        CancelRequests();
        ResetFeatureState(clearCache: true);
        SetStatus(statusText, loading);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Detach();
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        if (args.PropertyName == nameof(IRepositoryFilesContext.Repository))
        {
            SavePresentationState();
            CancelRequests();
            ResetFeatureState(clearCache: true);
            if (_isActive) _ = RefreshAsync();
            return;
        }

        if (args.PropertyName is nameof(IRepositoryFilesContext.SelectedObjectCommit)
            or nameof(IRepositoryFilesContext.HasSelectedStash))
        {
            if (_isActive) _ = RefreshAsync();
        }
    }

    private bool CanPublishSnapshot(
        Repository repository,
        string commitHash,
        long generation,
        CancellationTokenSource cancellation) =>
        !cancellation.IsCancellationRequested
        && ReferenceEquals(_snapshotCts, cancellation)
        && generation == Volatile.Read(ref _snapshotGeneration)
        && _isActive
        && ReferenceEquals(repository, _context?.Repository)
        && string.Equals(commitHash, _context?.SelectedObjectCommit, StringComparison.Ordinal);

    private bool CanPublishContent(
        Repository repository,
        string commitHash,
        long generation,
        CancellationTokenSource cancellation) =>
        !cancellation.IsCancellationRequested
        && ReferenceEquals(_contentSearchCts, cancellation)
        && generation == Volatile.Read(ref _contentSearchGeneration)
        && _isActive
        && IsContentSearchMode
        && ReferenceEquals(repository, _context?.Repository)
        && string.Equals(commitHash, _context?.SelectedObjectCommit, StringComparison.Ordinal);

    private void PublishSnapshot(
        Repository repository,
        string commitHash,
        IReadOnlyList<RepositorySnapshotEntry> snapshot)
    {
        var restoreSavedExpansionState = _states.ContainsKey(StateKey(repository, commitHash));
        _snapshotRepository = repository;
        _snapshotCommit = commitHash;
        _snapshot = snapshot;
        _snapshotLoadedSuccessfully = true;
        Notify(nameof(Snapshot));
        Notify(nameof(CurrentRepository));
        Notify(nameof(CurrentCommit));
        Notify(nameof(SnapshotMatchesSelection));

        RestorePresentationState(repository, commitHash);
        RebuildTree(restoreSavedExpansionState);
        if (IsContentSearchMode)
            SetStatus("Press Enter to search file contents.", loading: false);
        else if (string.IsNullOrWhiteSpace(_nameQuery))
            SetStatus(ReadyStatus(snapshot.Count), loading: false);
    }

    private string ReadyStatus(int entryCount)
    {
        if (_context?.HasSelectedStash == true)
        {
            return entryCount == 0
                ? "The tracked repository snapshot saved in this stash is empty. Untracked stash files are shown in Changes."
                : "Tracked repository snapshot saved in this stash. Untracked stash files are shown in Changes.";
        }

        return entryCount == 0 ? "This commit has an empty tree." : string.Empty;
    }

    private void RebuildTree(bool restoreNormalExpansionState = false)
    {
        _isRebuildingTree = true;
        Notify(nameof(IsRebuildingTree));
        try
        {
            var wasSearchActive = _treeSearchActive;
            var query = string.Equals(_searchMode, NameSearchMode, StringComparison.Ordinal)
                ? _nameQuery
                : null;
            RepositorySnapshotTreeSynchronizer.Reconcile(TreeRoots, _snapshot, query);

            var searchActive = !string.IsNullOrWhiteSpace(query);
            var state = CurrentState(create: true);
            if (searchActive || restoreNormalExpansionState || wasSearchActive)
            {
                foreach (var root in TreeRoots)
                    RestoreExpansion(root, searchActive, state);
            }

            if (!searchActive && state is not null)
                CaptureExpansionState(TreeRoots, state);
            _treeSearchActive = searchActive;

            var selected = state?.SelectedPath is { } selectedPath
                ? FindPath(TreeRoots, selectedPath)
                : null;
            SetSelectionCore(
                selected,
                selected?.Path,
                null,
                selected?.Entry);

            if (searchActive && TreeRoots.Count == 0 && _snapshot.Count > 0)
                SetStatus("No matches", loading: false);
            else if (_snapshot.Count > 0 && string.Equals(_searchMode, NameSearchMode, StringComparison.Ordinal))
                SetStatus(ReadyStatus(_snapshot.Count), loading: false);
        }
        finally
        {
            _isRebuildingTree = false;
            Notify(nameof(IsRebuildingTree));
            _treeRevision++;
            Notify(nameof(TreeRevision));
        }
    }

    private static void RestoreExpansion(
        RepositorySnapshotTreeNode node,
        bool searchActive,
        RepositoryFilesPresentationState? state)
    {
        node.IsExpanded = node.IsDirectory
            && (searchActive || state?.ExpandedPaths.Contains(node.Path) == true);
        foreach (var child in node.Children)
            RestoreExpansion(child, searchActive, state);
    }

    private static void CaptureExpansionState(
        IEnumerable<RepositorySnapshotTreeNode> nodes,
        RepositoryFilesPresentationState state)
    {
        state.ExpandedPaths.Clear();
        CaptureExpandedPaths(nodes, state.ExpandedPaths);
    }

    private static void CaptureExpandedPaths(
        IEnumerable<RepositorySnapshotTreeNode> nodes,
        ISet<string> expandedPaths)
    {
        foreach (var node in nodes)
        {
            if (node.IsDirectory && node.IsExpanded)
                expandedPaths.Add(node.Path);
            CaptureExpandedPaths(node.Children, expandedPaths);
        }
    }

    private static RepositorySnapshotTreeNode? FindPath(
        IEnumerable<RepositorySnapshotTreeNode> nodes,
        string path)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Path, path, StringComparison.Ordinal))
                return node;
            if (FindPath(node.Children, path) is { } match)
                return match;
        }

        return null;
    }

    private void SavePresentationState()
    {
        if (_snapshotRepository is null || _snapshotCommit is null) return;
        var state = CurrentState(create: true);
        if (state is null) return;

        state.NameQuery = _nameQuery;
        state.SearchMode = _searchMode;
        state.SelectedPath = _selectedPath;
        if (!_treeSearchActive)
            CaptureExpansionState(TreeRoots, state);
        _lastSelectedPath = state.SelectedPath;
        TouchState(StateKey(_snapshotRepository, _snapshotCommit));
    }

    private void RestorePresentationState(Repository repository, string commitHash)
    {
        var key = StateKey(repository, commitHash);
        if (!_states.TryGetValue(key, out var state))
        {
            var selectedPath = _lastSelectedPath is { } previousPath
                && _snapshot.Any(entry => string.Equals(entry.Path, previousPath, StringComparison.Ordinal))
                    ? previousPath
                    : null;
            state = new RepositoryFilesPresentationState
            {
                NameQuery = _nameQuery,
                SearchMode = _searchMode,
                SelectedPath = selectedPath
            };
            _states[key] = state;
        }

        TouchState(key);
        if (!string.Equals(_nameQuery, state.NameQuery, StringComparison.Ordinal))
        {
            _nameQuery = state.NameQuery;
            Notify(nameof(NameQuery));
        }

        if (!string.Equals(_searchMode, state.SearchMode, StringComparison.Ordinal))
        {
            _searchMode = state.SearchMode;
            Notify(nameof(SearchMode));
            Notify(nameof(IsContentSearchMode));
        }
    }

    private RepositoryFilesPresentationState? CurrentState(bool create)
    {
        if (_snapshotRepository is null || _snapshotCommit is null) return null;
        var key = StateKey(_snapshotRepository, _snapshotCommit);
        if (!_states.TryGetValue(key, out var state) && create)
        {
            state = new RepositoryFilesPresentationState
            {
                NameQuery = _nameQuery,
                SearchMode = _searchMode,
                SelectedPath = _selectedPath ?? _lastSelectedPath
            };
            _states[key] = state;
        }

        if (state is not null)
            TouchState(key);
        return state;
    }

    private void TouchState(string key)
    {
        if (_stateLruNodes.Remove(key, out var existing))
            _stateLru.Remove(existing);

        var node = _stateLru.AddFirst(key);
        _stateLruNodes[key] = node;

        while (_states.Count > PresentationStateLimit && _stateLru.Last is { } oldest)
        {
            _stateLru.RemoveLast();
            _stateLruNodes.Remove(oldest.Value);
            _states.Remove(oldest.Value);
        }
    }

    private void SetContentResults(
        IReadOnlyList<RepositoryContentSearchRow> results,
        string? commitHash)
    {
        _contentResults = results;
        _contentCommit = commitHash;
        Notify(nameof(ContentResults));
    }

    private void SetSelectionCore(
        RepositorySnapshotTreeNode? treeNode,
        string? path,
        int? lineNumber,
        RepositorySnapshotEntry? entry)
    {
        if (ReferenceEquals(_selectedTreeNode, treeNode)
            && string.Equals(_selectedPath, path, StringComparison.Ordinal)
            && _selectedLineNumber == lineNumber
            && Equals(_selectedEntry, entry))
        {
            return;
        }

        _selectedTreeNode = treeNode;
        _selectedPath = path;
        _selectedLineNumber = lineNumber;
        _selectedEntry = entry;
        Notify(nameof(SelectedTreeNode));
        Notify(nameof(SelectedPath));
        Notify(nameof(SelectedLineNumber));
        Notify(nameof(SelectedEntry));
        _selectionRevision++;
        Notify(nameof(SelectionRevision));
    }

    private void ClearTree()
    {
        _isRebuildingTree = true;
        try
        {
            TreeRoots.Clear();
            _treeSearchActive = false;
        }
        finally
        {
            _isRebuildingTree = false;
            _treeRevision++;
            Notify(nameof(TreeRevision));
        }
    }

    private void ResetFeatureState(bool clearCache)
    {
        if (clearCache)
            _snapshotCache.Clear();
        _states.Clear();
        _stateLru.Clear();
        _stateLruNodes.Clear();
        _snapshot = [];
        _snapshotRepository = null;
        _snapshotCommit = null;
        _snapshotLoadedSuccessfully = false;
        _contentCommit = null;
        _lastSelectedPath = null;
        _nameQuery = string.Empty;
        _searchMode = NameSearchMode;
        SetContentResults([], null);
        SetSelectionCore(null, null, null, null);
        ClearTree();
        SetStatus(string.Empty, loading: false);
        Notify(nameof(NameQuery));
        Notify(nameof(SearchMode));
        Notify(nameof(IsContentSearchMode));
        Notify(nameof(Snapshot));
        Notify(nameof(CurrentRepository));
        Notify(nameof(CurrentCommit));
        Notify(nameof(SnapshotMatchesSelection));
    }

    private void CancelRequests()
    {
        CancelSnapshotRequest();
        CancelContentSearch();
    }

    private void CancelSnapshotRequest()
    {
        Interlocked.Increment(ref _snapshotGeneration);
        var cancellation = Interlocked.Exchange(ref _snapshotCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void CancelContentSearch()
    {
        Interlocked.Increment(ref _contentSearchGeneration);
        var cancellation = Interlocked.Exchange(ref _contentSearchCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void SetStatus(string text, bool loading, string? errorMessage = null)
    {
        if (!string.Equals(_statusText, text, StringComparison.Ordinal))
        {
            _statusText = text;
            Notify(nameof(StatusText));
        }

        if (!string.Equals(_errorMessage, errorMessage, StringComparison.Ordinal))
        {
            _errorMessage = errorMessage;
            Notify(nameof(ErrorMessage));
        }

        SetLoading(loading);
    }

    private void SetLoading(bool value)
    {
        if (_isLoading == value) return;
        _isLoading = value;
        Notify(nameof(IsLoading));
    }

    private static string RepositoryIdentity(Repository repository) =>
        Path.GetFullPath(repository.GitDirectory);

    private static string StateKey(Repository repository, string commitHash) =>
        RepositoryIdentity(repository) + "\n" + commitHash;

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class RepositoryFilesPresentationState
    {
        public HashSet<string> ExpandedPaths { get; } = new(StringComparer.Ordinal);
        public string? SelectedPath { get; set; }
        public string NameQuery { get; set; } = string.Empty;
        public string SearchMode { get; set; } = NameSearchMode;
    }
}
