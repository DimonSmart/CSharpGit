using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public interface IWorkingTreeRepositoryContext
{
    Repository? Repository { get; }
    bool IsBusy { get; }
    bool CanRunRepositoryMutation { get; }
    bool CanRunWorkingTreeMutation { get; }
    string? ErrorMessage { get; }

    Task<bool> RunWorkingTreeMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string errorContext,
        Action? beforeMutation = null);

    void ReportWorkingTreeError(string message);
}

public sealed class WorkingTreeViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IWorkingTreeService _workingTreeService;
    private readonly IWorkingTreeDiffService _workingTreeDiffService;
    private IWorkingTreeRepositoryContext? _context;
    private IReadOnlyList<WorkingTreeChange> _selectedUnstagedChanges = [];
    private IReadOnlyList<WorkingTreeChange> _selectedStagedChanges = [];
    private WorkingTreeChange? _selectedChange;
    private WorkingTreeDiffKind? _selectedDiffKind;
    private FileDiff? _selectedDiff;
    private WorkingTreeDiscardRequest? _pendingBatchDiscard;
    private CancellationTokenSource? _mutationCts;
    private CancellationTokenSource? _diffCts;
    private long _diffGeneration;
    private bool _isMutationBusy;
    private bool _isDiffLoading;
    private bool _isDiffPreviewDeferred;
    private string? _diffPreviewDeferredMessage;
    private string _diffPreviewActionText = "Load diff";
    private string? _diffLoadErrorMessage;
    private string? _mutationErrorMessage;
    private int _disposed;

    public WorkingTreeViewModel(
        IWorkingTreeService workingTreeService,
        IWorkingTreeDiffService workingTreeDiffService)
    {
        _workingTreeService = workingTreeService ?? throw new ArgumentNullException(nameof(workingTreeService));
        _workingTreeDiffService = workingTreeDiffService ?? throw new ArgumentNullException(nameof(workingTreeDiffService));

        StageCommand = new AsyncCommand(StageActiveAsync, CanStageActive);
        UnstageCommand = new AsyncCommand(UnstageActiveAsync, CanUnstageActive);
        StageSelectedCommand = new AsyncCommand(StageSelectedAsync, () => CanRunWorkingTreeMutation && _selectedUnstagedChanges.Count > 0);
        StageAllCommand = new AsyncCommand(StageAllAsync, () => CanRunWorkingTreeMutation && Changes.Any(change => change.IsUnstaged));
        UnstageSelectedCommand = new AsyncCommand(UnstageSelectedAsync, () => CanRunWorkingTreeMutation && _selectedStagedChanges.Count > 0);
        UnstageAllCommand = new AsyncCommand(UnstageAllAsync, () => CanRunWorkingTreeMutation && Changes.Any(change => change.IsStaged));
        RequestDiscardSelectedCommand = new AsyncCommand(RequestDiscardSelectedAsync, CanRequestDiscardSelected);
        RequestDiscardAllCommand = new AsyncCommand(RequestDiscardAllAsync, CanRequestDiscardAll);
        ConfirmBatchDiscardCommand = new AsyncCommand(ConfirmBatchDiscardAsync, () => CanRunRepositoryMutation && _pendingBatchDiscard is not null);
        CancelBatchDiscardCommand = new AsyncCommand(CancelBatchDiscardAsync, () => _pendingBatchDiscard is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<WorkingTreeChange> Changes { get; } = new BulkObservableCollection<WorkingTreeChange>();
    public IReadOnlyList<WorkingTreeChange> SelectedUnstagedChanges => _selectedUnstagedChanges;
    public IReadOnlyList<WorkingTreeChange> SelectedStagedChanges => _selectedStagedChanges;

    public WorkingTreeChange? SelectedChange
    {
        get => _selectedChange;
        private set
        {
            if (ReferenceEquals(_selectedChange, value)) return;
            _selectedChange = value;
            Notify();
            RefreshAvailability();
        }
    }

    public WorkingTreeDiffKind? SelectedDiffKind
    {
        get => _selectedDiffKind;
        private set
        {
            if (_selectedDiffKind == value) return;
            _selectedDiffKind = value;
            Notify();
            RefreshAvailability();
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
        }
    }

    public bool IsMutationBusy
    {
        get => _isMutationBusy;
        private set
        {
            if (_isMutationBusy == value) return;
            _isMutationBusy = value;
            Notify();
            RefreshAvailability();
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

    public string? MutationErrorMessage
    {
        get => _mutationErrorMessage;
        private set
        {
            if (string.Equals(_mutationErrorMessage, value, StringComparison.Ordinal)) return;
            _mutationErrorMessage = value;
            Notify();
        }
    }

    public string BatchDiscardConfirmationMessage =>
        _pendingBatchDiscard?.ConfirmationMessage ?? string.Empty;

    public ICommand StageCommand { get; }
    public ICommand UnstageCommand { get; }
    public ICommand StageSelectedCommand { get; }
    public ICommand StageAllCommand { get; }
    public ICommand UnstageSelectedCommand { get; }
    public ICommand UnstageAllCommand { get; }
    public ICommand RequestDiscardSelectedCommand { get; }
    public ICommand RequestDiscardAllCommand { get; }
    public ICommand ConfirmBatchDiscardCommand { get; }
    public ICommand CancelBatchDiscardCommand { get; }

    private bool CanRunRepositoryMutation =>
        !_isMutationBusy &&
        _context?.CanRunRepositoryMutation == true;

    private bool CanRunWorkingTreeMutation =>
        !_isMutationBusy &&
        _context?.CanRunWorkingTreeMutation == true;

    internal void Attach(IWorkingTreeRepositoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _context = context;
        RefreshAvailability();
    }

    internal void OnRepositoryChanged(Repository? repository)
    {
        CancelMutation();
        CancelDiff();
        Changes.Clear();
        _selectedUnstagedChanges = [];
        _selectedStagedChanges = [];
        SetPendingBatchDiscard(null);
        SelectedChange = null;
        SelectedDiffKind = null;
        MutationErrorMessage = null;
        Notify(nameof(SelectedUnstagedChanges));
        Notify(nameof(SelectedStagedChanges));
        RefreshAvailability();
    }

    internal void ApplyRepositoryState(
        Repository repository,
        IEnumerable<WorkingTreeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(changes);
        if (!ReferenceEquals(repository, _context?.Repository)) return;

        CancelDiff(preserveSelection: true);
        var snapshot = changes as IReadOnlyList<WorkingTreeChange> ?? changes.ToArray();
        ((BulkObservableCollection<WorkingTreeChange>)Changes).ReplaceAll(snapshot);
        ReconcileSelection();
        RefreshAvailability();
    }

    internal void ClearRepositoryState() => OnRepositoryChanged(_context?.Repository);

    internal void RefreshAvailability()
    {
        foreach (var command in WorkingTreeCommands())
            command.RaiseCanExecuteChanged();
    }

    public void SetSelection(
        WorkingTreeDiffKind kind,
        IEnumerable<WorkingTreeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var snapshot = changes.ToArray();
        if (kind == WorkingTreeDiffKind.Unstaged)
        {
            _selectedUnstagedChanges = snapshot;
            Notify(nameof(SelectedUnstagedChanges));
        }
        else
        {
            _selectedStagedChanges = snapshot;
            Notify(nameof(SelectedStagedChanges));
        }

        RefreshAvailability();
    }

    public async Task SelectChangeAsync(
        WorkingTreeChange change,
        WorkingTreeDiffKind kind)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (Volatile.Read(ref _disposed) != 0) return;

        CancelDiff();
        SelectedChange = change;
        SelectedDiffKind = kind;

        var repository = _context?.Repository;
        if (repository is null)
        {
            ClearDiffResult();
            return;
        }

        if (TryGetWorkingTreeFileSize(repository, change.Path, out var fileSize) &&
            fileSize >= DiffPreviewPolicy.LargeFileBytes)
        {
            SetDeferred(
                $"This file is {FormatDiffByteSize(fileSize)}. Generating and displaying its diff may take some time.",
                "Load diff");
            return;
        }

        await LoadDiffAsync(repository, change, kind, DiffLoadMode.Preview);
    }

    public Task LoadSelectedDiffAnywayAsync()
    {
        var repository = _context?.Repository;
        var change = SelectedChange;
        var kind = SelectedDiffKind;
        if (repository is null || change is null || kind is null)
            return Task.CompletedTask;

        CancelDiff(preserveSelection: true);
        return LoadDiffAsync(repository, change, kind.Value, DiffLoadMode.Full);
    }

    public void ClearActiveSelection()
    {
        CancelDiff();
        SelectedChange = null;
        SelectedDiffKind = null;
    }

    public void CancelDiff() => CancelDiff(preserveSelection: true);

    public bool HasCurrentDelta(string path, WorkingTreeDiffKind kind) =>
        Changes.Any(change =>
            (string.Equals(change.Path, path, StringComparison.Ordinal) ||
             string.Equals(change.OriginalPath, path, StringComparison.Ordinal)) &&
            (kind == WorkingTreeDiffKind.Unstaged ? change.IsUnstaged : change.IsStaged));

    public bool IsActive(WorkingTreeChange change, WorkingTreeDiffKind kind) =>
        SelectedDiffKind == kind &&
        SelectedChange is { } selected &&
        SameChange(selected, change);

    public bool CanStageChanges(IReadOnlyCollection<WorkingTreeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return CanRunWorkingTreeMutation && changes.Count > 0;
    }

    public bool CanUnstageChanges(IReadOnlyCollection<WorkingTreeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return CanRunWorkingTreeMutation && changes.Count > 0;
    }

    public Task StageChangesAsync(
        IReadOnlyCollection<WorkingTreeChange> changes,
        string errorContext)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (!CanStageChanges(changes)) return Task.CompletedTask;

        var snapshot = changes.ToArray();
        return RunMutationAsync(
            (repository, cancellationToken) =>
                _workingTreeService.StageFilesAsync(repository, snapshot, cancellationToken),
            errorContext,
            requireBulkEligibility: true);
    }

    public Task UnstageChangesAsync(
        IReadOnlyCollection<WorkingTreeChange> changes,
        string errorContext)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (!CanUnstageChanges(changes)) return Task.CompletedTask;

        var snapshot = changes.ToArray();
        return RunMutationAsync(
            (repository, cancellationToken) =>
                _workingTreeService.UnstageFilesAsync(repository, snapshot, cancellationToken),
            errorContext,
            requireBulkEligibility: true);
    }

    public bool CanDiscardAllFileChanges(WorkingTreeChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return _context?.Repository is not null &&
               _context.IsBusy == false &&
               !IsMutationBusy &&
               change.IsStaged &&
               !change.IsConflicted;
    }

    public Task DiscardAllFileChangesAsync(WorkingTreeChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!CanDiscardAllFileChanges(change)) return Task.CompletedTask;

        return RunMutationAsync(
            (repository, cancellationToken) =>
                _workingTreeService.DiscardAllFileChangesAsync(repository, change, cancellationToken),
            "Could not discard changes",
            requireBulkEligibility: false,
            beforeMutation: ClearPresentationSelection);
    }

    internal void ClearPresentationSelection()
    {
        _selectedUnstagedChanges = [];
        _selectedStagedChanges = [];
        Notify(nameof(SelectedUnstagedChanges));
        Notify(nameof(SelectedStagedChanges));
        ClearActiveSelection();
        RefreshAvailability();
    }

    internal void ClearCommittedPresentationSelection()
    {
        _selectedStagedChanges = [];
        Notify(nameof(SelectedStagedChanges));

        if (SelectedDiffKind == WorkingTreeDiffKind.Staged)
            ClearActiveSelection();

        RefreshAvailability();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        CancelMutation();
        CancelDiff();
        _context = null;
    }

    private bool CanStageActive() =>
        CanRunWorkingTreeMutation &&
        SelectedDiffKind == WorkingTreeDiffKind.Unstaged &&
        SelectedChange is { IsUnstaged: true };

    private bool CanUnstageActive() =>
        CanRunWorkingTreeMutation &&
        SelectedDiffKind == WorkingTreeDiffKind.Staged &&
        SelectedChange is { IsStaged: true };

    private Task StageActiveAsync()
    {
        var change = SelectedChange;
        if (change is null) return Task.CompletedTask;
        return RunMutationAsync(
            (repository, cancellationToken) =>
                _workingTreeService.StageFileAsync(repository, change, cancellationToken),
            "Could not stage file",
            requireBulkEligibility: true);
    }

    private Task UnstageActiveAsync()
    {
        var change = SelectedChange;
        if (change is null) return Task.CompletedTask;
        return RunMutationAsync(
            (repository, cancellationToken) =>
                _workingTreeService.UnstageFileAsync(repository, change, cancellationToken),
            "Could not unstage file",
            requireBulkEligibility: true);
    }

    private Task StageSelectedAsync() =>
        StageChangesAsync(_selectedUnstagedChanges, "Could not stage selected files");

    private Task UnstageSelectedAsync() =>
        UnstageChangesAsync(_selectedStagedChanges, "Could not unstage selected files");

    private Task StageAllAsync()
    {
        if (!CanRunWorkingTreeMutation) return Task.CompletedTask;
        return RunMutationAsync(
            (repository, cancellationToken) =>
                _workingTreeService.StageAllAsync(repository, cancellationToken),
            "Could not stage all files",
            requireBulkEligibility: true);
    }

    private Task UnstageAllAsync()
    {
        if (!CanRunWorkingTreeMutation) return Task.CompletedTask;
        return RunMutationAsync(
            (repository, cancellationToken) =>
                _workingTreeService.UnstageAllAsync(repository, cancellationToken),
            "Could not unstage all files",
            requireBulkEligibility: true);
    }

    private Task RequestDiscardSelectedAsync()
    {
        SetPendingBatchDiscard(WorkingTreeDiscard.CreateSelected(_selectedUnstagedChanges));
        return Task.CompletedTask;
    }

    private Task RequestDiscardAllAsync()
    {
        SetPendingBatchDiscard(WorkingTreeDiscard.CreateAll(Changes));
        return Task.CompletedTask;
    }

    private Task CancelBatchDiscardAsync()
    {
        SetPendingBatchDiscard(null);
        return Task.CompletedTask;
    }

    private bool CanRequestDiscardSelected() =>
        CanRunRepositoryMutation &&
        WorkingTreeDiscard.CanDiscardSelected(_selectedUnstagedChanges);

    private bool CanRequestDiscardAll() =>
        CanRunRepositoryMutation &&
        Changes.Any(WorkingTreeDiscard.IsEligible);

    private async Task ConfirmBatchDiscardAsync()
    {
        var request = _pendingBatchDiscard;
        SetPendingBatchDiscard(null);
        if (request is null) return;

        IReadOnlyList<WorkingTreeDiscardResult>? results = null;
        var succeeded = await RunMutationCoreAsync(
            async (repository, cancellationToken) =>
            {
                results = await WorkingTreeDiscard.ExecuteAsync(
                    repository,
                    request,
                    (changes, token) =>
                        _workingTreeService.DiscardTrackedFilesAsync(repository, changes, token),
                    (change, token) =>
                        _workingTreeService.DiscardFileAsync(repository, change, token),
                    cancellationToken);
            },
            "Could not discard changes",
            requireBulkEligibility: false,
            beforeMutation: ClearPresentationSelection);

        if (!succeeded || results is null) return;

        var failureMessage = WorkingTreeDiscard.FormatFailures(results);
        if (failureMessage.Length == 0) return;

        MutationErrorMessage = failureMessage;
        _context?.ReportWorkingTreeError(failureMessage);
    }

    private async Task RunMutationAsync(
        Func<Repository, CancellationToken, Task> mutation,
        string errorContext,
        bool requireBulkEligibility,
        Action? beforeMutation = null)
    {
        await RunMutationCoreAsync(mutation, errorContext, requireBulkEligibility, beforeMutation);
    }

    private async Task<bool> RunMutationCoreAsync(
        Func<Repository, CancellationToken, Task> mutation,
        string errorContext,
        bool requireBulkEligibility,
        Action? beforeMutation = null)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var context = _context;
        var repository = context?.Repository;
        if (context is null || repository is null) return false;

        var allowed = requireBulkEligibility
            ? CanRunWorkingTreeMutation
            : CanRunRepositoryMutation;
        if (!allowed) return false;

        var cancellation = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref _mutationCts, cancellation, null) is not null)
        {
            cancellation.Dispose();
            return false;
        }

        IsMutationBusy = true;
        MutationErrorMessage = null;
        try
        {
            var succeeded = await context.RunWorkingTreeMutationAsync(
                repository,
                () => mutation(repository, cancellation.Token),
                errorContext,
                beforeMutation);
            if (!succeeded &&
                !cancellation.IsCancellationRequested &&
                ReferenceEquals(repository, context.Repository))
            {
                MutationErrorMessage = context.ErrorMessage;
            }

            return succeeded;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _mutationCts, null, cancellation), cancellation))
                cancellation.Dispose();
            IsMutationBusy = false;
        }
    }

    private async Task LoadDiffAsync(
        Repository repository,
        WorkingTreeChange change,
        WorkingTreeDiffKind kind,
        DiffLoadMode mode)
    {
        var cancellation = new CancellationTokenSource();
        _diffCts = cancellation;
        var generation = Interlocked.Increment(ref _diffGeneration);

        SelectedDiff = null;
        DiffLoadErrorMessage = null;
        IsDiffPreviewDeferred = false;
        DiffPreviewDeferredMessage = null;
        DiffPreviewActionText = "Load diff";
        IsDiffLoading = true;

        try
        {
            var diff = _workingTreeDiffService is IWorkingTreeDiffLoadService controlled
                ? await controlled.ReadDiffAsync(repository, change, kind, mode, cancellation.Token)
                : await _workingTreeDiffService.ReadDiffAsync(repository, change, kind, cancellation.Token);

            if (!CanPublishDiff(repository, change, kind, generation, cancellation))
                return;

            SelectedDiff = diff;
        }
        catch (OperationCanceledException)
        {
        }
        catch (DiffPreviewTooLargeException exception) when (mode == DiffLoadMode.Preview)
        {
            if (CanPublishDiff(repository, change, kind, generation, cancellation))
            {
                SetDeferred(
                    $"The diff is larger than the {FormatDiffByteSize(exception.LimitBytes)} automatic preview limit. " +
                    "Generating and displaying the full diff may take some time.",
                    "Load full diff anyway");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (CanPublishDiff(repository, change, kind, generation, cancellation))
            {
                SelectedDiff = null;
                DiffLoadErrorMessage = exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_diffCts, cancellation))
            {
                _diffCts = null;
                cancellation.Dispose();
            }

            if (generation == Volatile.Read(ref _diffGeneration))
                IsDiffLoading = false;
        }
    }

    private bool CanPublishDiff(
        Repository repository,
        WorkingTreeChange change,
        WorkingTreeDiffKind kind,
        long generation,
        CancellationTokenSource cancellation) =>
        !cancellation.IsCancellationRequested &&
        generation == Volatile.Read(ref _diffGeneration) &&
        ReferenceEquals(_diffCts, cancellation) &&
        ReferenceEquals(repository, _context?.Repository) &&
        IsActive(change, kind);

    private void SetDeferred(string message, string actionText)
    {
        SelectedDiff = null;
        DiffLoadErrorMessage = null;
        IsDiffLoading = false;
        DiffPreviewDeferredMessage = message;
        DiffPreviewActionText = actionText;
        IsDiffPreviewDeferred = true;
    }

    private void ClearDiffResult()
    {
        SelectedDiff = null;
        DiffLoadErrorMessage = null;
        IsDiffLoading = false;
        IsDiffPreviewDeferred = false;
        DiffPreviewDeferredMessage = null;
        DiffPreviewActionText = "Load diff";
    }

    private void CancelDiff(bool preserveSelection = true)
    {
        Interlocked.Increment(ref _diffGeneration);
        var cancellation = Interlocked.Exchange(ref _diffCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        ClearDiffResult();

        if (!preserveSelection)
        {
            SelectedChange = null;
            SelectedDiffKind = null;
        }
    }

    private void CancelMutation()
    {
        var cancellation = Interlocked.Exchange(ref _mutationCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        IsMutationBusy = false;
    }

    private void ReconcileSelection()
    {
        _selectedUnstagedChanges = Reconcile(_selectedUnstagedChanges, WorkingTreeDiffKind.Unstaged);
        _selectedStagedChanges = Reconcile(_selectedStagedChanges, WorkingTreeDiffKind.Staged);
        Notify(nameof(SelectedUnstagedChanges));
        Notify(nameof(SelectedStagedChanges));
    }

    private IReadOnlyList<WorkingTreeChange> Reconcile(
        IReadOnlyList<WorkingTreeChange> selected,
        WorkingTreeDiffKind kind)
    {
        if (selected.Count == 0) return [];
        var paths = selected.Select(change => change.Path).ToHashSet(StringComparer.Ordinal);
        return Changes
            .Where(change =>
                paths.Contains(change.Path) &&
                (kind == WorkingTreeDiffKind.Unstaged ? change.IsUnstaged : change.IsStaged))
            .ToArray();
    }

    private void SetPendingBatchDiscard(WorkingTreeDiscardRequest? request)
    {
        _pendingBatchDiscard = request;
        Notify(nameof(BatchDiscardConfirmationMessage));
        RefreshAvailability();
    }

    private IEnumerable<AsyncCommand> WorkingTreeCommands()
    {
        yield return (AsyncCommand)StageCommand;
        yield return (AsyncCommand)UnstageCommand;
        yield return (AsyncCommand)StageSelectedCommand;
        yield return (AsyncCommand)StageAllCommand;
        yield return (AsyncCommand)UnstageSelectedCommand;
        yield return (AsyncCommand)UnstageAllCommand;
        yield return (AsyncCommand)RequestDiscardSelectedCommand;
        yield return (AsyncCommand)RequestDiscardAllCommand;
        yield return (AsyncCommand)ConfirmBatchDiscardCommand;
        yield return (AsyncCommand)CancelBatchDiscardCommand;
    }

    private static bool SameChange(WorkingTreeChange left, WorkingTreeChange right) =>
        string.Equals(left.Path, right.Path, StringComparison.Ordinal) &&
        left.IndexStatus == right.IndexStatus &&
        left.WorkingTreeStatus == right.WorkingTreeStatus &&
        string.Equals(left.OriginalPath, right.OriginalPath, StringComparison.Ordinal);

    private static bool TryGetWorkingTreeFileSize(
        Repository repository,
        string relativePath,
        out long size)
    {
        size = 0;
        try
        {
            var root = Path.GetFullPath(repository.WorkingDirectory);
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var rootPrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootPrefix, comparison)) return false;

            var info = new FileInfo(fullPath);
            if (!info.Exists) return false;
            size = info.Length;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string FormatDiffByteSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (1024d * 1024d):0.#} MB"
            : $"{Math.Max(1, bytes / 1024d):0.#} KB";

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
