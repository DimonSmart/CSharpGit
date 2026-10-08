using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Threading;

namespace CSharpGit.Presentation.ViewModels;

public interface IRepositorySyncContext : INotifyPropertyChanged
{
    Repository? Repository { get; }
    bool IsBusy { get; }
    bool CanRunSyncMutation { get; }
    RepositoryOperation CurrentOperation { get; }
    GitBranch? CurrentLocalBranch { get; }

    Task<bool> RunSyncMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string? errorContext = null);
}

public enum PushExecutionKind
{
    Completed,
    PublishTargetRequired,
    NonFastForwardRejected,
    Unavailable,
    Failed
}

public sealed record PushExecutionResult(
    PushExecutionKind Kind,
    string? Message = null);

public sealed record PublishBranchPreparationResult(
    PublishBranchPreparation? Preparation,
    string? ErrorMessage = null)
{
    public bool Succeeded => Preparation is not null && ErrorMessage is null;
}

public enum ForcePushPreparationKind
{
    Ready,
    ExplicitTargetRequired,
    RemoteBranchDoesNotExist,
    MultiplePushDestinations,
    StaleRepository,
    Failed
}

public sealed record ForcePushPreparationResult(
    ForcePushPreparationKind Kind,
    ForcePushWithLeaseSnapshot? Snapshot = null,
    string? Message = null);

public enum ForcePushExecutionKind
{
    Completed,
    LeaseRejected,
    Cancelled,
    StaleRepository,
    Failed
}

public sealed record ForcePushExecutionResult(
    ForcePushExecutionKind Kind,
    string? Message = null);

public enum PullExecutionKind
{
    Completed,
    NeedsAttention,
    Refused,
    Failed,
    Unavailable
}

public sealed record PullExecutionResult(
    PullExecutionKind Kind,
    string Message,
    PullResult? GitResult = null);

