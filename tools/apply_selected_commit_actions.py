from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def read(path):
    return (ROOT / path).read_text(encoding="utf-8")

def write(path, content):
    (ROOT / path).write_text(content, encoding="utf-8", newline="\n")

def replace_once(path, old, new):
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one match, got {count}: {old[:100]!r}")
    write(path, text.replace(old, new, 1))

def replace_between(path, start, end, replacement):
    text = read(path)
    a = text.find(start)
    b = text.find(end, a + 1)
    if a < 0 or b < 0:
        raise RuntimeError(f"{path}: markers not found")
    write(path, text[:a] + replacement + text[b:])

contract = "src/CSharpGit.Application/Abstractions/IRepositoryStateService.cs"
replace_once(
    contract,
    "    Task ForcePushWithLeaseAsync(Repository repository, ForcePushWithLeaseSnapshot snapshot, CancellationToken cancellationToken = default);\n",
    """    Task ForcePushWithLeaseAsync(Repository repository, ForcePushWithLeaseSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<ApplyCommitResult> CherryPickAsync(Repository repository, string commit, int? mainlineParent = null, CancellationToken cancellationToken = default);
    Task<ApplyCommitResult> RevertAsync(Repository repository, string commit, int? mainlineParent = null, CancellationToken cancellationToken = default);
    Task ResetAsync(Repository repository, string commit, ResetMode mode, CancellationToken cancellationToken = default);
""")

vm = "src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.cs"
for old, new in [
    ("public ObservableCollection<WorkingTreeChange> Changes { get; } = [];",
     "public ObservableCollection<WorkingTreeChange> Changes { get; } = new BulkObservableCollection<WorkingTreeChange>();"),
    ("public ObservableCollection<GitBranch> LocalBranches { get; } = [];",
     "public ObservableCollection<GitBranch> LocalBranches { get; } = new BulkObservableCollection<GitBranch>();"),
    ("public ObservableCollection<GitBranch> RemoteBranches { get; } = [];",
     "public ObservableCollection<GitBranch> RemoteBranches { get; } = new BulkObservableCollection<GitBranch>();"),
    ("public ObservableCollection<GitRemote> Remotes { get; } = [];",
     "public ObservableCollection<GitRemote> Remotes { get; } = new BulkObservableCollection<GitRemote>();"),
    ("public ObservableCollection<GitTag> Tags { get; } = [];",
     "public ObservableCollection<GitTag> Tags { get; } = new BulkObservableCollection<GitTag>();"),
    ("public ObservableCollection<GitStash> Stashes { get; } = [];",
     "public ObservableCollection<GitStash> Stashes { get; } = new BulkObservableCollection<GitStash>();"),
    ("public ObservableCollection<ConflictFile> Conflicts { get; } = [];",
     "public ObservableCollection<ConflictFile> Conflicts { get; } = new BulkObservableCollection<ConflictFile>();"),
]:
    replace_once(vm, old, new)

replace_once(
    vm,
    "    internal Task RefreshAsyncForDesktopCheck() => RefreshAllAsync();\n",
    """    internal Task RefreshAsyncForDesktopCheck() => RefreshAllAsync();

    internal Task<bool> RunMutationAsync(Func<Task> mutation, string? errorContext = null, bool includeHistory = true) =>
        MutateAsync(mutation, errorContext, includeHistory: includeHistory);
""")

refresh = """    private Task RefreshAllAsync() => RefreshStateAsync(includeHistory: true);

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

"""
replace_between(vm, "    private async Task RefreshAllAsync()\n", "    private async Task<bool> MutateAsync", refresh)

replace_once(
    vm,
    "    private async Task<bool> MutateAsync(Func<Task> mutation, string? errorContext = null, Action? beforeMutation = null)\n",
    "    private async Task<bool> MutateAsync(Func<Task> mutation, string? errorContext = null, Action? beforeMutation = null, bool includeHistory = true)\n")
replace_once(vm, "            try { await RefreshAllAsync(); }\n", "            try { await RefreshStateAsync(includeHistory); }\n")

