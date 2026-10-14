using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public interface IStashesRepositoryContext : INotifyPropertyChanged
{
    Repository? Repository { get; }
    bool CanCreateStash { get; }
    bool CanMutateStash { get; }
    IReadOnlyList<WorkingTreeChange> WorkingTreeChanges { get; }

    Task<bool> RunStashMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string errorContext);
}

public sealed class StashesViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IStashMutationService _stashMutationService;
    private IStashesRepositoryContext? _context;
    private GitStash? _selectedStash;
    private bool _operationInProgress;
    private int _disposed;

    public StashesViewModel(IStashMutationService stashMutationService)
    {
        _stashMutationService = stashMutationService ?? throw new ArgumentNullException(nameof(stashMutationService));
        ApplyCommand = new AsyncCommand(ApplySelectedStashAsync, () => CanMutateSelectedStash);
        PopCommand = new AsyncCommand(PopSelectedStashAsync, () => CanMutateSelectedStash);
        DropCommand = new AsyncCommand(DropSelectedStashAsync, () => CanMutateSelectedStash);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action<GitStash?>? SelectedStashChanged;

    public ObservableCollection<GitStash> Items { get; } = new BulkObservableCollection<GitStash>();
    public ICommand ApplyCommand { get; }
    public ICommand PopCommand { get; }
    public ICommand DropCommand { get; }

    public GitStash? SelectedStash => _selectedStash;
    public bool HasSelectedStash => SelectedStash is not null;
    public bool CanCreateStash => !_operationInProgress && _context?.CanCreateStash == true;
    public bool CanMutateSelectedStash =>
        !_operationInProgress
        && SelectedStash is not null
        && _context?.CanMutateStash == true;

    internal void Attach(IStashesRepositoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (ReferenceEquals(_context, context)) return;

        Detach();
        _context = context;
        _context.PropertyChanged += Context_PropertyChanged;
        RefreshAvailability();
    }

    internal void Detach()
    {
        if (_context is not null)
            _context.PropertyChanged -= Context_PropertyChanged;
        _context = null;
        ClearRepositoryState();
        RefreshAvailability();
    }

    internal void OnRepositoryChanged(Repository? repository) => ClearRepositoryState();

    internal void ApplyRepositoryState(IEnumerable<GitStash> stashes)
    {
        ArgumentNullException.ThrowIfNull(stashes);

        var selectedCommit = SelectedStash?.Commit;
        var previousIndex = SelectedStash is null
            ? -1
            : Items.ToList().FindIndex(stash =>
                string.Equals(stash.Commit, SelectedStash.Commit, StringComparison.Ordinal));

        Replace(Items, stashes);

        if (selectedCommit is null)
        {
            RefreshAvailability();
            return;
        }

        var surviving = Items.FirstOrDefault(stash =>
            string.Equals(stash.Commit, selectedCommit, StringComparison.Ordinal));
        if (surviving is not null)
        {
            SetSelectedStash(surviving);
            RefreshAvailability();
            return;
        }

        GitStash? fallback = null;
        if (previousIndex >= 0 && previousIndex < Items.Count)
            fallback = Items[previousIndex];
        else if (previousIndex > 0 && previousIndex - 1 < Items.Count)
            fallback = Items[previousIndex - 1];

        SetSelectedStash(fallback);
        RefreshAvailability();
    }

    internal void ClearRepositoryState()
    {
        Replace(Items, []);
        SetSelectedStash(null);
        RefreshAvailability();
    }

    public Task SelectStashAsync(GitStash stash)
    {
        ArgumentNullException.ThrowIfNull(stash);
        var canonical = Items.FirstOrDefault(item =>
            string.Equals(item.Commit, stash.Commit, StringComparison.Ordinal)) ?? stash;
        SetSelectedStash(canonical);
        return Task.CompletedTask;
    }

    public void ClearSelection() => SetSelectedStash(null);

    internal bool CanMutateStash(GitStash stash) =>
        stash is not null
        && !_operationInProgress
        && _context?.CanMutateStash == true;

    internal bool CanCreateStashRequest(StashScope scope, bool includeUntracked)
    {
        if (!CanCreateStash || _context is null) return false;

        return scope switch
        {
            StashScope.AllTrackedChanges =>
                _context.WorkingTreeChanges.Any(change => change.Kind != FileChangeKind.Untracked)
                || includeUntracked && _context.WorkingTreeChanges.Any(change => change.Kind == FileChangeKind.Untracked),
            StashScope.StagedChangesOnly =>
                !includeUntracked && _context.WorkingTreeChanges.Any(change => change.IsStaged),
            _ => false
        };
    }

    internal bool CanCreateSelectedStash(IReadOnlyCollection<WorkingTreeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return CanCreateStash && changes.Count > 0;
    }

    public async Task CreateStashAsync(CreateStashRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = _context;
        var repository = context?.Repository;
        if (context is null || repository is null || !CanCreateStash) return;

        await RunMutationAsync(
            repository,
            () => _stashMutationService.CreateStashAsync(repository, request),
            "Could not create stash");
    }

    internal Task CreateSelectedStashAsync(
        IReadOnlyCollection<WorkingTreeChange> changes,
        string? message)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (!CanCreateSelectedStash(changes))
            return Task.CompletedTask;

        var snapshot = changes
            .Select(change => new StashSelectedPath(
                change.Path,
                change.OriginalPath,
                change.Kind == FileChangeKind.Untracked))
            .ToArray();

        return CreateStashAsync(new CreateStashRequest(
            message,
            StashScope.SelectedPaths,
            snapshot));
    }

    internal void RefreshAvailability()
    {
        Notify(nameof(CanCreateStash));
        Notify(nameof(CanMutateSelectedStash));
        ((AsyncCommand)ApplyCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)PopCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)DropCommand).RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Detach();
        SelectedStashChanged = null;
    }

    private async Task ApplySelectedStashAsync()
    {
        var stash = SelectedStash;
        if (stash is null) return;
        await RunSelectedMutationAsync(
            stash,
            (repository, selected) => _stashMutationService.ApplyStashAsync(repository, selected),
            "Could not apply stash");
    }

    private async Task PopSelectedStashAsync()
    {
        var stash = SelectedStash;
        if (stash is null) return;
        await RunSelectedMutationAsync(
            stash,
            (repository, selected) => _stashMutationService.PopStashAsync(repository, selected),
            "Could not pop stash");
    }

    private async Task DropSelectedStashAsync()
    {
        var stash = SelectedStash;
        if (stash is null) return;
        await RunSelectedMutationAsync(
            stash,
            (repository, selected) => _stashMutationService.DropStashAsync(repository, selected),
            "Could not drop stash");
    }

    private Task RunSelectedMutationAsync(
        GitStash stash,
        Func<Repository, GitStash, Task> mutation,
        string errorContext)
    {
        var context = _context;
        var repository = context?.Repository;
        if (context is null || repository is null || !CanMutateStash(stash))
            return Task.CompletedTask;

        return RunMutationAsync(
            repository,
            () => mutation(repository, stash),
            errorContext);
    }

    private async Task RunMutationAsync(
        Repository repository,
        Func<Task> mutation,
        string errorContext)
    {
        var context = _context;
        if (context is null || !ReferenceEquals(repository, context.Repository))
            return;

        _operationInProgress = true;
        RefreshAvailability();
        try
        {
            await context.RunStashMutationAsync(repository, mutation, errorContext);
        }
        finally
        {
            _operationInProgress = false;
            RefreshAvailability();
        }
    }

    private void SetSelectedStash(GitStash? value)
    {
        if (ReferenceEquals(_selectedStash, value)) return;
        _selectedStash = value;
        Notify(nameof(SelectedStash));
        Notify(nameof(HasSelectedStash));
        RefreshAvailability();
        SelectedStashChanged?.Invoke(value);
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IStashesRepositoryContext.Repository))
            ClearRepositoryState();
        else
            RefreshAvailability();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        var snapshot = values as IReadOnlyList<T> ?? values.ToArray();
        if (target.SequenceEqual(snapshot)) return;

        if (target is BulkObservableCollection<T> bulk)
        {
            bulk.ReplaceAll(snapshot);
            return;
        }

        target.Clear();
        foreach (var value in snapshot) target.Add(value);
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
