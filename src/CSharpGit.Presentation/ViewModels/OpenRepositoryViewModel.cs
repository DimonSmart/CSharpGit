using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation.ViewModels;

public sealed record UiChoice<T>(string Label, T Value);

public sealed partial class OpenRepositoryViewModel : INotifyPropertyChanged
{
    private readonly IFolderPicker _folderPicker;
    private readonly IRepositoryService _repositoryService;
    private readonly ILogger<OpenRepositoryViewModel> _logger;
    private readonly IHistoryService _historyService;
    private readonly IRepositoryStateService _stateService;
    private readonly IWorkingTreeService _workingTreeService;
    private readonly IReferenceService _referenceService;
    private readonly IRepositoryWorkflowService _workflowService;
    private readonly AsyncCommand _openRepositoryCommand;
    private Repository? _repository;
    private string? _errorMessage;
    private bool _isBusy;
    private int _busyOperations;
    private bool _isMutating;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private CancellationTokenSource? _historyLoadCts;
    private long _historyLoadGeneration;
    private long _diffLoadGeneration;
    private string _filterText = string.Empty;
    private UiChoice<HistoryScope> _selectedScope;
    private HistoryRow? _selectedHistoryRow;
    private ChangedFile? _selectedFile;
    private FileDiff? _selectedDiff;
    private bool _isDiffLoading;
    private bool _hasMore;
    private WorkingTreeChange? _selectedChange;
    private WorkingTreeDiffKind? _selectedWorkingTreeDiffKind;
    private FileDiff? _selectedWorkingTreeDiff;
    private WorkingTreeChange? _pendingDiscard;
    private readonly ObservableCollection<WorkingTreeChange> _selectedUnstagedChanges = [];
    private readonly ObservableCollection<WorkingTreeChange> _selectedStagedChanges = [];
    private string _commitMessage = string.Empty;
    private bool _isEmptyIndexChoiceOpen;
    private GitBranch? _selectedLocalBranch;
    private GitBranch? _selectedRemoteBranch;
    private GitRemote? _selectedRemote;
    private GitTag? _selectedTag;
    private string _newBranchName = string.Empty;
    private string _pushBranchName = string.Empty;
    private bool _setUpstream;
    private string _headDisplay = string.Empty;
    private GitStash? _selectedStash;
    private GitBranch? _selectedMergeBranch;
    private string _stashMessage = string.Empty;
    private string _operationDisplay = string.Empty;
    private string _rebaseOnto = "HEAD~3";
    private RebasePlanItem? _selectedRebaseItem;
    private string _rebaseAction = "pick";
    private string _rebaseMessage = string.Empty;
    private RepositoryOperation _currentOperation;
    private ConflictFile? _selectedConflict;
    private RepositoryOperationState _operationState = RepositoryOperationState.None;
    private string _mergeToolName = "vscode";
    private MergeToolConfigurationKind _mergeToolKind = MergeToolConfigurationKind.Preset;
    private GitConfigurationScope _mergeToolScope = GitConfigurationScope.RepositoryLocal;
    private string _mergeToolExecutable = string.Empty;
    private string _mergeToolArguments = string.Empty;
    private string _configuredMergeToolDisplay = "Merge tool is not configured";