for old, new in [
("""        await MutateAsync(
            () => _workingTreeService.StageFileAsync(Repository!, change),
            "Could not stage file",
            ClearWorkingTreePresentationSelection);""",
"""        await MutateAsync(
            () => _workingTreeService.StageFileAsync(Repository!, change),
            "Could not stage file",
            includeHistory: false);"""),
("""        await MutateAsync(
            () => _workingTreeService.UnstageFileAsync(Repository!, change),
            "Could not unstage file",
            ClearWorkingTreePresentationSelection);""",
"""        await MutateAsync(
            () => _workingTreeService.UnstageFileAsync(Repository!, change),
            "Could not unstage file",
            includeHistory: false);"""),
("""        await MutateAsync(
            () => _workingTreeService.StageFilesAsync(Repository!, changes),
            "Could not stage selected files",
            ClearWorkingTreePresentationSelection);""",
"""        await MutateAsync(
            () => _workingTreeService.StageFilesAsync(Repository!, changes),
            "Could not stage selected files",
            includeHistory: false);"""),
("""        await MutateAsync(
            () => _workingTreeService.StageAllAsync(Repository!),
            "Could not stage all files",
            ClearWorkingTreePresentationSelection);""",
"""        await MutateAsync(
            () => _workingTreeService.StageAllAsync(Repository!),
            "Could not stage all files",
            includeHistory: false);"""),
("""        await MutateAsync(
            () => _workingTreeService.UnstageFilesAsync(Repository!, changes),
            "Could not unstage selected files",
            ClearWorkingTreePresentationSelection);""",
"""        await MutateAsync(
            () => _workingTreeService.UnstageFilesAsync(Repository!, changes),
            "Could not unstage selected files",
            includeHistory: false);"""),
("""        await MutateAsync(
            () => _workingTreeService.UnstageAllAsync(Repository!),
            "Could not unstage all files",
            ClearWorkingTreePresentationSelection);""",
"""        await MutateAsync(
            () => _workingTreeService.UnstageAllAsync(Repository!),
            "Could not unstage all files",
            includeHistory: false);"""),
("""        if (change is not null) await MutateAsync(() => _workingTreeService.DiscardFileAsync(Repository!, change));""",
"""        if (change is not null)
            await MutateAsync(
                () => _workingTreeService.DiscardFileAsync(Repository!, change),
                "Could not discard file",
                includeHistory: false);"""),
]:
    replace_once(vm, old, new)

replace_once(
    vm,
"""    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }
""",
"""    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
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
""")

page = "src/CSharpGit.Presentation/MainPage.xaml.cs"
replace_once(
    page,
"""        _viewModel.Changes.CollectionChanged += (_, _) => RefreshPresentationCollections();
        _viewModel.LocalBranches.CollectionChanged += (_, _) => RebuildRepositoryTree();
        _viewModel.RemoteBranches.CollectionChanged += (_, _) => RebuildRepositoryTree();
        _viewModel.Remotes.CollectionChanged += (_, _) => RebuildRepositoryTree();
        _viewModel.Tags.CollectionChanged += (_, _) => RebuildRepositoryTree();
        _viewModel.Stashes.CollectionChanged += (_, _) => RebuildRepositoryTree();
""",
"""        _viewModel.Changes.CollectionChanged += (_, _) => QueueRepositoryPresentationRefresh(workingTreeChanged: true);
        _viewModel.LocalBranches.CollectionChanged += (_, _) => QueueRepositoryPresentationRefresh();
        _viewModel.RemoteBranches.CollectionChanged += (_, _) => QueueRepositoryPresentationRefresh();
        _viewModel.Remotes.CollectionChanged += (_, _) => QueueRepositoryPresentationRefresh();
        _viewModel.Tags.CollectionChanged += (_, _) => QueueRepositoryPresentationRefresh();
        _viewModel.Stashes.CollectionChanged += (_, _) => QueueRepositoryPresentationRefresh();
""")
replace_once(
    page,
"""        RefreshPresentationCollections();
        InitializeWorkingTreeDiffSurface();
""",
"""        RefreshPresentationCollections();
        InitializeWorkingTreeDiffSurface();
        InitializeCommitActions();
""")
replace_once(
    page,
"""    private void RefreshPresentationCollections()
    {
        _unstagedChanges.Clear();
        _stagedChanges.Clear();
        foreach (var change in _viewModel.Changes)
        {
            if (change.IsUnstaged) _unstagedChanges.Add(change);
            if (change.IsStaged) _stagedChanges.Add(change);
        }
        UnstagedHeader.Text = $"Unstaged changes ({_unstagedChanges.Count})";
        StagedHeader.Text = $"Staged changes ({_stagedChanges.Count})";
        RebuildRepositoryTree();
        UpdateStatusBar();
    }
""",
"""    private void RefreshPresentationCollections()
    {
        _workingTreeSelectionSync = true;
        try
        {
            _unstagedChanges.Clear();
            _stagedChanges.Clear();
            foreach (var change in _viewModel.Changes)
            {
                if (change.IsUnstaged) _unstagedChanges.Add(change);
                if (change.IsStaged) _stagedChanges.Add(change);
            }
        }
        finally
        {
            _workingTreeSelectionSync = false;
        }

        UnstagedHeader.Text = $"Unstaged changes ({_unstagedChanges.Count})";
        StagedHeader.Text = $"Staged changes ({_stagedChanges.Count})";
        RebuildRepositoryTree();
        UpdateStatusBar();
    }
""")

replace_once(
    ".idd/intent/INDEX.md",
    "| IDD-0013 | Spec | Safe force push | Explicit force-with-lease snapshot, confirmation and CAS safety | — |\n",
    """| IDD-0013 | Spec | Safe force push | Explicit force-with-lease snapshot, confirmation and CAS safety | — |
| IDD-0014 | Spec | Commit actions and refresh stability | Selected-commit actions, exact Git semantics and stable state refresh | — |
""")

for path in [
    ROOT / "tools" / "apply_selected_commit_actions.py",
    ROOT / ".github" / "workflows" / "implement-selected-commit-actions.yml",
]:
    try:
        path.unlink()
    except FileNotFoundError:
        pass