public sealed class RepositorySyncViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IRepositorySyncService _syncService;
    private readonly IAppSettingsService _settings;
    private readonly IUiDispatcher? _uiDispatcher;
    private PullExecutionResult? _lastPullResult;
    private IRepositorySyncContext? _context;
    private GitRemote? _selectedRemote;
    private bool _operationInProgress;
    private int _disposed;

    public RepositorySyncViewModel(
        IRepositorySyncService syncService,
        IAppSettingsService settings,
        IUiDispatcher? uiDispatcher = null)
    {
        _uiDispatcher = uiDispatcher;
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        FetchCommand = new AsyncCommand(FetchSelectedRemoteAsync, () => CanFetch);
        FetchAllCommand = new AsyncCommand(FetchAllCurrentAsync, () => CanFetchAll);
        PullCommand = new AsyncCommand(PullCurrentAsync, () => CanPull);
        Remotes.CollectionChanged += Remotes_CollectionChanged;
        _settings.Changed += PullSettings_Changed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GitRemote> Remotes { get; } =
        new BulkObservableCollection<GitRemote>();

    public GitRemote? SelectedRemote
    {
        get => _selectedRemote;
        set
        {
            if (ReferenceEquals(_selectedRemote, value)) return;
            _selectedRemote = value;
            Notify();
            RefreshAvailability();
        }
    }

    public ICommand FetchCommand { get; }
    public ICommand FetchAllCommand { get; }
    public ICommand PullCommand { get; }

    public bool IsBusy => _operationInProgress || _context?.IsBusy == true;

    public bool CanFetch =>
        _context?.CanRunSyncMutation == true
        && SelectedRemote is not null;

    public bool CanFetchAll => _context?.CanRunSyncMutation == true;

    public bool CanPull =>
        _context is { Repository: not null, CanRunSyncMutation: true, IsBusy: false }
        && !_operationInProgress
        && _context.CurrentOperation == RepositoryOperation.None
        && _context.CurrentLocalBranch is { Upstream: { Length: > 0 } };

    public PullStrategy DefaultPullStrategy => _settings.DefaultPullStrategy;
    public bool ForcePullAutoStash => _settings.ForcePullAutoStash;
    public bool IsGitConfigurationDefault => DefaultPullStrategy == PullStrategy.GitConfiguration;
    public bool IsMergeDefault => DefaultPullStrategy == PullStrategy.Merge;
    public bool IsRebaseDefault => DefaultPullStrategy == PullStrategy.Rebase;
    public bool IsFastForwardOnlyDefault => DefaultPullStrategy == PullStrategy.FastForwardOnly;
    public GitBranch? CurrentPullBranch => _context?.CurrentLocalBranch;
    public PullExecutionResult? LastPullResult => _lastPullResult;

    public string PullTooltip
    {
        get
        {
            var branch = CurrentPullBranch;
            var status = _context?.CurrentOperation != RepositoryOperation.None
                ? "A repository operation is already in progress."
                : branch is null ? "HEAD is detached."
                : string.IsNullOrWhiteSpace(branch.Upstream) ? $"Branch '{branch.Name}' has no upstream."
                : $"Pull from {branch.Upstream}";
            var strategy = DefaultPullStrategy == PullStrategy.GitConfiguration
                ? "Git configuration (Git decides)"
                : DefaultPullStrategy.ToString();
            return $"{status} · Strategy: {strategy} · Autostash: {(ForcePullAutoStash ? "forced" : "Git configuration")}";
        }
    }

    public bool CanPush =>
        _context is { Repository: not null, CanRunSyncMutation: true, IsBusy: false }
        && _context.CurrentLocalBranch is not null;

    public bool CanPushTo =>
        _context is { Repository: not null, IsBusy: false }
        && !_operationInProgress
        && _context.CurrentOperation == RepositoryOperation.None
        && _context.CurrentLocalBranch is not null
        && Remotes.Count > 0;

    public bool CanForcePushWithLease =>
        _context is { Repository: not null, IsBusy: false }
        && !_operationInProgress
        && _context.CurrentOperation == RepositoryOperation.None
        && _context.CurrentLocalBranch is not null;

    internal void Attach(IRepositorySyncContext context)
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

    internal void ApplyRepositoryState(IEnumerable<GitRemote> remotes)
    {
        ArgumentNullException.ThrowIfNull(remotes);

        var selectedName = SelectedRemote?.Name;
        Replace(Remotes, remotes);
        SelectedRemote = string.IsNullOrWhiteSpace(selectedName)
            ? Remotes.FirstOrDefault()
            : Remotes.FirstOrDefault(remote =>
                string.Equals(remote.Name, selectedName, StringComparison.Ordinal))
              ?? Remotes.FirstOrDefault();
        RefreshAvailability();
    }

    internal void ClearRepositoryState()
    {
        Replace(Remotes, []);
        SelectedRemote = null;
        RefreshAvailability();
    }

    internal void RefreshAvailability()
    {
        Notify(nameof(IsBusy));
        Notify(nameof(CanFetch));
        Notify(nameof(CanFetchAll));
        Notify(nameof(CanPull));
        Notify(nameof(CurrentPullBranch));
        Notify(nameof(PullTooltip));
        Notify(nameof(CanPush));
        Notify(nameof(CanPushTo));
        Notify(nameof(CanForcePushWithLease));
        ((AsyncCommand)FetchCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)FetchAllCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)PullCommand).RaiseCanExecuteChanged();
    }

    public Task<bool> FetchAsync(
        Repository repository,
        string remote)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);

        var context = _context;
        if (!IsCurrentRepository(context, repository))
            return Task.FromResult(false);

        return context!.RunSyncMutationAsync(
            repository,
            () => _syncService.FetchAsync(repository, remote));
    }

    public Task<bool> FetchAllAsync(Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var context = _context;
        if (!IsCurrentRepository(context, repository))
            return Task.FromResult(false);

        return context!.RunSyncMutationAsync(
            repository,
            () => _syncService.FetchAllAsync(repository));
    }

    public Task<PullExecutionResult> PullAsync(Repository repository) =>
        PullAsync(repository, DefaultPullStrategy);

    public async Task<PullExecutionResult> PullAsync(
        Repository repository,
        PullStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (!Enum.IsDefined(strategy))
            throw new ArgumentOutOfRangeException(nameof(strategy));
        var context = _context;
        if (!IsCurrentRepository(context, repository))
            return PublishPullResult(new(PullExecutionKind.Unavailable,
                "The repository changed before pull could start."));
        if (!CanPull)
            return PublishPullResult(new(PullExecutionKind.Unavailable,
                "Pull is unavailable while the repository is busy, an operation is active, or no upstream is configured."));

        // Snapshot both preferences before the first await. The selected one-off strategy
        // never modifies the stored default.
        var options = new PullOptions(strategy, ForcePullAutoStash);
        PullExecutionResult? result = null;
        var lifecycleSucceeded = await context!.RunSyncMutationAsync(
            repository,
            async () =>
            {
                try
                {
                    var gitResult = await _syncService.PullAsync(repository, options);
                    var kind = gitResult.Outcome switch
                    {
                        PullOutcome.Completed => PullExecutionKind.Completed,
                        PullOutcome.NeedsAttention => PullExecutionKind.NeedsAttention,
                        PullOutcome.Refused => PullExecutionKind.Refused,
                        _ => PullExecutionKind.Failed
                    };
                    result = new(kind, gitResult.Message, gitResult);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    result = new(PullExecutionKind.Failed, exception.Message);
                }
            });

        if (!lifecycleSucceeded || !IsCurrentRepository(_context, repository))
            return PublishPullResult(new(
                IsCurrentRepository(_context, repository)
                    ? PullExecutionKind.Failed : PullExecutionKind.Unavailable,
                IsCurrentRepository(_context, repository)
                    ? "The repository could not be refreshed after pull."
                    : "The repository changed while pull was running."));

        return PublishPullResult(result ??
            new(PullExecutionKind.Failed, "Pull did not produce a result."));
    }

    public async Task SetDefaultPullStrategyAsync(PullStrategy strategy)
    {
        try
        {
            await _settings.SetDefaultPullStrategyAsync(strategy);
        }
        finally
        {
            NotifyPullPreferences();
        }
    }

    public async Task SetPullAutoStashAsync(bool value)
    {
        try
        {
            await _settings.SetForcePullAutoStashAsync(value);
        }
        finally
        {
            NotifyPullPreferences();
        }
    }

    private PullExecutionResult PublishPullResult(PullExecutionResult result)
    {
        _lastPullResult = result;
        Notify(nameof(LastPullResult));
        return result;
    }

    private void PullSettings_Changed(object? sender, EventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (_uiDispatcher is { HasThreadAccess: false })
        {
            _uiDispatcher.TryEnqueue(() =>
            {
                if (Volatile.Read(ref _disposed) == 0)
                    NotifyPullPreferences();
            });
            return;
        }
        NotifyPullPreferences();
    }

    private void NotifyPullPreferences()
    {
        Notify(nameof(DefaultPullStrategy));
        Notify(nameof(ForcePullAutoStash));
        Notify(nameof(IsGitConfigurationDefault));
        Notify(nameof(IsMergeDefault));
        Notify(nameof(IsRebaseDefault));
        Notify(nameof(IsFastForwardOnlyDefault));
        Notify(nameof(PullTooltip));
    }

    public async Task<PushExecutionResult> PushAsync(Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var context = _context;
        if (!IsCurrentRepository(context, repository))
            return new(PushExecutionKind.Unavailable, "The repository changed before the push could start.");

        var currentBranch = context!.CurrentLocalBranch;
        if (currentBranch is null)
            return new(
                PushExecutionKind.Unavailable,
                "HEAD is detached. Publishing requires a current local branch.");

        var autoSetupRemote = string.IsNullOrWhiteSpace(currentBranch.Upstream);
        if (autoSetupRemote && !_settings.AutoSetupRemoteOnPush)
            return new(PushExecutionKind.PublishTargetRequired);

        PushExecutionResult? result = null;
        var lifecycleSucceeded = await context.RunSyncMutationAsync(
            repository,
            async () =>
            {
                try
                {
                    await _syncService.PushAsync(
                        repository,
                        autoSetupRemote
                            ? new PushOptions(AutoSetupRemote: true)
                            : null);
                    result = new(PushExecutionKind.Completed);
                }
                catch (PushRejectedException exception)
                    when (autoSetupRemote
                          && exception.ResultKind == PushResultKind.PushDestinationUnavailable)
                {
                    result = new(
                        PushExecutionKind.PublishTargetRequired,
                        "Git could not determine a push destination from the current configuration.");
                }
                catch (PushRejectedException exception)
                    when (exception.ResultKind == PushResultKind.NonFastForwardRejected)
                {
                    result = autoSetupRemote
                        ? new(
                            PushExecutionKind.PublishTargetRequired,
                            "The destination selected by Git has different history. Choose the publish target explicitly to continue safely.")
                        : new(PushExecutionKind.NonFastForwardRejected, exception.Message);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    result = new(PushExecutionKind.Failed, exception.Message);
                }
            });

        if (!lifecycleSucceeded)
            return IsCurrentRepository(_context, repository)
                ? new(PushExecutionKind.Failed, "The repository could not be refreshed after the push operation.")
                : new(PushExecutionKind.Unavailable, "The repository changed while the push was running.");

        return result ?? new(PushExecutionKind.Failed, "The push did not produce a result.");
    }

    public async Task<PublishBranchPreparationResult> PreparePublishBranchAsync(
        Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var context = _context;
        if (!CanRunQuery(context, repository))
            return new(null, "The repository changed while the publish dialog was open.");

        _operationInProgress = true;
        RefreshAvailability();
        try
        {
            var preparation = await _syncService.PreparePublishBranchAsync(repository);
            return IsCurrentRepository(context, repository)
                ? new(preparation)
                : new(null, "The repository changed while preparing the publish target.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(null, exception.Message);
        }
        finally
        {
            _operationInProgress = false;
            RefreshAvailability();
        }
    }

    public async Task<PushExecutionResult> PublishBranchAsync(
        Repository repository,
        string remote,
        string remoteBranch,
        bool setUpstream)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteBranch);

        if (!IsCurrentRepository(_context, repository))
            return new(PushExecutionKind.Unavailable, "The repository changed while the publish dialog was open.");

        PushExecutionResult? result = null;
        var lifecycleSucceeded = await _context!.RunSyncMutationAsync(
            repository,
            async () =>
            {
                try
                {
                    await _syncService.PublishBranchAsync(
                        repository,
                        new PublishBranchRequest(remote, remoteBranch, setUpstream));
                    result = new(PushExecutionKind.Completed);
                }
                catch (PushRejectedException exception)
                    when (exception.ResultKind == PushResultKind.NonFastForwardRejected)
                {
                    result = new(PushExecutionKind.NonFastForwardRejected, exception.Message);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    result = new(PushExecutionKind.Failed, exception.Message);
                }
            });

        if (!lifecycleSucceeded)
            return IsCurrentRepository(_context, repository)
                ? new(PushExecutionKind.Failed, "The repository could not be refreshed after the publish operation.")
                : new(PushExecutionKind.Unavailable, "The repository changed while publishing the branch.");

        return result ?? new(PushExecutionKind.Failed, "The publish operation did not produce a result.");
    }

    public async Task<ForcePushPreparationResult> PrepareForcePushWithLeaseAsync(
        Repository repository,
        string? remote = null,
        string? remoteBranch = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var context = _context;
        if (!CanRunQuery(context, repository))
            return new(
                ForcePushPreparationKind.StaleRepository,
                Message: "The repository changed before force-push preparation could start.");

        _operationInProgress = true;
        RefreshAvailability();
        try
        {
            var snapshot = await _syncService.PrepareForcePushWithLeaseAsync(
                repository,
                remote,
                remoteBranch);
            return IsCurrentRepository(context, repository)
                ? new(ForcePushPreparationKind.Ready, snapshot)
                : new(
                    ForcePushPreparationKind.StaleRepository,
                    Message: "The repository changed while preparing force push with lease.");
        }
        catch (ForcePushWithLeasePreparationException exception)
        {
            return exception.Failure switch
            {
                ForcePushPreparationFailure.MissingUpstream =>
                    new(ForcePushPreparationKind.ExplicitTargetRequired, Message: exception.Message),
                ForcePushPreparationFailure.RemoteBranchDoesNotExist =>
                    new(ForcePushPreparationKind.RemoteBranchDoesNotExist, Message: exception.Message),
                ForcePushPreparationFailure.MultiplePushDestinations =>
                    new(ForcePushPreparationKind.MultiplePushDestinations, Message: exception.Message),
                _ => new(ForcePushPreparationKind.Failed, Message: exception.Message)
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(ForcePushPreparationKind.Failed, Message: exception.Message);
        }
        finally
        {
            _operationInProgress = false;
            RefreshAvailability();
        }
    }

    public async Task<ForcePushExecutionResult> ForcePushWithLeaseAsync(
        Repository repository,
        ForcePushWithLeaseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!IsCurrentRepository(_context, repository))
            return new(
                ForcePushExecutionKind.StaleRepository,
                "The repository changed before force push with lease could start.");

        ForcePushExecutionResult? result = null;
        var lifecycleSucceeded = await _context!.RunSyncMutationAsync(
            repository,
            async () =>
            {
                try
                {
                    await _syncService.ForcePushWithLeaseAsync(repository, snapshot);
                    result = new(ForcePushExecutionKind.Completed);
                }
                catch (ForcePushWithLeaseCancelledException exception)
                {
                    result = new(ForcePushExecutionKind.Cancelled, exception.Message);
                }
                catch (PushRejectedException exception)
                    when (exception.ResultKind == PushResultKind.LeaseRejected)
                {
                    result = new(ForcePushExecutionKind.LeaseRejected, exception.Message);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    result = new(ForcePushExecutionKind.Failed, exception.Message);
                }
            });

        if (!lifecycleSucceeded)
            return IsCurrentRepository(_context, repository)
                ? new(ForcePushExecutionKind.Failed, "The repository could not be refreshed after force push with lease.")
                : new(ForcePushExecutionKind.StaleRepository, "The repository changed while force push with lease was running.");

        return result ?? new(ForcePushExecutionKind.Failed, "Force push with lease did not produce a result.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Remotes.CollectionChanged -= Remotes_CollectionChanged;
        _settings.Changed -= PullSettings_Changed;
        Detach();
    }

    private async Task FetchSelectedRemoteAsync()
    {
        var repository = _context?.Repository;
        var remote = SelectedRemote;
        if (repository is null || remote is null) return;
        await FetchAsync(repository, remote.Name);
    }

    private async Task FetchAllCurrentAsync()
    {
        if (_context?.Repository is { } repository)
            await FetchAllAsync(repository);
    }

    private async Task PullCurrentAsync()
    {
        if (_context?.Repository is { } repository)
            await PullAsync(repository);
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IRepositorySyncContext.Repository))
        {
            ClearRepositoryState();
            return;
        }

        if (args.PropertyName is nameof(IRepositorySyncContext.IsBusy)
            or nameof(IRepositorySyncContext.CanRunSyncMutation)
            or nameof(IRepositorySyncContext.CurrentOperation)
            or nameof(IRepositorySyncContext.CurrentLocalBranch))
            RefreshAvailability();
    }

    private void Remotes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        RefreshAvailability();

    private bool CanRunQuery(
        IRepositorySyncContext? context,
        Repository repository) =>
        context is not null
        && !_operationInProgress
        && !context.IsBusy
        && context.CurrentOperation == RepositoryOperation.None
        && IsCurrentRepository(context, repository);

    private static bool IsCurrentRepository(
        IRepositorySyncContext? context,
        Repository repository) =>
        context is not null && ReferenceEquals(context.Repository, repository);

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
