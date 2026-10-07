using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public interface IBranchesRepositoryContext : INotifyPropertyChanged
{
    Repository? Repository { get; }
    bool IsBusy { get; }
    RepositoryOperation CurrentOperation { get; }
    IReadOnlyList<GitRemote> Remotes { get; }

    Task<bool> RunBranchMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string errorContext,
        bool includeHistory,
        Action? afterSuccessfulMutation = null);
}

public sealed record BranchDeletionOperationResult(
    bool LifecycleSucceeded,
    bool LocalDeleted,
    bool RemoteDeleted,
    string? SecondaryFailureMessage = null);

public sealed class BranchesViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IReferenceService _referenceService;
    private readonly IRepositorySyncService _syncService;
    private IBranchesRepositoryContext? _context;
    private GitBranch? _selectedLocalBranch;
    private GitBranch? _selectedRemoteBranch;
    private string _newBranchName = string.Empty;
    private string? _pendingLocalSelectionName;
    private bool _operationInProgress;
    private int _disposed;

    public BranchesViewModel(
        IReferenceService referenceService,
        IRepositorySyncService syncService)
    {
        _referenceService = referenceService ?? throw new ArgumentNullException(nameof(referenceService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));

        SwitchBranchCommand = new AsyncCommand(
            SwitchSelectedBranchAsync,
            () => CanMutate && SelectedLocalBranch is { IsCurrent: false });
        CheckoutRemoteCommand = new AsyncCommand(
            CheckoutSelectedRemoteBranchAsync,
            () => CanMutate
                  && SelectedRemoteBranch is not null
                  && !string.IsNullOrWhiteSpace(NewBranchName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GitBranch> LocalBranches { get; } =
        new BulkObservableCollection<GitBranch>();

    public ObservableCollection<GitBranch> RemoteBranches { get; } =
        new BulkObservableCollection<GitBranch>();

    public ICommand SwitchBranchCommand { get; }
    public ICommand CheckoutRemoteCommand { get; }

    public GitBranch? SelectedLocalBranch
    {
        get => _selectedLocalBranch;
        set
        {
            if (ReferenceEquals(_selectedLocalBranch, value)) return;
            _selectedLocalBranch = value;
            Notify();
            NotifyAvailability();
        }
    }

    public GitBranch? SelectedRemoteBranch
    {
        get => _selectedRemoteBranch;
        set
        {
            if (ReferenceEquals(_selectedRemoteBranch, value)) return;
            _selectedRemoteBranch = value;
            Notify();
            NotifyAvailability();
        }
    }

    public string NewBranchName
    {
        get => _newBranchName;
        set
        {
            if (string.Equals(_newBranchName, value, StringComparison.Ordinal)) return;
            _newBranchName = value;
            Notify();
            NotifyAvailability();
        }
    }

    public bool IsBusy => _operationInProgress || _context?.IsBusy == true;

    public bool CanMutate =>
        _context?.Repository is not null
        && !IsBusy
        && _context.CurrentOperation == RepositoryOperation.None;

    public bool CanRename => CanMutate;

    internal void Attach(IBranchesRepositoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (ReferenceEquals(_context, context)) return;

        Detach();
        _context = context;
        _context.PropertyChanged += Context_PropertyChanged;
        NotifyAvailability();
    }

    internal void Detach()
    {
        if (_context is not null)
            _context.PropertyChanged -= Context_PropertyChanged;
        _context = null;
        ClearRepositoryState();
        NotifyAvailability();
    }

    internal void ApplyRepositoryState(
        IEnumerable<GitBranch> localBranches,
        IEnumerable<GitBranch> remoteBranches)
    {
        ArgumentNullException.ThrowIfNull(localBranches);
        ArgumentNullException.ThrowIfNull(remoteBranches);

        var localSelectionName = _pendingLocalSelectionName ?? SelectedLocalBranch?.Name;
        var remoteSelectionName = SelectedRemoteBranch?.Name;

        Replace(LocalBranches, localBranches);
        Replace(RemoteBranches, remoteBranches);

        SelectedLocalBranch =
            FindBranch(LocalBranches, localSelectionName)
            ?? LocalBranches.FirstOrDefault(branch => branch.IsCurrent)
            ?? LocalBranches.FirstOrDefault();
        SelectedRemoteBranch = FindBranch(RemoteBranches, remoteSelectionName);
        _pendingLocalSelectionName = null;
        NotifyAvailability();
    }

    internal void ClearRepositoryState()
    {
        _pendingLocalSelectionName = null;
        Replace(LocalBranches, []);
        Replace(RemoteBranches, []);
        SelectedLocalBranch = null;
        SelectedRemoteBranch = null;
        NewBranchName = string.Empty;
    }

    internal void RefreshAvailability() => NotifyAvailability();

    public async Task<bool> SwitchBranchAsync(
        Repository repository,
        GitBranch branch)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(branch);
        if (branch.IsCurrent) return true;

        _pendingLocalSelectionName = branch.Name;
        SelectedLocalBranch = branch;
        return await RunMutationAsync(
            repository,
            () => _referenceService.SwitchBranchAsync(repository, branch.Name),
            "Could not switch branch");
    }

    public async Task<bool> CheckoutRemoteBranchAsync(
        Repository repository,
        GitBranch remoteBranch,
        string localBranchName)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(remoteBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(localBranchName);

        var localName = localBranchName.Trim();
        _pendingLocalSelectionName = localName;
        SelectedRemoteBranch = remoteBranch;
        NewBranchName = localName;
        return await RunMutationAsync(
            repository,
            () => _referenceService.CheckoutRemoteBranchAsync(
                repository,
                remoteBranch.Name,
                localName),
            "Could not checkout remote branch");
    }

    public async Task<bool> CreateBranchAsync(
        Repository repository,
        string branchName,
        string? startPoint = null,
        bool switchToBranch = true)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);

        var name = branchName.Trim();
        if (switchToBranch)
            _pendingLocalSelectionName = name;
        return await RunMutationAsync(
            repository,
            () => _referenceService.CreateBranchAsync(
                repository,
                name,
                startPoint,
                switchToBranch),
            "Could not create branch");
    }

    public async Task<bool> RenameBranchAsync(
        Repository repository,
        GitBranch branch,
        string newName,
        Action? afterSuccessfulMutation = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var normalized = newName.Trim();
        if (string.Equals(branch.Name, normalized, StringComparison.Ordinal)) return true;

        _pendingLocalSelectionName = normalized;
        return await RunMutationAsync(
            repository,
            () => _referenceService.RenameBranchAsync(
                repository,
                branch.Name,
                normalized),
            "Could not rename local branch",
            afterSuccessfulMutation: afterSuccessfulMutation);
    }

    internal LocalBranchRemoteDeletionTarget? ResolveRemoteDeletionTargetForLocal(
        GitBranch localBranch) =>
        BranchDeletionResolver.ResolveRemoteForLocal(
            localBranch,
            RemoteBranches,
            _context?.Remotes ?? []);

    internal RemoteBranchDeletionTarget ResolveRemoteDeletionTarget(
        GitBranch remoteBranch) =>
        BranchDeletionResolver.Resolve(
            remoteBranch,
            LocalBranches,
            _context?.Remotes ?? []);

    public async Task<BranchDeletionOperationResult> DeleteLocalBranchAsync(
        Repository repository,
        GitBranch branch,
        BranchDeletionMode mode,
        bool deleteRemote,
        Action<string>? referenceDeleted = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(branch);
        if (branch.IsCurrent)
            return new(false, false, false, "The current branch cannot be deleted.");

        var remoteTarget = deleteRemote
            ? ResolveRemoteDeletionTargetForLocal(branch)
            : null;
        var localDeleted = false;
        var remoteDeleted = false;
        string? secondaryFailure = null;

        var lifecycleSucceeded = await RunMutationAsync(
            repository,
            async () =>
            {
                await _referenceService.DeleteBranchAsync(
                    repository,
                    branch.Name,
                    mode);
                localDeleted = true;
                referenceDeleted?.Invoke(branch.Name);

                if (remoteTarget is null) return;

                try
                {
                    await _syncService.DeleteRemoteBranchAsync(
                        repository,
                        remoteTarget.Remote.Name,
                        remoteTarget.BranchName);
                    remoteDeleted = true;
                    referenceDeleted?.Invoke(
                        $"{remoteTarget.Remote.Name}/{remoteTarget.BranchName}");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    secondaryFailure = exception.Message;
                }
            },
            "Could not delete local branch");

        return new(
            lifecycleSucceeded,
            localDeleted,
            remoteDeleted,
            secondaryFailure);
    }

    public async Task<BranchDeletionOperationResult> DeleteRemoteBranchAsync(
        Repository repository,
        GitBranch remoteBranch,
        RemoteBranchDeletionTarget target,
        bool deleteLocal,
        BranchDeletionMode localDeletionMode,
        Action<string>? referenceDeleted = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(remoteBranch);
        ArgumentNullException.ThrowIfNull(target);

        var remoteDeleted = false;
        var localDeleted = false;
        string? secondaryFailure = null;

        var lifecycleSucceeded = await RunMutationAsync(
            repository,
            async () =>
            {
                await _syncService.DeleteRemoteBranchAsync(
                    repository,
                    target.Remote.Name,
                    target.BranchName);
                remoteDeleted = true;
                referenceDeleted?.Invoke(remoteBranch.Name);

                if (!deleteLocal || target.LocalBranch is null) return;

                try
                {
                    await _referenceService.DeleteBranchAsync(
                        repository,
                        target.LocalBranch.Name,
                        localDeletionMode);
                    localDeleted = true;
                    referenceDeleted?.Invoke(target.LocalBranch.Name);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    secondaryFailure = exception.Message;
                }
            },
            "Could not delete remote branch");

        return new(
            lifecycleSucceeded,
            localDeleted,
            remoteDeleted,
            secondaryFailure);
    }

    internal BranchFolderDeletionPlan CreateLocalFolderDeletionPlan(
        IReadOnlyList<BranchFolderDeletionCandidate> candidates) =>
        BranchFolderDeletionPlanner.CreateLocal(candidates);

    internal IReadOnlyList<RemoteBranchFolderDeletionTarget> CreateRemoteFolderDeletionTargets(
        BranchFolderInfo folderInfo,
        IReadOnlyList<BranchFolderDeletionCandidate> candidates) =>
        BranchFolderDeletionPlanner.CreateRemoteTargets(folderInfo, candidates);

    internal async Task<BranchFolderDeletionExecutionResult?> DeleteLocalFolderAsync(
        Repository repository,
        BranchFolderDeletionPlan plan,
        BranchDeletionMode mode,
        Action<IReadOnlyList<string>>? referencesDeleted = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(plan);

        BranchFolderDeletionExecutionResult? result = null;
        var succeeded = await RunMutationAsync(
            repository,
            async () =>
            {
                result = await BranchFolderDeletionExecutor.ExecuteAsync(
                    plan.EligibleBranches,
                    candidate => candidate.Branch.Name,
                    candidate => _referenceService.DeleteBranchAsync(
                        repository,
                        candidate.Branch.Name,
                        mode));
                referencesDeleted?.Invoke(result.SuccessfulBranches);
            },
            "Could not delete branches in folder");

        return succeeded ? result : null;
    }

    internal async Task<BranchFolderDeletionExecutionResult?> DeleteRemoteFolderAsync(
        Repository repository,
        string remoteName,
        IReadOnlyList<RemoteBranchFolderDeletionTarget> targets,
        Action<IReadOnlyList<string>>? referencesDeleted = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteName);
        ArgumentNullException.ThrowIfNull(targets);

        BranchFolderDeletionExecutionResult? result = null;
        var succeeded = await RunMutationAsync(
            repository,
            async () =>
            {
                result = await BranchFolderDeletionExecutor.ExecuteAsync(
                    targets,
                    target => target.BranchName,
                    target => _syncService.DeleteRemoteBranchAsync(
                        repository,
                        remoteName,
                        target.RelativeBranchName));
                referencesDeleted?.Invoke(result.SuccessfulBranches);
            },
            "Could not delete remote branches in folder");

        return succeeded ? result : null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Detach();
    }

    private Task SwitchSelectedBranchAsync()
    {
        var repository = _context?.Repository;
        var branch = SelectedLocalBranch;
        return repository is null || branch is null
            ? Task.CompletedTask
            : SwitchBranchAsync(repository, branch);
    }

    private Task CheckoutSelectedRemoteBranchAsync()
    {
        var repository = _context?.Repository;
        var branch = SelectedRemoteBranch;
        return repository is null || branch is null || string.IsNullOrWhiteSpace(NewBranchName)
            ? Task.CompletedTask
            : CheckoutRemoteBranchAsync(repository, branch, NewBranchName);
    }

    private async Task<bool> RunMutationAsync(
        Repository repository,
        Func<Task> mutation,
        string errorContext,
        bool includeHistory = true,
        Action? afterSuccessfulMutation = null)
    {
        var context = _context;
        if (!CanRunMutation(context, repository)) return false;

        _operationInProgress = true;
        NotifyAvailability();
        try
        {
            return await context!.RunBranchMutationAsync(
                repository,
                mutation,
                errorContext,
                includeHistory,
                afterSuccessfulMutation);
        }
        finally
        {
            _operationInProgress = false;
            NotifyAvailability();
        }
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IBranchesRepositoryContext.Repository))
        {
            ClearRepositoryState();
            NotifyAvailability();
            return;
        }

        if (args.PropertyName is nameof(IBranchesRepositoryContext.IsBusy)
            or nameof(IBranchesRepositoryContext.CurrentOperation))
            NotifyAvailability();
    }

    private void NotifyAvailability()
    {
        Notify(nameof(IsBusy));
        Notify(nameof(CanMutate));
        Notify(nameof(CanRename));
        ((AsyncCommand)SwitchBranchCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)CheckoutRemoteCommand).RaiseCanExecuteChanged();
    }

    private bool CanRunMutation(
        IBranchesRepositoryContext? context,
        Repository repository) =>
        context is not null
        && CanMutate
        && IsCurrentRepository(context, repository);

    private bool CanRunQuery(
        IBranchesRepositoryContext? context,
        Repository repository) =>
        context is not null
        && !_operationInProgress
        && !context.IsBusy
        && context.CurrentOperation == RepositoryOperation.None
        && IsCurrentRepository(context, repository);

    private static bool IsCurrentRepository(
        IBranchesRepositoryContext? context,
        Repository repository) =>
        context is not null && ReferenceEquals(context.Repository, repository);

    private static GitBranch? FindBranch(
        IEnumerable<GitBranch> branches,
        string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : branches.FirstOrDefault(
                branch => string.Equals(branch.Name, name, StringComparison.Ordinal));

    private static void Replace<T>(
        ObservableCollection<T> target,
        IEnumerable<T> values)
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
