using System.ComponentModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Threading;
using Microsoft.Extensions.Logging;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IFolderPicker _folderPicker;
    private readonly IRepositoryService _repositoryService;
    private readonly ILogger<OpenRepositoryViewModel> _logger;
    private readonly IRepositoryStateService _stateService;
    private readonly IAppSettingsService _settings;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly AsyncCommand _openRepositoryCommand;
    private Repository? _repository;
    private string? _errorMessage;
    private bool _isBusy;
    private int _busyOperations;
    private bool _isMutating;
    private int _repositoryChangeInProgress;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private int _disposed;
    private string _headDisplay = string.Empty;
    private string? _currentBranchName;
    private string? _currentHeadCommit;
    private bool _isDetachedHead;
    private bool? _headExists;
    private readonly DisplayedWorkingTreeBaseline _displayedWorkingTreeBaseline = new();
    internal event Action<Repository>? RepositoryStateRefreshStarting;
    internal event Action<Repository>? RepositoryStateRefreshCompleted;

    public OpenRepositoryViewModel(
        IFolderPicker folderPicker,
        IRepositoryService repositoryService,
        IRepositoryStateService stateService,
        WorkingTreeViewModel workingTreeViewModel,
        BranchesViewModel branchesViewModel,
        TagsViewModel tagsViewModel,
        HistoryViewModel historyViewModel,
        StashesViewModel stashesViewModel,
        CommitDetailsViewModel commitDetailsViewModel,
        CommitActionsViewModel commitActionsViewModel,
        CommitCreationViewModel commitCreationViewModel,
        RepositoryHistoryRewriteViewModel repositoryHistoryRewriteViewModel,
        RepositorySyncViewModel repositorySyncViewModel,
        RepositoryOperationsViewModel repositoryOperationsViewModel,
        InteractiveRebaseViewModel interactiveRebaseViewModel,
        IAppSettingsService settings,
        IUiDispatcher uiDispatcher,
        ILogger<OpenRepositoryViewModel> logger)
    {
        _folderPicker = folderPicker;
        _repositoryService = repositoryService;
        _stateService = stateService;
        WorkingTree = workingTreeViewModel ?? throw new ArgumentNullException(nameof(workingTreeViewModel));
        WorkingTree.Attach(this);
        Branches = branchesViewModel ?? throw new ArgumentNullException(nameof(branchesViewModel));
        Branches.Attach(this);
        Tags = tagsViewModel ?? throw new ArgumentNullException(nameof(tagsViewModel));
        Tags.Attach(this);
        RepositorySync = repositorySyncViewModel ?? throw new ArgumentNullException(nameof(repositorySyncViewModel));
        RepositorySync.Attach(this);
        History = historyViewModel ?? throw new ArgumentNullException(nameof(historyViewModel));
        History.Attach(this);
        History.SelectedRowChanged += History_SelectedRowChanged;
        Stashes = stashesViewModel ?? throw new ArgumentNullException(nameof(stashesViewModel));
        Stashes.Attach(this);
        Stashes.SelectedStashChanged += Stashes_SelectedStashChanged;
        CommitDetails = commitDetailsViewModel ?? throw new ArgumentNullException(nameof(commitDetailsViewModel));
        CommitDetails.Attach(this);
        CommitActions = commitActionsViewModel ?? throw new ArgumentNullException(nameof(commitActionsViewModel));
        CommitActions.Attach(this);
        RepositoryHistoryRewrite = repositoryHistoryRewriteViewModel ?? throw new ArgumentNullException(nameof(repositoryHistoryRewriteViewModel));
        RepositoryHistoryRewrite.Attach(this);
        RepositoryOperations = repositoryOperationsViewModel ?? throw new ArgumentNullException(nameof(repositoryOperationsViewModel));
        RepositoryOperations.Attach(this);
        RepositoryOperations.PropertyChanged += RepositoryOperations_PropertyChanged;
        InteractiveRebase = interactiveRebaseViewModel ?? throw new ArgumentNullException(nameof(interactiveRebaseViewModel));
        InteractiveRebase.Attach(this);
        CommitCreation = commitCreationViewModel ?? throw new ArgumentNullException(nameof(commitCreationViewModel));
        CommitCreation.Attach(this);
        WorkingTree.Changes.CollectionChanged += CommitCreationSourceCollectionChanged;
        RepositoryOperations.Conflicts.CollectionChanged += CommitCreationSourceCollectionChanged;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _settings.Changed += AppSettings_Changed;
        _logger = logger;
        _openRepositoryCommand = new AsyncCommand(OpenRepositoryAsync, () => !IsBusy && Repository is null);
        RefreshAllCommand = new AsyncCommand(RefreshAllAsync, () => Repository is not null);
        DismissErrorCommand = new AsyncCommand(() =>
        {
            ErrorMessage = null;
            return Task.CompletedTask;
        }, () => true);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Stashes.SelectedStashChanged -= Stashes_SelectedStashChanged;
        Stashes.Dispose();
        CommitDetails.Dispose();
        WorkingTree.Changes.CollectionChanged -= CommitCreationSourceCollectionChanged;
        RepositoryOperations.Conflicts.CollectionChanged -= CommitCreationSourceCollectionChanged;
        RepositoryOperations.PropertyChanged -= RepositoryOperations_PropertyChanged;
        RepositoryOperations.Dispose();
        InteractiveRebase.Dispose();
        _settings.Changed -= AppSettings_Changed;
        History.SelectedRowChanged -= History_SelectedRowChanged;
        History.Dispose();
        RepositorySync.Dispose();
        Tags.Dispose();
        Branches.Dispose();
        WorkingTree.Dispose();
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
        if (Volatile.Read(ref _disposed) != 0) return;

        Notify(nameof(CommitTimeDisplayMode));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CommitTimeDisplayMode CommitTimeDisplayMode => _settings.CommitTimeDisplayMode;

    public ICommand OpenRepositoryCommand => _openRepositoryCommand;
    public ICommand RefreshAllCommand { get; }
    public ICommand DismissErrorCommand { get; }
    public HistoryViewModel History { get; }
    public StashesViewModel Stashes { get; }
    public CommitDetailsViewModel CommitDetails { get; }
    public CommitActionsViewModel CommitActions { get; }
    public RepositoryHistoryRewriteViewModel RepositoryHistoryRewrite { get; }
    public CommitCreationViewModel CommitCreation { get; }
    public RepositoryOperationsViewModel RepositoryOperations { get; }
    public InteractiveRebaseViewModel InteractiveRebase { get; }
    public WorkingTreeViewModel WorkingTree { get; }
    public BranchesViewModel Branches { get; }
    public RepositorySyncViewModel RepositorySync { get; }
    public TagsViewModel Tags { get; }
    public Repository? Repository
    {
        get => _repository;
        private set
        {
            if (ReferenceEquals(_repository, value)) return;

            SetHeadPresentationState(null, null, false, string.Empty);
            _repository = value;
            History.OnRepositoryChanged(value);
            WorkingTree.OnRepositoryChanged(value);
            Stashes.OnRepositoryChanged(value);
            CommitDetails.OnRepositoryChanged(value);
            _headExists = null;
            _displayedWorkingTreeBaseline.Clear();
            Notify();
            Notify(nameof(DisplayedWorkingTreeStatusSnapshot));
            Notify(nameof(HasRepository));
            Notify(nameof(RepositoryKind));
        }
    }
    public WorkingTreeStatusSnapshot? DisplayedWorkingTreeStatusSnapshot => _displayedWorkingTreeBaseline.Snapshot;
    public long DisplayedRefreshBaselineRevision => _displayedWorkingTreeBaseline.Revision;
    public string? ErrorMessage { get => _errorMessage; private set { _errorMessage = value; Notify(); Notify(nameof(HasError)); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; Notify(); _openRepositoryCommand.RaiseCanExecuteChanged(); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasRepository => Repository is not null;
    public bool CanChangeRepository => !_isMutating && Volatile.Read(ref _repositoryChangeInProgress) == 0;
    public string RepositoryKind => Repository?.IsWorktree == true ? "Git worktree" : "Git repository";
    public bool HasSelectedCommit => History.SelectedRow is not null;
    public string HeadDisplay => _headDisplay;
    public string? CurrentBranchName => _currentBranchName;
    public string? CurrentHeadCommit => _currentHeadCommit;
    public bool IsDetachedHead => _isDetachedHead;
    public Task RefreshWhenActivatedAsync() => Repository is null ? Task.CompletedTask : RefreshAllAsync();

    internal Task OpenRepositoryAsyncForDesktopCheck() => _openRepositoryCommand.ExecuteAsync();

    internal Task RefreshAsyncForDesktopCheck() => RefreshAllAsync();

    internal Task<bool> RunMutationAsync(Func<Task> mutation, string? errorContext = null, bool includeHistory = true) =>
        MutateAsync(mutation, errorContext, includeHistory: includeHistory);

    private async Task OpenRepositoryAsync()
    {
        ErrorMessage = null;
        EnterBusy();
        try
        {
            string? path;
            try
            {
                path = await _folderPicker.PickFolderAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ErrorMessage = $"The system folder picker could not be opened: {exception.Message}";
                _logger.LogError(exception, "Native folder picker failed");
                return;
            }

            if (path is not null)
                _ = await OpenRepositoryPathAsync(path);
        }
        finally
        {
            ExitBusy();
            _openRepositoryCommand.RaiseCanExecuteChanged();
        }
    }

    internal async Task<bool> OpenRepositoryPathAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Interlocked.CompareExchange(ref _repositoryChangeInProgress, 1, 0) != 0)
            return false;

        Notify(nameof(CanChangeRepository));
        RaiseCommands();
        if (!await _mutationGate.WaitAsync(0))
        {
            Interlocked.Exchange(ref _repositoryChangeInProgress, 0);
            Notify(nameof(CanChangeRepository));
            RaiseCommands();
            return false;
        }

        var previousRepository = Repository;
        ErrorMessage = null;
        EnterBusy();
        try
        {
            var openedRepository = await _repositoryService.OpenAsync(
                path,
                cancellationToken);

            History.Invalidate();
            Repository = openedRepository;
            try
            {
                await RefreshAllAsync();
            }
            catch
            {
                History.Invalidate();
                Repository = previousRepository;
                if (previousRepository is null)
                    ClearRepositoryPresentation();
                else
                {
                    try
                    {
                        await RefreshAllAsync();
                    }
                    catch (Exception restoreException) when (
                        restoreException is not OperationCanceledException)
                    {
                        _logger.LogError(
                            restoreException,
                            "Could not refresh the previous repository after a failed workspace switch");
                    }
                }

                throw;
            }

            _logger.LogInformation(
                "Opened repository at {RepositoryRoot}",
                Repository.RepositoryRoot);
            return true;
        }
        catch (RepositoryOpenException exception)
        {
            ErrorMessage = exception.Message;
            _logger.LogWarning(exception, "Repository selection failed");
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"The repository could not be opened: {exception.Message}";
            _logger.LogError(exception, "Repository opening failed");
            return false;
        }
        finally
        {
            ExitBusy();
            _mutationGate.Release();
            Interlocked.Exchange(ref _repositoryChangeInProgress, 0);
            Notify(nameof(CanChangeRepository));
            RaiseCommands();
            _openRepositoryCommand.RaiseCanExecuteChanged();
        }
    }

    internal bool CloseRepository()
    {
        if (Repository is null) return true;
        if (Interlocked.CompareExchange(ref _repositoryChangeInProgress, 1, 0) != 0)
            return false;

        Notify(nameof(CanChangeRepository));
        RaiseCommands();
        if (!_mutationGate.Wait(0))
        {
            Interlocked.Exchange(ref _repositoryChangeInProgress, 0);
            Notify(nameof(CanChangeRepository));
            RaiseCommands();
            return false;
        }

        try
        {
            History.Invalidate();
            Repository = null;
            ClearRepositoryPresentation();
            ErrorMessage = null;
            _openRepositoryCommand.RaiseCanExecuteChanged();
            return true;
        }
        finally
        {
            _mutationGate.Release();
            Interlocked.Exchange(ref _repositoryChangeInProgress, 0);
            Notify(nameof(CanChangeRepository));
            RaiseCommands();
        }
    }

    private void ClearRepositoryPresentation()
    {
        WorkingTree.ClearRepositoryState();
        Branches.ClearRepositoryState();
        Stashes.ClearRepositoryState();
        CommitDetails.ClearRepositoryState();
        RepositorySync.ClearRepositoryState();
        Tags.ClearRepositoryState();
        RepositoryOperations.ClearRepositoryState();
        InteractiveRebase.ClearRepositoryState();
        SetHeadPresentationState(null, null, false, string.Empty);
        ClearDisplayedWorkingTreeBaseline();
        RaiseCommands();
    }

    private void SetHeadPresentationState(
        string? branchName,
        string? headCommit,
        bool isDetachedHead,
        string headDisplay)
    {
        var headDisplayChanged = !string.Equals(_headDisplay, headDisplay, StringComparison.Ordinal);
        var branchChanged = !string.Equals(_currentBranchName, branchName, StringComparison.Ordinal);
        var commitChanged = !string.Equals(_currentHeadCommit, headCommit, StringComparison.Ordinal);
        var detachedChanged = _isDetachedHead != isDetachedHead;

        _headDisplay = headDisplay;
        _currentBranchName = branchName;
        _currentHeadCommit = headCommit;
        _isDetachedHead = isDetachedHead;

        if (headDisplayChanged) Notify(nameof(HeadDisplay));
        if (branchChanged) Notify(nameof(CurrentBranchName));
        if (commitChanged) Notify(nameof(CurrentHeadCommit));
        if (detachedChanged) Notify(nameof(IsDetachedHead));
    }

    private void ClearDisplayedWorkingTreeBaseline()
    {
        if (_displayedWorkingTreeBaseline.Clear())
            Notify(nameof(DisplayedWorkingTreeStatusSnapshot));
    }

    private void PublishDisplayedWorkingTreeBaseline(WorkingTreeStatusSnapshot snapshot)
    {
        if (_displayedWorkingTreeBaseline.Publish(snapshot))
            Notify(nameof(DisplayedWorkingTreeStatusSnapshot));
        Notify(nameof(DisplayedRefreshBaselineRevision));
    }

    private bool CanMutate() =>
        Repository is not null
        && !_isMutating
        && Volatile.Read(ref _repositoryChangeInProgress) == 0;

    private bool CanBulkMutate() =>
        CanMutate()
        && !RepositoryOperations.Conflicts.Any(conflict => !conflict.IsResolved);

    private void EnterBusy()
    {
        _busyOperations++;
        IsBusy = true;
    }

    private void ExitBusy()
    {
        _busyOperations = Math.Max(0, _busyOperations - 1);
        IsBusy = _busyOperations > 0;
    }

    private Task RefreshAllAsync() => RefreshStateAsync(includeHistory: true);

    private Task RefreshStateAsync(bool includeHistory) =>
        RefreshStateCoreAsync(includeHistory, localOnly: false);

    private Task RefreshStateLocalOnlyAsync(bool includeHistory) =>
        RefreshStateCoreAsync(includeHistory, localOnly: true);

    private async Task RefreshStateCoreAsync(bool includeHistory, bool localOnly)
    {
        if (Repository is null) return;
        EnterBusy();
        var repository = Repository;
        RepositoryStateRefreshStarting?.Invoke(repository);
        try
        {
            var stateRead = await _stateService.ReadWithWorkingTreeStatusAsync(
                repository,
                localOnly);
            var state = stateRead.State;
            var workingTreeStatus = stateRead.WorkingTreeStatus;
            if (!ReferenceEquals(repository, Repository)) return;

            _headExists = state.HeadCommit is not null;
            var shortHead = state.HeadCommit is { } commit ? commit[..Math.Min(10, commit.Length)] : "no commit";
            SetHeadPresentationState(
                state.IsDetached ? null : state.HeadReference,
                state.HeadCommit,
                state.IsDetached,
                state.IsDetached ? $"Detached HEAD: {shortHead}" : $"Current branch: {state.HeadReference}");
            WorkingTree.ApplyRepositoryState(repository, state.Changes);
            Branches.ApplyRepositoryState(
                state.Refs.LocalBranches,
                state.Refs.RemoteBranches);
            RepositorySync.ApplyRepositoryState(state.Refs.Remotes);
            Tags.ApplyRepositoryState(state.Refs.Tags);
            Stashes.ApplyRepositoryState(state.Stashes);
            RepositoryOperations.ApplyRepositoryState(
                state.Operation,
                state.CurrentOperation,
                Branches.LocalBranches);
            if (includeHistory)
                await History.RefreshAsync();

            if (ReferenceEquals(repository, Repository))
                PublishDisplayedWorkingTreeBaseline(workingTreeStatus);
        }
        finally
        {
            RepositoryStateRefreshCompleted?.Invoke(repository);
            ExitBusy();
        }
    }

    private async Task<bool> MutateAsync(
        Func<Task> mutation,
        string? errorContext = null,
        Action? beforeMutation = null,
        bool includeHistory = true,
        bool localOnlyRefresh = false,
        Action? afterSuccessfulMutation = null,
        Repository? expectedRepository = null)
    {
        var succeeded = false;
        if (Volatile.Read(ref _repositoryChangeInProgress) != 0
            || !await _mutationGate.WaitAsync(0))
            return false;

        if (Volatile.Read(ref _repositoryChangeInProgress) != 0)
        {
            _mutationGate.Release();
            return false;
        }

        if (expectedRepository is not null && !ReferenceEquals(expectedRepository, Repository))
        {
            _mutationGate.Release();
            return false;
        }

        _isMutating = true;
        Notify(nameof(CanChangeRepository));
        EnterBusy();
        RaiseCommands();
        ErrorMessage = null;
        try
        {
            beforeMutation?.Invoke();
            Exception? failure = null;
            OperationCanceledException? cancellation = null;
            try
            {
                await mutation();
                afterSuccessfulMutation?.Invoke();
            }
            catch (OperationCanceledException exception) { cancellation = exception; }
            catch (Exception exception) { failure = exception; }
            try
            {
                if (localOnlyRefresh)
                    await RefreshStateLocalOnlyAsync(includeHistory);
                else
                    await RefreshStateAsync(includeHistory);
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { failure ??= exception; }
            if (cancellation is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cancellation).Throw();
            if (failure is not null)
                ErrorMessage = errorContext is null ? $"Git: {failure.Message}" : $"{errorContext}\nGit: {failure.Message}";
            succeeded = failure is null;
        }
        finally
        {
            _isMutating = false;
            Notify(nameof(CanChangeRepository));
            ExitBusy();
            _mutationGate.Release();
            RaiseCommands();
        }
        return succeeded;
    }

    private void RaiseCommands()
    {
        foreach (var command in new[] { RefreshAllCommand }.OfType<AsyncCommand>())
            command.RaiseCanExecuteChanged();
        Tags.RefreshAvailability();
        RepositorySync.RefreshAvailability();
        CommitCreation.RefreshAvailability();
        RepositoryOperations.RefreshAvailability();
        Branches.RefreshAvailability();
        WorkingTree.RefreshAvailability();
        History.RefreshAvailability();
        Stashes.RefreshAvailability();
    }

    private void CommitCreationSourceCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args) =>
        CommitCreation.RefreshAvailability();

    private void RepositoryOperations_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(RepositoryOperationsViewModel.CurrentOperation))
            return;

        Notify(nameof(IBranchesRepositoryContext.CurrentOperation));
        RepositorySync.RefreshAvailability();
        RaiseCommands();
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
