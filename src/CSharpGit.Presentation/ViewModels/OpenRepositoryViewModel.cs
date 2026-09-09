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

public sealed class OpenRepositoryViewModel : INotifyPropertyChanged
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
    private long _commitLoadGeneration;
    private long _diffLoadGeneration;
    private ElementTheme _selectedTheme = ElementTheme.Default;
    private string _filterText = string.Empty;
    private UiChoice<HistoryScope> _selectedScope;
    private HistoryRow? _selectedHistoryRow;
    private CommitDetails? _selectedCommit;
    private ChangedFile? _selectedFile;
    private FileDiff? _selectedDiff;
    private bool _hasMore;
    private WorkingTreeChange? _selectedChange;
    private WorkingTreeChange? _pendingDiscard;
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
        StageCommand = new AsyncCommand(() => MutateAsync(() => _workingTreeService.StageFileAsync(Repository!, SelectedChange!)), () => CanMutate() && SelectedChange is { IsUnstaged: true });
        UnstageCommand = new AsyncCommand(() => MutateAsync(() => _workingTreeService.UnstageFileAsync(Repository!, SelectedChange!)), () => CanMutate() && SelectedChange is { IsStaged: true });
        CommitCommand = new AsyncCommand(RequestCommitAsync, () => CanMutate() && !string.IsNullOrWhiteSpace(CommitMessage));
        EmptyCommitCommand = new AsyncCommand(() => CommitAsync(false, true), () => CanMutate() && !string.IsNullOrWhiteSpace(CommitMessage));
        AmendCommand = new AsyncCommand(() => CommitAsync(true, false), () => CanMutate() && !string.IsNullOrWhiteSpace(CommitMessage));
        StageAllAndCommitCommand = new AsyncCommand(StageAllAndCommitAsync, () => CanMutate() && IsEmptyIndexChoiceOpen);
        ConfirmEmptyCommitCommand = new AsyncCommand(ConfirmEmptyCommitAsync, () => CanMutate() && IsEmptyIndexChoiceOpen);
        CancelCommitCommand = new AsyncCommand(CancelCommitAsync, () => IsEmptyIndexChoiceOpen);
        RequestDiscardCommand = new AsyncCommand(RequestDiscardAsync, () => CanMutate() && SelectedChange is not null);
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
    public ObservableCollection<WorkingTreeChange> Changes { get; } = [];
    public ObservableCollection<GitBranch> LocalBranches { get; } = [];
    public ObservableCollection<GitBranch> RemoteBranches { get; } = [];
    public ObservableCollection<GitRemote> Remotes { get; } = [];
    public ObservableCollection<GitTag> Tags { get; } = [];
    public ObservableCollection<GitStash> Stashes { get; } = [];
    public ObservableCollection<RebasePlanItem> RebasePlan { get; } = [];
    public ObservableCollection<ConflictFile> Conflicts { get; } = [];
    public IReadOnlyList<string> RebaseActions { get; } = ["pick", "reword", "squash", "fixup", "drop"];
    public IReadOnlyList<UiChoice<HistoryScope>> Scopes { get; } =
    [
        new("All references", HistoryScope.AllReferences),
        new("Current branch", HistoryScope.CurrentBranch)
    ];
    public IReadOnlyList<string> MergeToolNames { get; } = MergeToolPresets.Known;
    public IReadOnlyList<MergeToolConfigurationKind> MergeToolKinds { get; } = Enum.GetValues<MergeToolConfigurationKind>();
    public IReadOnlyList<GitConfigurationScope> MergeToolScopes { get; } = Enum.GetValues<GitConfigurationScope>();
    public Repository? Repository { get => _repository; private set { _repository = value; Notify(); Notify(nameof(RepositoryVisibility)); Notify(nameof(PickerVisibility)); Notify(nameof(RepositoryKind)); ((AsyncCommand)RefreshHistoryCommand).RaiseCanExecuteChanged(); } }
    public string? ErrorMessage { get => _errorMessage; private set { _errorMessage = value; Notify(); Notify(nameof(HasError)); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; Notify(); Notify(nameof(BusyVisibility)); _openRepositoryCommand.RaiseCanExecuteChanged(); ((AsyncCommand)RefreshHistoryCommand).RaiseCanExecuteChanged(); ((AsyncCommand)LoadMoreCommand).RaiseCanExecuteChanged(); } }
    public IReadOnlyList<UiChoice<ElementTheme>> Themes { get; } =
    [
        new("System", ElementTheme.Default),
        new("Light", ElementTheme.Light),
        new("Dark", ElementTheme.Dark)
    ];
    public ElementTheme SelectedTheme { get => _selectedTheme; set { _selectedTheme = value; Notify(); Notify(nameof(SelectedThemeName)); } }
    public UiChoice<ElementTheme> SelectedThemeName
    {
        get => Themes.First(theme => theme.Value == SelectedTheme);
        set
        {
            SelectedTheme = value.Value;
            Notify();
        }
    }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RepositoryVisibility => Repository is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PickerVisibility => Repository is null ? Visibility.Visible : Visibility.Collapsed;
    public string RepositoryKind => Repository?.IsWorktree == true ? "Git worktree" : "Git repository";
    public string FilterText { get => _filterText; set { _filterText = value; Notify(); } }
    public UiChoice<HistoryScope> SelectedScope { get => _selectedScope; set { if (_selectedScope == value) return; _selectedScope = value; Notify(); _ = LoadHistoryAsync(true); } }
    public HistoryRow? SelectedHistoryRow { get => _selectedHistoryRow; set { if (ReferenceEquals(_selectedHistoryRow, value)) return; _selectedHistoryRow = value; Notify(); _ = LoadCommitAsync(); } }
    public CommitDetails? SelectedCommit { get => _selectedCommit; private set { _selectedCommit = value; Notify(); Notify(nameof(DetailsVisibility)); } }
    public ChangedFile? SelectedFile { get => _selectedFile; set { if (_selectedFile == value) return; _selectedFile = value; Notify(); _ = LoadDiffAsync(); } }
    public FileDiff? SelectedDiff { get => _selectedDiff; private set { _selectedDiff = value; Notify(); Notify(nameof(DiffVisibility)); Notify(nameof(BinaryVisibility)); } }
    public bool HasMore { get => _hasMore; private set { _hasMore = value; Notify(); ((AsyncCommand)LoadMoreCommand).RaiseCanExecuteChanged(); } }
    public Visibility DetailsVisibility => SelectedCommit is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DiffVisibility => SelectedDiff is { IsBinary: false } ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BinaryVisibility => SelectedDiff?.IsBinary == true ? Visibility.Visible : Visibility.Collapsed;
    public WorkingTreeChange? SelectedChange { get => _selectedChange; set { _selectedChange = value; Notify(); RaiseCommands(); } }
    public WorkingTreeChange? PendingDiscard { get => _pendingDiscard; private set { _pendingDiscard = value; Notify(); Notify(nameof(DiscardConfirmationVisibility)); RaiseCommands(); } }
    public Visibility DiscardConfirmationVisibility => PendingDiscard is null ? Visibility.Collapsed : Visibility.Visible;
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
    public RepositoryOperation CurrentOperation { get => _currentOperation; private set { _currentOperation = value; Notify(); RaiseCommands(); } }
    public ConflictFile? SelectedConflict { get => _selectedConflict; set { _selectedConflict = value; Notify(); Notify(nameof(CurrentSideLabel)); Notify(nameof(IncomingSideLabel)); RaiseCommands(); } }
    public RepositoryOperationState OperationState { get => _operationState; private set { _operationState = value; Notify(); Notify(nameof(OperationVisibility)); RaiseCommands(); } }
    public string CurrentSideLabel => SelectedConflict?.CurrentLocalLabel ?? "Current/local";
    public string IncomingSideLabel => SelectedConflict?.IncomingRemoteLabel ?? "Incoming/remote";
    public Visibility OperationVisibility => OperationState.Kind == RepositoryOperation.None ? Visibility.Collapsed : Visibility.Visible;
    public string MergeToolName { get => _mergeToolName; set { _mergeToolName = value; Notify(); RaiseCommands(); } }
    public MergeToolConfigurationKind MergeToolKind { get => _mergeToolKind; set { _mergeToolKind = value; Notify(); } }
    public GitConfigurationScope MergeToolScope { get => _mergeToolScope; set { _mergeToolScope = value; Notify(); } }
    public string MergeToolExecutable { get => _mergeToolExecutable; set { _mergeToolExecutable = value; Notify(); } }
    public string MergeToolArguments { get => _mergeToolArguments; set { _mergeToolArguments = value; Notify(); } }
    public string ConfiguredMergeToolDisplay { get => _configuredMergeToolDisplay; private set { _configuredMergeToolDisplay = value; Notify(); } }

    public Task RefreshWhenActivatedAsync() => Repository is null ? Task.CompletedTask : RefreshAllAsync();

    internal Task OpenRepositoryAsyncForDesktopCheck() => _openRepositoryCommand.ExecuteAsync();

    internal Task RefreshAsyncForDesktopCheck() => RefreshAllAsync();

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

    private async Task RefreshAllAsync()
    {
        if (Repository is null) return;
        EnterBusy();
        try
        {
        Repository = await _repositoryService.OpenAsync(Repository.WorkingDirectory);
        var state = await _stateService.ReadAsync(Repository);
        var shortHead = state.HeadCommit is { } commit ? commit[..Math.Min(10, commit.Length)] : "no commit";
        HeadDisplay = state.IsDetached ? $"Detached HEAD: {shortHead}" : $"Current branch: {state.HeadReference}";
        Changes.Clear();
        foreach (var change in state.Changes) Changes.Add(change);
        SelectedChange = Changes.FirstOrDefault();
        Replace(LocalBranches, state.Refs.LocalBranches);
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
        SelectedRemote ??= Remotes.FirstOrDefault();
        await LoadHistoryAsync(true);
        }
        finally { ExitBusy(); }
    }

    private async Task<bool> MutateAsync(Func<Task> mutation)
    {
        var succeeded = false;
        await _mutationGate.WaitAsync();
        _isMutating = true;
        EnterBusy();
        RaiseCommands();
        ErrorMessage = null;
        try
        {
            Exception? failure = null;
            try { await mutation(); }
            catch (Exception exception) when (exception is not OperationCanceledException) { failure = exception; }
            try { await RefreshAllAsync(); }
            catch (Exception exception) when (exception is not OperationCanceledException) { failure ??= exception; }
            if (failure is not null) ErrorMessage = $"Git: {failure.Message}";
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

    private async Task StageAllAndCommitAsync()
    {
        IsEmptyIndexChoiceOpen = false;
        var message = CommitMessage;
        if (await MutateAsync(async () =>
        {
            await _workingTreeService.StageAllAsync(Repository!);
            await _workingTreeService.CommitAsync(Repository!, message);
        }) && CommitMessage == message) CommitMessage = string.Empty;
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
    private Task RequestDiscardAsync() { PendingDiscard = SelectedChange; return Task.CompletedTask; }
    private Task CancelDiscardAsync() { PendingDiscard = null; return Task.CompletedTask; }
    private async Task ConfirmDiscardAsync()
    {
        var change = PendingDiscard;
        PendingDiscard = null;
        if (change is not null) await MutateAsync(() => _workingTreeService.DiscardFileAsync(Repository!, change));
    }

    private void RaiseCommands()
    {
        foreach (var command in new[] { RefreshAllCommand, StageCommand, UnstageCommand, CommitCommand, EmptyCommitCommand, AmendCommand, StageAllAndCommitCommand, ConfirmEmptyCommitCommand, CancelCommitCommand, RequestDiscardCommand, ConfirmDiscardCommand, CancelDiscardCommand, SwitchBranchCommand, CreateBranchCommand, DeleteBranchCommand, CheckoutRemoteCommand, CheckoutTagCommand, FetchCommand, FetchAllCommand, PullCommand, PushCommand, CreateStashCommand, ApplyStashCommand, PopStashCommand, MergeCommand, LoadRebasePlanCommand, ApplyRebaseItemCommand, MoveRebaseUpCommand, MoveRebaseDownCommand, StartRebaseCommand, ContinueRebaseCommand, AbortRebaseCommand, OpenConflictCommand, ChooseCurrentCommand, ChooseIncomingCommand, KeepDeletionCommand, StageConflictCommand, MergeToolCommand, MergeToolWorkflowCommand, ConfigureMergeToolCommand, ContinueOperationCommand, AbortOperationCommand, SkipOperationCommand }.OfType<AsyncCommand>()) command.RaiseCanExecuteChanged();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
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
                    SelectedCommit = null;
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

    private async Task LoadCommitAsync()
    {
        var repository = Repository;
        var selectedRow = SelectedHistoryRow;
        var generation = Interlocked.Increment(ref _commitLoadGeneration);
        if (repository is null || selectedRow is null)
        {
            SelectedCommit = null;
            SelectedFile = null;
            SelectedDiff = null;
            return;
        }

        try
        {
            var commit = await _historyService.ReadCommitAsync(repository, selectedRow.Commit.Hash);
            if (generation != Volatile.Read(ref _commitLoadGeneration)
                || !ReferenceEquals(repository, Repository)
                || !ReferenceEquals(selectedRow, SelectedHistoryRow))
                return;

            SelectedCommit = commit;
            SelectedFile = SelectedCommit.Files.FirstOrDefault();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _commitLoadGeneration)
                && ReferenceEquals(repository, Repository)
                && ReferenceEquals(selectedRow, SelectedHistoryRow))
                ErrorMessage = $"Could not read commit: {exception.Message}";
        }
    }

    private async Task LoadDiffAsync()
    {
        var repository = Repository;
        var selectedCommit = SelectedCommit;
        var selectedFile = SelectedFile;
        var generation = Interlocked.Increment(ref _diffLoadGeneration);
        if (repository is null || selectedCommit is null || selectedFile is null)
        {
            SelectedDiff = null;
            return;
        }

        try
        {
            var diff = await _historyService.ReadDiffAsync(repository, selectedCommit.Commit.Hash, selectedFile.Path);
            if (generation != Volatile.Read(ref _diffLoadGeneration)
                || !ReferenceEquals(repository, Repository)
                || !ReferenceEquals(selectedCommit, SelectedCommit)
                || !ReferenceEquals(selectedFile, SelectedFile))
                return;
            SelectedDiff = diff;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _diffLoadGeneration)
                && ReferenceEquals(repository, Repository)
                && ReferenceEquals(selectedCommit, SelectedCommit)
                && ReferenceEquals(selectedFile, SelectedFile))
                ErrorMessage = $"Could not read change: {exception.Message}";
        }
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