    public OpenRepositoryViewModel(IFolderPicker folderPicker, IRepositoryService repositoryService, IHistoryService historyService, IRepositoryStateService stateService, IWorkingTreeService workingTreeService, IReferenceService referenceService, IRepositoryWorkflowService workflowService, ILogger<OpenRepositoryViewModel> logger)
    {
        _selectedScope = Scopes[0];
        _folderPicker = folderPicker;
        _repositoryService = repositoryService;
        _historyService = historyService;
        _stateService = stateService;
        _workingTreeService = workingTreeService;
        _referenceService = referenceService;
        _workflowService = workflowService;
        _logger = logger;
        _openRepositoryCommand = new AsyncCommand(OpenRepositoryAsync, () => !IsBusy && Repository is null);
        RefreshHistoryCommand = new AsyncCommand(() => LoadHistoryAsync(true), () => Repository is not null);
        LoadMoreCommand = new AsyncCommand(() => LoadHistoryAsync(false), () => Repository is not null && HasMore);
        RefreshAllCommand = new AsyncCommand(RefreshAllAsync, () => Repository is not null);
        StageCommand = new AsyncCommand(StageActiveAsync, () => CanBulkMutate() && SelectedWorkingTreeDiffKind == WorkingTreeDiffKind.Unstaged && SelectedChange is { IsUnstaged: true });
        UnstageCommand = new AsyncCommand(UnstageActiveAsync, () => CanBulkMutate() && SelectedWorkingTreeDiffKind == WorkingTreeDiffKind.Staged && SelectedChange is { IsStaged: true });
        StageSelectedCommand = new AsyncCommand(StageSelectedAsync, () => CanBulkMutate() && _selectedUnstagedChanges.Count > 0);
        StageAllCommand = new AsyncCommand(StageAllWorkingTreeAsync, () => CanBulkMutate() && Changes.Any(change => change.IsUnstaged));
        UnstageSelectedCommand = new AsyncCommand(UnstageSelectedAsync, () => CanBulkMutate() && _selectedStagedChanges.Count > 0);
        UnstageAllCommand = new AsyncCommand(UnstageAllWorkingTreeAsync, () => CanBulkMutate() && Changes.Any(change => change.IsStaged));
        CommitCommand = new AsyncCommand(RequestCommitAsync, () => CanMutate() && !string.IsNullOrWhiteSpace(CommitMessage));
        EmptyCommitCommand = new AsyncCommand(() => CommitAsync(false, true), () => CanMutate() && !string.IsNullOrWhiteSpace(CommitMessage));
        AmendCommand = new AsyncCommand(() => CommitAsync(true, false), () => CanMutate() && !string.IsNullOrWhiteSpace(CommitMessage));
        StageAllAndCommitCommand = new AsyncCommand(StageAllAndCommitAsync, () => CanBulkMutate() && IsEmptyIndexChoiceOpen);
        ConfirmEmptyCommitCommand = new AsyncCommand(ConfirmEmptyCommitAsync, () => CanMutate() && IsEmptyIndexChoiceOpen);
        CancelCommitCommand = new AsyncCommand(CancelCommitAsync, () => IsEmptyIndexChoiceOpen);
        RequestDiscardCommand = new AsyncCommand(RequestDiscardAsync, () => CanMutate() && _selectedUnstagedChanges.Count == 1 && _selectedUnstagedChanges[0].IsUnstaged);
        ConfirmDiscardCommand = new AsyncCommand(ConfirmDiscardAsync, () => CanMutate() && PendingDiscard is not null);
        CancelDiscardCommand = new AsyncCommand(CancelDiscardAsync, () => PendingDiscard is not null);
        SwitchBranchCommand = new AsyncCommand(() => MutateAsync(() => _referenceService.SwitchBranchAsync(Repository!, SelectedLocalBranch!.Name)), () => CanMutate() && SelectedLocalBranch is not null);
        CreateBranchCommand = new AsyncCommand(() => MutateAsync(() => _referenceService.CreateBranchAsync(Repository!, NewBranchName)), () => CanMutate() && !string.IsNullOrWhiteSpace(NewBranchName));
        DeleteBranchCommand = new AsyncCommand(() => MutateAsync(() => _referenceService.DeleteBranchAsync(Repository!, SelectedLocalBranch!.Name)), () => CanMutate() && SelectedLocalBranch is { IsCurrent: false });
        CheckoutRemoteCommand = new AsyncCommand(() => MutateAsync(() => _referenceService.CheckoutRemoteBranchAsync(Repository!, SelectedRemoteBranch!.Name, NewBranchName)), () => CanMutate() && SelectedRemoteBranch is not null && !string.IsNullOrWhiteSpace(NewBranchName));
        CheckoutTagCommand = new AsyncCommand(() => MutateAsync(() => _referenceService.CheckoutAsync(Repository!, SelectedTag!.Name)), () => CanMutate() && SelectedTag is not null);
        FetchCommand = new AsyncCommand(() => MutateAsync(() => _referenceService.FetchAsync(Repository!, SelectedRemote!.Name)), () => CanMutate() && SelectedRemote is not null);
        FetchAllCommand = new AsyncCommand(() => MutateAsync(() => _referenceService.FetchAllAsync(Repository!)), CanMutate);
        PullCommand = new AsyncCommand(() => MutateAsync(() => _referenceService.PullAsync(Repository!)), CanMutate);
        PushCommand = new AsyncCommand(() => MutateAsync(() => _referenceService.PushAsync(Repository!, string.IsNullOrWhiteSpace(PushBranchName) ? null : SelectedRemote?.Name, string.IsNullOrWhiteSpace(PushBranchName) ? null : PushBranchName, SetUpstream)), CanMutate);
        CreateStashCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.CreateStashAsync(Repository!, StashMessage)), CanMutate);
        ApplyStashCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.ApplyStashAsync(Repository!, SelectedStash!.Name)), () => CanMutate() && SelectedStash is not null);
        PopStashCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.PopStashAsync(Repository!, SelectedStash!.Name)), () => CanMutate() && SelectedStash is not null);
        MergeCommand = new AsyncCommand(MergeAsync, () => CanMutate() && SelectedMergeBranch is { IsCurrent: false });
        LoadRebasePlanCommand = new AsyncCommand(LoadRebasePlanAsync, () => CanMutate() && !string.IsNullOrWhiteSpace(RebaseOnto));
        ApplyRebaseItemCommand = new AsyncCommand(ApplyRebaseItemAsync, () => CanMutate() && SelectedRebaseItem is not null);
        MoveRebaseUpCommand = new AsyncCommand(() => MoveRebaseItemAsync(-1), () => CanMutate() && SelectedRebaseItem is not null && RebasePlan.IndexOf(SelectedRebaseItem) > 0);
        MoveRebaseDownCommand = new AsyncCommand(() => MoveRebaseItemAsync(1), () => CanMutate() && SelectedRebaseItem is not null && RebasePlan.IndexOf(SelectedRebaseItem) < RebasePlan.Count - 1);
        StartRebaseCommand = new AsyncCommand(StartRebaseAsync, () => CanMutate() && RebasePlan.Count > 0 && CurrentOperation == RepositoryOperation.None);
        ContinueRebaseCommand = new AsyncCommand(ContinueRebaseAsync, () => CanMutate() && CurrentOperation == RepositoryOperation.Rebase);
        AbortRebaseCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.AbortRebaseAsync(Repository!)), () => CanMutate() && CurrentOperation == RepositoryOperation.Rebase);
        OpenConflictCommand = new AsyncCommand(() => RunConflictActionAsync(() => _workflowService.OpenConflictAsync(Repository!, SelectedConflict!)), () => CanMutate() && SelectedConflict?.CanOpenManually == true);
        ChooseCurrentCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.ChooseConflictSideAsync(Repository!, SelectedConflict!, ConflictResolutionSide.CurrentLocal)), () => CanMutate() && SelectedConflict?.CanChooseCurrentLocal == true);
        ChooseIncomingCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.ChooseConflictSideAsync(Repository!, SelectedConflict!, ConflictResolutionSide.IncomingRemote)), () => CanMutate() && SelectedConflict?.CanChooseIncomingRemote == true);
        KeepDeletionCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.KeepConflictDeletionAsync(Repository!, SelectedConflict!)), () => CanMutate() && SelectedConflict?.CanKeepDeletion == true);
        StageConflictCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.StageResolvedConflictAsync(Repository!, SelectedConflict!)), () => CanMutate() && SelectedConflict?.CanStage == true);
        MergeToolCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.RunMergeToolForFileAsync(Repository!, SelectedConflict!)), () => CanMutate() && SelectedConflict?.CanRunMergeTool == true);
        MergeToolWorkflowCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.RunMergeToolWorkflowAsync(Repository!)), () => CanMutate() && Conflicts.Any(conflict => !conflict.IsResolved));
        ConfigureMergeToolCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.ConfigureMergeToolAsync(Repository!, new MergeToolConfiguration(MergeToolName, MergeToolKind, MergeToolScope, MergeToolExecutable, MergeToolArguments))), () => CanMutate() && !string.IsNullOrWhiteSpace(MergeToolName));
        ContinueOperationCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.ContinueOperationAsync(Repository!)), () => CanMutate() && OperationState.CanContinue);
        AbortOperationCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.AbortOperationAsync(Repository!)), () => CanMutate() && OperationState.CanAbort);
        SkipOperationCommand = new AsyncCommand(() => MutateAsync(() => _workflowService.SkipOperationAsync(Repository!)), () => CanMutate() && OperationState.CanSkip);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand OpenRepositoryCommand => _openRepositoryCommand;
    public ICommand RefreshHistoryCommand { get; }
    public ICommand LoadMoreCommand { get; }
    public ICommand RefreshAllCommand { get; }
    public ICommand StageCommand { get; }
    public ICommand UnstageCommand { get; }
    public ICommand StageSelectedCommand { get; }
    public ICommand StageAllCommand { get; }
    public ICommand UnstageSelectedCommand { get; }
    public ICommand UnstageAllCommand { get; }
    public ICommand CommitCommand { get; }
    public ICommand EmptyCommitCommand { get; }
    public ICommand AmendCommand { get; }
    public ICommand StageAllAndCommitCommand { get; }
    public ICommand ConfirmEmptyCommitCommand { get; }
    public ICommand CancelCommitCommand { get; }
    public ICommand RequestDiscardCommand { get; }
    public ICommand ConfirmDiscardCommand { get; }
    public ICommand CancelDiscardCommand { get; }
    public ICommand SwitchBranchCommand { get; }
    public ICommand CreateBranchCommand { get; }
    public ICommand DeleteBranchCommand { get; }
    public ICommand CheckoutRemoteCommand { get; }
    public ICommand CheckoutTagCommand { get; }
    public ICommand FetchCommand { get; }
    public ICommand FetchAllCommand { get; }
    public ICommand PullCommand { get; }
    public ICommand PushCommand { get; }
    public ICommand CreateStashCommand { get; }
    public ICommand ApplyStashCommand { get; }
    public ICommand PopStashCommand { get; }
    public ICommand MergeCommand { get; }
    public ICommand LoadRebasePlanCommand { get; }
    public ICommand ApplyRebaseItemCommand { get; }
    public ICommand MoveRebaseUpCommand { get; }
    public ICommand MoveRebaseDownCommand { get; }
    public ICommand StartRebaseCommand { get; }
    public ICommand ContinueRebaseCommand { get; }
    public ICommand AbortRebaseCommand { get; }
    public ICommand OpenConflictCommand { get; }
    public ICommand ChooseCurrentCommand { get; }
    public ICommand ChooseIncomingCommand { get; }
    public ICommand KeepDeletionCommand { get; }
    public ICommand StageConflictCommand { get; }
    public ICommand MergeToolCommand { get; }
    public ICommand MergeToolWorkflowCommand { get; }
    public ICommand ConfigureMergeToolCommand { get; }
    public ICommand ContinueOperationCommand { get; }
    public ICommand AbortOperationCommand { get; }
    public ICommand SkipOperationCommand { get; }
    public ObservableCollection<HistoryRow> History { get; } = [];
    public ObservableCollection<WorkingTreeChange> Changes { get; } = new BulkObservableCollection<WorkingTreeChange>();
    public IReadOnlyCollection<WorkingTreeChange> SelectedUnstagedChanges => _selectedUnstagedChanges;
    public IReadOnlyCollection<WorkingTreeChange> SelectedStagedChanges => _selectedStagedChanges;
    public ObservableCollection<GitBranch> LocalBranches { get; } = new BulkObservableCollection<GitBranch>();
    public ObservableCollection<GitBranch> RemoteBranches { get; } = new BulkObservableCollection<GitBranch>();
    public ObservableCollection<GitRemote> Remotes { get; } = new BulkObservableCollection<GitRemote>();
    public ObservableCollection<GitTag> Tags { get; } = new BulkObservableCollection<GitTag>();
    public ObservableCollection<GitStash> Stashes { get; } = new BulkObservableCollection<GitStash>();
    public ObservableCollection<RebasePlanItem> RebasePlan { get; } = [];
    public ObservableCollection<ConflictFile> Conflicts { get; } = new BulkObservableCollection<ConflictFile>();
    public IReadOnlyList<string> RebaseActions { get; } = ["pick", "reword", "squash", "fixup", "drop"];
    public IReadOnlyList<UiChoice<HistoryScope>> Scopes { get; } =
    [
        new("All references", HistoryScope.AllReferences),
        new("Current branch", HistoryScope.CurrentBranch)
    ];
    public IReadOnlyList<string> MergeToolNames { get; } = MergeToolPresets.Known;
    public IReadOnlyList<MergeToolConfigurationKind> MergeToolKinds { get; } = Enum.GetValues<MergeToolConfigurationKind>();
    public IReadOnlyList<GitConfigurationScope> MergeToolScopes { get; } = Enum.GetValues<GitConfigurationScope>();
    public Repository? Repository { get => _repository; private set { if (ReferenceEquals(_repository, value)) return; ResetCommitChangesSession(); _repository = value; Notify(); Notify(nameof(RepositoryVisibility)); Notify(nameof(PickerVisibility)); Notify(nameof(RepositoryKind)); Notify(nameof(CanForcePushWithLease)); ((AsyncCommand)RefreshHistoryCommand).RaiseCanExecuteChanged(); } }
    public string? ErrorMessage { get => _errorMessage; private set { _errorMessage = value; Notify(); Notify(nameof(HasError)); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; Notify(); Notify(nameof(BusyVisibility)); Notify(nameof(CanForcePushWithLease)); _openRepositoryCommand.RaiseCanExecuteChanged(); ((AsyncCommand)RefreshHistoryCommand).RaiseCanExecuteChanged(); ((AsyncCommand)LoadMoreCommand).RaiseCanExecuteChanged(); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RepositoryVisibility => Repository is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PickerVisibility => Repository is null ? Visibility.Visible : Visibility.Collapsed;
    public string RepositoryKind => Repository?.IsWorktree == true ? "Git worktree" : "Git repository";
    public string FilterText { get => _filterText; set { _filterText = value; Notify(); } }
    public UiChoice<HistoryScope> SelectedScope { get => _selectedScope; set { if (_selectedScope == value) return; _selectedScope = value; Notify(); _ = LoadHistoryAsync(true); } }
    public HistoryRow? SelectedHistoryRow { get => _selectedHistoryRow; set { if (ReferenceEquals(_selectedHistoryRow, value)) return; _selectedHistoryRow = value; Notify(); Notify(nameof(DetailsVisibility)); OnSelectedHistoryRowChanged(); } }
    public ChangedFile? SelectedFile { get => _selectedFile; set { if (_selectedFile == value) return; _selectedFile = value; Notify(); OnSelectedFileChanged(); } }
    public FileDiff? SelectedDiff { get => _selectedDiff; private set { _selectedDiff = value; Notify(); Notify(nameof(DiffVisibility)); Notify(nameof(BinaryVisibility)); } }
    public bool IsDiffLoading { get => _isDiffLoading; private set { if (_isDiffLoading == value) return; _isDiffLoading = value; Notify(); Notify(nameof(DiffLoadingVisibility)); } }
    public bool HasMore { get => _hasMore; private set { _hasMore = value; Notify(); ((AsyncCommand)LoadMoreCommand).RaiseCanExecuteChanged(); } }
    public Visibility DetailsVisibility => SelectedHistoryRow is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DiffLoadingVisibility => IsDiffLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DiffVisibility => SelectedDiff is { IsBinary: false } ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BinaryVisibility => SelectedDiff?.IsBinary == true ? Visibility.Visible : Visibility.Collapsed;
    public WorkingTreeChange? SelectedChange
    {
        get => _selectedChange;
        set
        {
            if (ReferenceEquals(_selectedChange, value)) return;
            _selectedChange = value;
            Notify();
            Notify(nameof(ActiveWorkingTreeChange));
            RaiseCommands();
        }
    }
    public WorkingTreeChange? ActiveWorkingTreeChange { get => SelectedChange; set => SelectedChange = value; }
    public WorkingTreeDiffKind? SelectedWorkingTreeDiffKind
    {
        get => _selectedWorkingTreeDiffKind;
        set
        {
            if (_selectedWorkingTreeDiffKind == value) return;
            _selectedWorkingTreeDiffKind = value;
            Notify();
            Notify(nameof(ActiveWorkingTreeDiffKind));
            RaiseCommands();
        }
    }
    public WorkingTreeDiffKind? ActiveWorkingTreeDiffKind { get => SelectedWorkingTreeDiffKind; set => SelectedWorkingTreeDiffKind = value; }
    public FileDiff? SelectedWorkingTreeDiff
    {
        get => _selectedWorkingTreeDiff;
        set
        {
            if (ReferenceEquals(_selectedWorkingTreeDiff, value)) return;
            _selectedWorkingTreeDiff = value;
            Notify();
        }
    }
    public WorkingTreeChange? PendingDiscard { get => _pendingDiscard; private set { _pendingDiscard = value; Notify(); Notify(nameof(DiscardConfirmationVisibility)); Notify(nameof(DiscardConfirmationMessage)); RaiseCommands(); } }
    public Visibility DiscardConfirmationVisibility => PendingDiscard is null ? Visibility.Collapsed : Visibility.Visible;
    public string DiscardConfirmationMessage => PendingDiscard is null
        ? string.Empty
        : $"Discard unstaged changes in '{PendingDiscard.Path}'? This restores the working-tree version from the index (or deletes an untracked file). Staged changes are preserved.";
    public string CommitMessage { get => _commitMessage; set { _commitMessage = value; Notify(); RaiseCommands(); } }
    public bool HasUnappliedCommitMessage => CommitMessage.Length > 0;
    public bool IsEmptyIndexChoiceOpen { get => _isEmptyIndexChoiceOpen; private set { _isEmptyIndexChoiceOpen = value; Notify(); Notify(nameof(EmptyIndexChoiceVisibility)); RaiseCommands(); } }
    public Visibility EmptyIndexChoiceVisibility => IsEmptyIndexChoiceOpen ? Visibility.Visible : Visibility.Collapsed;
    public GitBranch? SelectedLocalBranch { get => _selectedLocalBranch; set { _selectedLocalBranch = value; Notify(); RaiseCommands(); } }
    public GitBranch? SelectedRemoteBranch { get => _selectedRemoteBranch; set { _selectedRemoteBranch = value; Notify(); RaiseCommands(); } }
    public GitRemote? SelectedRemote { get => _selectedRemote; set { _selectedRemote = value; Notify(); RaiseCommands(); } }
    public GitTag? SelectedTag { get => _selectedTag; set { _selectedTag = value; Notify(); RaiseCommands(); } }
    public string NewBranchName { get => _newBranchName; set { _newBranchName = value; Notify(); RaiseCommands(); } }
    public string PushBranchName { get => _pushBranchName; set { _pushBranchName = value; Notify(); } }
    public bool SetUpstream { get => _setUpstream; set { _setUpstream = value; Notify(); } }
    public string HeadDisplay { get => _headDisplay; private set { _headDisplay = value; Notify(); } }
    public GitStash? SelectedStash { get => _selectedStash; set { _selectedStash = value; Notify(); RaiseCommands(); } }
    public GitBranch? SelectedMergeBranch { get => _selectedMergeBranch; set { _selectedMergeBranch = value; Notify(); RaiseCommands(); } }
    public string StashMessage { get => _stashMessage; set { _stashMessage = value; Notify(); } }
    public string OperationDisplay { get => _operationDisplay; private set { _operationDisplay = value; Notify(); } }
    public string RebaseOnto { get => _rebaseOnto; set { _rebaseOnto = value; Notify(); RaiseCommands(); } }
    public RebasePlanItem? SelectedRebaseItem { get => _selectedRebaseItem; set { _selectedRebaseItem = value; if (value is not null) { RebaseAction = value.Action.ToString().ToLowerInvariant(); RebaseMessage = value.NewMessage ?? value.Subject; } Notify(); RaiseCommands(); } }
    public string RebaseAction { get => _rebaseAction; set { _rebaseAction = value; Notify(); } }
    public string RebaseMessage { get => _rebaseMessage; set { _rebaseMessage = value; Notify(); } }
    public RepositoryOperation CurrentOperation { get => _currentOperation; private set { _currentOperation = value; Notify(); Notify(nameof(CanForcePushWithLease)); RaiseCommands(); } }
    public ConflictFile? SelectedConflict { get => _selectedConflict; set { _selectedConflict = value; Notify(); Notify(nameof(CurrentSideLabel)); Notify(nameof(IncomingSideLabel)); RaiseCommands(); } }
    public RepositoryOperationState OperationState { get => _operationState; private set { _operationState = value; Notify(); Notify(nameof(OperationVisibility)); RaiseCommands(); } }
    public string CurrentSideLabel => SelectedConflict?.CurrentLocalLabel ?? "Current/local";
    public string IncomingSideLabel => SelectedConflict?.IncomingRemoteLabel ?? "Incoming/remote";
    public Visibility OperationVisibility => OperationState.Kind == RepositoryOperation.None ? Visibility.Collapsed : Visibility.Visible;
    public bool CanForcePushWithLease => Repository is not null && !IsBusy && CurrentOperation == RepositoryOperation.None && LocalBranches.Any(branch => branch.IsCurrent);
    public string MergeToolName { get => _mergeToolName; set { _mergeToolName = value; Notify(); RaiseCommands(); } }
    public MergeToolConfigurationKind MergeToolKind { get => _mergeToolKind; set { _mergeToolKind = value; Notify(); } }
    public GitConfigurationScope MergeToolScope { get => _mergeToolScope; set { _mergeToolScope = value; Notify(); } }
    public string MergeToolExecutable { get => _mergeToolExecutable; set { _mergeToolExecutable = value; Notify(); } }
    public string MergeToolArguments { get => _mergeToolArguments; set { _mergeToolArguments = value; Notify(); } }
    public string ConfiguredMergeToolDisplay { get => _configuredMergeToolDisplay; private set { _configuredMergeToolDisplay = value; Notify(); } }

    public Task RefreshWhenActivatedAsync() => Repository is null ? Task.CompletedTask : RefreshAllAsync();

    internal Task OpenRepositoryAsyncForDesktopCheck() => _openRepositoryCommand.ExecuteAsync();

    internal Task RefreshAsyncForDesktopCheck() => RefreshAllAsync();

    internal Task<bool> RunMutationAsync(Func<Task> mutation, string? errorContext = null, bool includeHistory = true) =>
        MutateAsync(mutation, errorContext, includeHistory: includeHistory);

    internal void SetWorkingTreeSelection(WorkingTreeDiffKind kind, IEnumerable<WorkingTreeChange> changes)
    {
        var selected = changes.ToArray();
        var target = kind == WorkingTreeDiffKind.Unstaged ? _selectedUnstagedChanges : _selectedStagedChanges;
        target.Clear();
        foreach (var change in selected) target.Add(change);
        Notify(kind == WorkingTreeDiffKind.Unstaged ? nameof(SelectedUnstagedChanges) : nameof(SelectedStagedChanges));
        RaiseCommands();
    }

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

            if (path is null)
            {
                return;
            }

            Repository = await _repositoryService.OpenAsync(path);
            await RefreshAllAsync();
            _logger.LogInformation("Opened repository at {RepositoryRoot}", Repository.RepositoryRoot);
        }
        catch (RepositoryOpenException exception)
        {
            ErrorMessage = exception.Message;
            _logger.LogWarning(exception, "Repository selection failed");
        }
        catch (Exception exception)
        {
            ErrorMessage = $"The repository could not be opened: {exception.Message}";
            _logger.LogError(exception, "Repository opening failed");
        }
        finally
        {
            ExitBusy();
            _openRepositoryCommand.RaiseCanExecuteChanged();
        }
    }

    private bool CanMutate() => Repository is not null && !_isMutating;

    private bool CanBulkMutate() => CanMutate() && !Conflicts.Any(conflict => !conflict.IsResolved);

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

    private async Task RefreshStateAsync(bool includeHistory)
    {
        if (Repository is null) return;
        EnterBusy();
        try
        {
            var repository = Repository;
            var state = await _stateService.ReadAsync(repository);
            if (!ReferenceEquals(repository, Repository)) return;

            var shortHead = state.HeadCommit is { } commit ? commit[..Math.Min(10, commit.Length)] : "no commit";
            HeadDisplay = state.IsDetached ? $"Detached HEAD: {shortHead}" : $"Current branch: {state.HeadReference}";
            Replace(Changes, state.Changes);
            Replace(LocalBranches, state.Refs.LocalBranches);
            Notify(nameof(CanForcePushWithLease));
            Replace(RemoteBranches, state.Refs.RemoteBranches);
            Replace(Remotes, state.Refs.Remotes);
            Replace(Tags, state.Refs.Tags);
            Replace(Stashes, state.Stashes);
            SelectedLocalBranch = LocalBranches.FirstOrDefault(branch => branch.IsCurrent) ?? LocalBranches.FirstOrDefault();
            SelectedMergeBranch = LocalBranches.FirstOrDefault(branch => !branch.IsCurrent);
            SelectedStash = Stashes.FirstOrDefault();
            OperationDisplay = state.Operation == RepositoryOperation.None ? "No operation in progress" : $"Operation in progress: {state.Operation}";
            CurrentOperation = state.Operation;
            OperationState = state.CurrentOperation;
            var configuredTool = state.LocalConfiguration.GetValueOrDefault("merge.tool") ?? state.GlobalConfiguration.GetValueOrDefault("merge.tool");
            ConfiguredMergeToolDisplay = configuredTool is null ? "Merge tool is not configured" : $"Active merge tool: {configuredTool}";
            Replace(Conflicts, state.CurrentOperation.Conflicts);
            SelectedConflict = Conflicts.FirstOrDefault();
            SelectedRemote = SelectedRemote is null
                ? Remotes.FirstOrDefault()
                : Remotes.FirstOrDefault(remote => string.Equals(remote.Name, SelectedRemote.Name, StringComparison.Ordinal))
                  ?? Remotes.FirstOrDefault();

            if (includeHistory)
                await LoadHistoryAsync(true);
        }
        finally
        {
            ExitBusy();
        }
    }

    private async Task<bool> MutateAsync(Func<Task> mutation, string? errorContext = null, Action? beforeMutation = null, bool includeHistory = true)
    {
        var succeeded = false;
        if (!await _mutationGate.WaitAsync(0)) return false;
        _isMutating = true;
        EnterBusy();
        RaiseCommands();
        ErrorMessage = null;
        try
        {
            beforeMutation?.Invoke();
            Exception? failure = null;
            try { await mutation(); }
            catch (Exception exception) when (exception is not OperationCanceledException) { failure = exception; }
            try { await RefreshStateAsync(includeHistory); }
            catch (Exception exception) when (exception is not OperationCanceledException) { failure ??= exception; }
            if (failure is not null)
                ErrorMessage = errorContext is null ? $"Git: {failure.Message}" : $"{errorContext}\nGit: {failure.Message}";
            succeeded = failure is null;
        }
        finally
        {
            _isMutating = false;
            ExitBusy();
            _mutationGate.Release();
            RaiseCommands();
        }
        return succeeded;
    }

    private async Task RunConflictActionAsync(Func<Task> action)
    {
        await action();
        await RefreshAllAsync();
    }

    private async Task MergeAsync()
    {
        MergeResult? result = null;
        await MutateAsync(async () => { result = await _workflowService.MergeAsync(Repository!, SelectedMergeBranch!.Name); });
        if (result is not null) OperationDisplay = result.Message;
    }

    private async Task LoadRebasePlanAsync()
    {
        EnterBusy();
        ErrorMessage = null;
        try
        {
            var plan = await _workflowService.ReadInteractiveRebasePlanAsync(Repository!, RebaseOnto);
            RebaseOnto = plan.Onto;
            Replace(RebasePlan, plan.Items);
            SelectedRebaseItem = RebasePlan.FirstOrDefault();
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { ErrorMessage = $"Git: {exception.Message}"; }
        finally { ExitBusy(); RaiseCommands(); }
    }

    private Task ApplyRebaseItemAsync()
    {
        if (SelectedRebaseItem is null || !Enum.TryParse<CSharpGit.Domain.RebaseAction>(RebaseAction, true, out var action)) return Task.CompletedTask;
        var index = RebasePlan.IndexOf(SelectedRebaseItem);
        var updated = SelectedRebaseItem with { Action = action, NewMessage = action == CSharpGit.Domain.RebaseAction.Reword ? RebaseMessage : null };
        RebasePlan[index] = updated;
        SelectedRebaseItem = updated;
        return Task.CompletedTask;
    }

    private Task MoveRebaseItemAsync(int offset)
    {
        if (SelectedRebaseItem is null) return Task.CompletedTask;
        var oldIndex = RebasePlan.IndexOf(SelectedRebaseItem);
        var newIndex = oldIndex + offset;
        if (newIndex >= 0 && newIndex < RebasePlan.Count) RebasePlan.Move(oldIndex, newIndex);
        RaiseCommands();
        return Task.CompletedTask;
    }

    private async Task StartRebaseAsync()
    {
        RebaseResult? result = null;
        var plan = new InteractiveRebasePlan(RebaseOnto, RebasePlan.ToList());
        await MutateAsync(async () => result = await _workflowService.StartInteractiveRebaseAsync(Repository!, plan));
        if (result is not null) OperationDisplay = result.Message;
    }

    private async Task ContinueRebaseAsync()
    {
        RebaseResult? result = null;
        await MutateAsync(async () => result = await _workflowService.ContinueRebaseAsync(Repository!));
        if (result is not null) OperationDisplay = result.Message;
    }

    private Task RequestCommitAsync()
    {
        if (!Changes.Any(change => change.IsStaged) && Changes.Any(change => change.IsUnstaged))
        {
            IsEmptyIndexChoiceOpen = true;
            return Task.CompletedTask;
        }

        return CommitAsync(false, false);
    }

    private async Task StageActiveAsync()
    {
        var change = SelectedChange;
        if (change is null) return;
        await MutateAsync(
            () => _workingTreeService.StageFileAsync(Repository!, change),
            "Could not stage file",
            includeHistory: false);
    }

    private async Task UnstageActiveAsync()
    {
        var change = SelectedChange;
        if (change is null) return;
        await MutateAsync(
            () => _workingTreeService.UnstageFileAsync(Repository!, change),
            "Could not unstage file",
            includeHistory: false);
    }

    private async Task StageSelectedAsync()
    {
        var changes = _selectedUnstagedChanges.ToArray();
        if (changes.Length == 0) return;
        await MutateAsync(
            () => _workingTreeService.StageFilesAsync(Repository!, changes),
            "Could not stage selected files",
            includeHistory: false);
    }

    private async Task StageAllWorkingTreeAsync()
    {
        await MutateAsync(
            () => _workingTreeService.StageAllAsync(Repository!),
            "Could not stage all files",
            includeHistory: false);
    }

    private async Task UnstageSelectedAsync()
    {
        var changes = _selectedStagedChanges.ToArray();
        if (changes.Length == 0) return;
        await MutateAsync(
            () => _workingTreeService.UnstageFilesAsync(Repository!, changes),
            "Could not unstage selected files",
            includeHistory: false);
    }

    private async Task UnstageAllWorkingTreeAsync()
    {
        await MutateAsync(
            () => _workingTreeService.UnstageAllAsync(Repository!),
            "Could not unstage all files",
            includeHistory: false);
    }

    private void ClearWorkingTreePresentationSelection()
    {
        _selectedUnstagedChanges.Clear();
        _selectedStagedChanges.Clear();
        Notify(nameof(SelectedUnstagedChanges));
        Notify(nameof(SelectedStagedChanges));
        SelectedChange = null;
        SelectedWorkingTreeDiffKind = null;
        SelectedWorkingTreeDiff = null;
        PendingDiscard = null;
        RaiseCommands();
    }

    private async Task StageAllAndCommitAsync()
    {
        IsEmptyIndexChoiceOpen = false;
        var message = CommitMessage;
        if (await MutateAsync(async () =>
        {
            await _workingTreeService.StageAllAsync(Repository!);
            await _workingTreeService.CommitAsync(Repository!, message);
        }, "Could not stage all files and commit", ClearWorkingTreePresentationSelection) && CommitMessage == message) CommitMessage = string.Empty;
    }

    private Task ConfirmEmptyCommitAsync()
    {
        IsEmptyIndexChoiceOpen = false;
        return CommitAsync(false, true);
    }

    private Task CancelCommitAsync()
    {
        IsEmptyIndexChoiceOpen = false;
        return Task.CompletedTask;
    }

    private async Task CommitAsync(bool amend, bool empty)
    {
        var message = CommitMessage;
        if (await MutateAsync(() => _workingTreeService.CommitAsync(Repository!, message, amend, empty)) && CommitMessage == message)
            CommitMessage = string.Empty;
    }

    private Task RequestDiscardAsync()
    {
        PendingDiscard = _selectedUnstagedChanges.Count == 1 ? _selectedUnstagedChanges[0] : null;
        return Task.CompletedTask;
    }

    private Task CancelDiscardAsync() { PendingDiscard = null; return Task.CompletedTask; }

    private async Task ConfirmDiscardAsync()
    {
        var change = PendingDiscard;
        PendingDiscard = null;
        if (change is not null)
            await MutateAsync(
                () => _workingTreeService.DiscardFileAsync(Repository!, change),
                "Could not discard file",
                includeHistory: false);
    }

    private void RaiseCommands()
    {
        foreach (var command in new[] { RefreshAllCommand, StageCommand, UnstageCommand, StageSelectedCommand, StageAllCommand, UnstageSelectedCommand, UnstageAllCommand, CommitCommand, EmptyCommitCommand, AmendCommand, StageAllAndCommitCommand, ConfirmEmptyCommitCommand, CancelCommitCommand, RequestDiscardCommand, ConfirmDiscardCommand, CancelDiscardCommand, SwitchBranchCommand, CreateBranchCommand, DeleteBranchCommand, CheckoutRemoteCommand, CheckoutTagCommand, FetchCommand, FetchAllCommand, PullCommand, PushCommand, CreateStashCommand, ApplyStashCommand, PopStashCommand, MergeCommand, LoadRebasePlanCommand, ApplyRebaseItemCommand, MoveRebaseUpCommand, MoveRebaseDownCommand, StartRebaseCommand, ContinueRebaseCommand, AbortRebaseCommand, OpenConflictCommand, ChooseCurrentCommand, ChooseIncomingCommand, KeepDeletionCommand, StageConflictCommand, MergeToolCommand, MergeToolWorkflowCommand, ConfigureMergeToolCommand, ContinueOperationCommand, AbortOperationCommand, SkipOperationCommand }.OfType<AsyncCommand>()) command.RaiseCanExecuteChanged();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        var snapshot = values.ToArray();
        if (target.SequenceEqual(snapshot)) return;

        if (target is BulkObservableCollection<T> bulk)
        {
            bulk.ReplaceAll(snapshot);
            return;
        }

        target.Clear();
        foreach (var value in snapshot) target.Add(value);
    }

    internal void InvalidateHistoryLoad()
    {
        Interlocked.Increment(ref _historyLoadGeneration);
        _historyLoadCts?.Cancel();
    }

    private async Task LoadHistoryAsync(bool reset)
    {
        if (Repository is null) return;

        var repository = Repository;
        var selectedHash = reset ? SelectedHistoryRow?.Commit.Hash : null;
        var skip = reset ? 0 : History.Count;
        var scope = SelectedScope.Value;
        var filter = FilterText;
        var generation = Interlocked.Increment(ref _historyLoadGeneration);
        var cancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _historyLoadCts, cancellation);
        if (previousCancellation is not null)
        {
            previousCancellation.Cancel();
            previousCancellation.Dispose();
        }

        EnterBusy();
        try
        {
            var page = await _historyService.ReadHistoryAsync(
                repository,
                new HistoryQuery(scope, filter, skip),
                cancellation.Token);

            if (cancellation.IsCancellationRequested
                || generation != Volatile.Read(ref _historyLoadGeneration)
                || !ReferenceEquals(repository, Repository))
                return;

            if (reset) History.Clear();
            foreach (var row in page.Rows) History.Add(row);
            HasMore = page.HasMore;

            if (reset)
            {
                var restored = selectedHash is null
                    ? History.FirstOrDefault()
                    : History.FirstOrDefault(row => string.Equals(row.Commit.Hash, selectedHash, StringComparison.Ordinal))
                      ?? History.FirstOrDefault();
                SelectedHistoryRow = restored;
                if (restored is null)
                {
                    SelectedFile = null;
                    SelectedDiff = null;
                }
            }
            else if (SelectedHistoryRow is null && History.FirstOrDefault() is { } first)
            {
                SelectedHistoryRow = first;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (generation != Volatile.Read(ref _historyLoadGeneration)) return;
            ErrorMessage = $"Could not read history: {exception.Message}";
            _logger.LogWarning(exception, "History loading failed");
        }
        finally { ExitBusy(); }
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
