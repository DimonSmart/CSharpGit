using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public interface IRepositoryOperationsContext : INotifyPropertyChanged
{
    Repository? Repository { get; }
    bool IsBusy { get; }
    bool CanRunRepositoryMutation { get; }

    Task<bool> RunRepositoryOperationMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string? errorContext = null);
}

public sealed class RepositoryOperationsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMergeService _mergeService;
    private readonly IConflictResolutionService _conflictResolutionService;
    private readonly IRepositoryOperationService _repositoryOperationService;
    private readonly IExternalGitToolService _externalGitToolService;
    private IRepositoryOperationsContext? _context;
    private GitBranch? _selectedMergeBranch;
    private string _operationDisplay = string.Empty;
    private RepositoryOperation _currentOperation;
    private ConflictFile? _selectedConflict;
    private RepositoryOperationState _operationState = RepositoryOperationState.None;
    private int _disposed;

    public RepositoryOperationsViewModel(
        IMergeService mergeService,
        IConflictResolutionService conflictResolutionService,
        IRepositoryOperationService repositoryOperationService,
        IExternalGitToolService externalGitToolService)
    {
        _mergeService = mergeService ?? throw new ArgumentNullException(nameof(mergeService));
        _conflictResolutionService = conflictResolutionService ?? throw new ArgumentNullException(nameof(conflictResolutionService));
        _repositoryOperationService = repositoryOperationService ?? throw new ArgumentNullException(nameof(repositoryOperationService));
        _externalGitToolService = externalGitToolService ?? throw new ArgumentNullException(nameof(externalGitToolService));

        MergeCommand = new AsyncCommand(
            MergeAsync,
            () => CanRunRepositoryMutation && SelectedMergeBranch is { IsCurrent: false });
        OpenConflictCommand = new AsyncCommand(
            () => RunSelectedConflictMutationAsync(conflict =>
                _externalGitToolService.OpenConflictInEditorAsync(Repository!, conflict)),
            () => CanRunRepositoryMutation && SelectedConflict?.CanOpenManually == true);
        ChooseCurrentCommand = new AsyncCommand(
            () => RunSelectedConflictMutationAsync(conflict =>
                _conflictResolutionService.ChooseConflictSideAsync(
                    Repository!,
                    conflict,
                    ConflictResolutionSide.CurrentLocal)),
            () => CanRunRepositoryMutation && SelectedConflict?.CanChooseCurrentLocal == true);
        ChooseIncomingCommand = new AsyncCommand(
            () => RunSelectedConflictMutationAsync(conflict =>
                _conflictResolutionService.ChooseConflictSideAsync(
                    Repository!,
                    conflict,
                    ConflictResolutionSide.IncomingRemote)),
            () => CanRunRepositoryMutation && SelectedConflict?.CanChooseIncomingRemote == true);
        KeepDeletionCommand = new AsyncCommand(
            () => RunSelectedConflictMutationAsync(conflict =>
                _conflictResolutionService.KeepConflictDeletionAsync(Repository!, conflict)),
            () => CanRunRepositoryMutation && SelectedConflict?.CanKeepDeletion == true);
        StageConflictCommand = new AsyncCommand(
            () => RunSelectedConflictMutationAsync(conflict =>
                _conflictResolutionService.StageResolvedConflictAsync(Repository!, conflict)),
            () => CanRunRepositoryMutation && SelectedConflict?.CanStage == true);
        MergeToolCommand = new AsyncCommand(
            () => RunSelectedConflictMutationAsync(conflict =>
                _externalGitToolService.RunMergeToolForFileAsync(Repository!, conflict)),
            () => CanRunRepositoryMutation && SelectedConflict?.CanRunMergeTool == true);
        MergeToolWorkflowCommand = new AsyncCommand(
            () => RunRepositoryMutationAsync(repository =>
                _externalGitToolService.RunMergeToolWorkflowAsync(repository)),
            () => CanRunRepositoryMutation && Conflicts.Any(conflict => !conflict.IsResolved));
        ContinueOperationCommand = new AsyncCommand(
            () => RunRepositoryMutationAsync(repository =>
                _repositoryOperationService.ContinueOperationAsync(repository)),
            () => CanRunRepositoryMutation && OperationState.CanContinue);
        AbortOperationCommand = new AsyncCommand(
            () => RunRepositoryMutationAsync(repository =>
                _repositoryOperationService.AbortOperationAsync(repository)),
            () => CanRunRepositoryMutation && OperationState.CanAbort);
        SkipOperationCommand = new AsyncCommand(
            () => RunRepositoryMutationAsync(repository =>
                _repositoryOperationService.SkipOperationAsync(repository)),
            () => CanRunRepositoryMutation && OperationState.CanSkip);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ConflictFile> Conflicts { get; } =
        new BulkObservableCollection<ConflictFile>();

    public ICommand MergeCommand { get; }
    public ICommand OpenConflictCommand { get; }
    public ICommand ChooseCurrentCommand { get; }
    public ICommand ChooseIncomingCommand { get; }
    public ICommand KeepDeletionCommand { get; }
    public ICommand StageConflictCommand { get; }
    public ICommand MergeToolCommand { get; }
    public ICommand MergeToolWorkflowCommand { get; }
    public ICommand ContinueOperationCommand { get; }
    public ICommand AbortOperationCommand { get; }
    public ICommand SkipOperationCommand { get; }

    public GitBranch? SelectedMergeBranch
    {
        get => _selectedMergeBranch;
        set
        {
            if (ReferenceEquals(_selectedMergeBranch, value)) return;
            _selectedMergeBranch = value;
            Notify();
            RefreshAvailability();
        }
    }

    public string OperationDisplay
    {
        get => _operationDisplay;
        private set
        {
            if (string.Equals(_operationDisplay, value, StringComparison.Ordinal)) return;
            _operationDisplay = value;
            Notify();
        }
    }

    public RepositoryOperation CurrentOperation
    {
        get => _currentOperation;
        private set
        {
            if (_currentOperation == value) return;
            _currentOperation = value;
            Notify();
            RefreshAvailability();
        }
    }

    public ConflictFile? SelectedConflict
    {
        get => _selectedConflict;
        set
        {
            if (ReferenceEquals(_selectedConflict, value)) return;
            _selectedConflict = value;
            Notify();
            Notify(nameof(CurrentSideLabel));
            Notify(nameof(IncomingSideLabel));
            RefreshAvailability();
        }
    }

    public RepositoryOperationState OperationState
    {
        get => _operationState;
        private set
        {
            if (Equals(_operationState, value)) return;
            _operationState = value;
            Notify();
            Notify(nameof(HasActiveOperation));
            RefreshAvailability();
        }
    }

    public string CurrentSideLabel => SelectedConflict?.CurrentLocalLabel ?? "Current/local";
    public string IncomingSideLabel => SelectedConflict?.IncomingRemoteLabel ?? "Incoming/remote";
    public bool HasActiveOperation => OperationState.Kind != RepositoryOperation.None;

    private Repository? Repository => _context?.Repository;
    private bool CanRunRepositoryMutation => _context?.CanRunRepositoryMutation == true;

    internal void Attach(IRepositoryOperationsContext context)
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

    internal void ApplyRepositoryState(
        RepositoryOperation operation,
        RepositoryOperationState operationState,
        IEnumerable<GitBranch> localBranches)
    {
        ArgumentNullException.ThrowIfNull(operationState);
        ArgumentNullException.ThrowIfNull(localBranches);

        var selectedConflictPath = SelectedConflict?.Path;
        var selectedMergeBranchName = SelectedMergeBranch?.Name;
        var branches = localBranches as IReadOnlyList<GitBranch> ?? localBranches.ToArray();

        CurrentOperation = operation;
        OperationState = operationState;
        OperationDisplay = operation == RepositoryOperation.None
            ? "No operation in progress"
            : $"Operation in progress: {operation}";

        Replace(Conflicts, operationState.Conflicts);
        SelectedConflict = selectedConflictPath is null
            ? Conflicts.FirstOrDefault()
            : Conflicts.FirstOrDefault(conflict =>
                  string.Equals(conflict.Path, selectedConflictPath, StringComparison.Ordinal))
              ?? Conflicts.FirstOrDefault();

        SelectedMergeBranch = selectedMergeBranchName is null
            ? branches.FirstOrDefault(branch => !branch.IsCurrent)
            : branches.FirstOrDefault(branch =>
                  string.Equals(branch.Name, selectedMergeBranchName, StringComparison.Ordinal)
                  && !branch.IsCurrent)
              ?? branches.FirstOrDefault(branch => !branch.IsCurrent);

        RefreshAvailability();
    }

    internal void ClearRepositoryState()
    {
        CurrentOperation = RepositoryOperation.None;
        OperationState = RepositoryOperationState.None;
        OperationDisplay = string.Empty;
        Replace(Conflicts, []);
        SelectedConflict = null;
        SelectedMergeBranch = null;
        RefreshAvailability();
    }

    internal void SetOperationDisplay(string message) =>
        OperationDisplay = message ?? string.Empty;

    internal void RefreshAvailability()
    {
        foreach (var command in new[]
                 {
                     MergeCommand,
                     OpenConflictCommand,
                     ChooseCurrentCommand,
                     ChooseIncomingCommand,
                     KeepDeletionCommand,
                     StageConflictCommand,
                     MergeToolCommand,
                     MergeToolWorkflowCommand,
                     ContinueOperationCommand,
                     AbortOperationCommand,
                     SkipOperationCommand
                 }.OfType<AsyncCommand>())
        {
            command.RaiseCanExecuteChanged();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Detach();
    }

    private async Task MergeAsync()
    {
        var context = _context;
        var repository = context?.Repository;
        var branch = SelectedMergeBranch;
        if (context is null || repository is null || branch is null)
            return;

        MergeResult? result = null;
        await context.RunRepositoryOperationMutationAsync(
            repository,
            async () => result = await _mergeService.MergeAsync(repository, branch.Name));

        if (result is not null)
            SetOperationDisplay(result.Message);
    }

    private Task RunSelectedConflictMutationAsync(Func<ConflictFile, Task> mutation)
    {
        var conflict = SelectedConflict;
        return conflict is null
            ? Task.CompletedTask
            : RunRepositoryMutationAsync(_ => mutation(conflict));
    }

    private async Task RunRepositoryMutationAsync(Func<Repository, Task> mutation)
    {
        var context = _context;
        var repository = context?.Repository;
        if (context is null || repository is null || !context.CanRunRepositoryMutation)
            return;

        await context.RunRepositoryOperationMutationAsync(
            repository,
            () => mutation(repository));
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IRepositoryOperationsContext.Repository))
        {
            ClearRepositoryState();
            return;
        }

        if (args.PropertyName == nameof(IRepositoryOperationsContext.IsBusy))
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
        foreach (var value in snapshot)
            target.Add(value);
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
