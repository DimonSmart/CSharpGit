using System.ComponentModel;
using System.Runtime.CompilerServices;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public interface IWorktreesRepositoryContext : INotifyPropertyChanged
{
    Repository? Repository { get; }
    bool IsBusy { get; }
    RepositoryOperation CurrentOperation { get; }
    string HeadDisplay { get; }
    string? ErrorMessage { get; }

    Task<bool> RunWorktreeMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation);
}

public sealed record WorktreeOperationResult(
    bool Succeeded,
    string? ErrorTitle = null,
    string? ErrorMessage = null,
    string? WorktreePath = null,
    bool Canceled = false)
{
    public bool Failed => !Succeeded && !Canceled;
}

public sealed class WorktreesViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IWorktreeService _worktreeService;
    private IWorktreesRepositoryContext? _context;
    private IReadOnlyList<WorktreeInfo> _worktrees = [];
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _operationCts;
    private long _refreshGeneration;
    private bool _isRefreshing;
    private bool _operationInProgress;
    private string? _refreshErrorMessage;
    private int _disposed;

    public WorktreesViewModel(IWorktreeService worktreeService)
    {
        _worktreeService = worktreeService ?? throw new ArgumentNullException(nameof(worktreeService));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<WorktreeInfo> Worktrees => _worktrees;

    public bool IsBusy =>
        _isRefreshing ||
        _operationInProgress ||
        _context?.IsBusy == true;

    public bool CanMutate =>
        _context?.Repository is not null &&
        !IsBusy &&
        _context.CurrentOperation == RepositoryOperation.None;

    public string? RefreshErrorMessage
    {
        get => _refreshErrorMessage;
        private set
        {
            if (string.Equals(_refreshErrorMessage, value, StringComparison.Ordinal)) return;
            _refreshErrorMessage = value;
            Notify();
        }
    }

    internal void Attach(IWorktreesRepositoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (ReferenceEquals(_context, context)) return;

        Detach();
        _context = context;
        _context.PropertyChanged += Context_PropertyChanged;
        NotifyAvailability();
        _ = RefreshAsync();
    }

    internal void Detach()
    {
        if (_context is not null)
            _context.PropertyChanged -= Context_PropertyChanged;
        _context = null;

        CancelAndDispose(ref _operationCts);
        CancelAndDispose(ref _refreshCts);
        Interlocked.Increment(ref _refreshGeneration);
        _operationInProgress = false;
        _isRefreshing = false;
        SetWorktrees([]);
        RefreshErrorMessage = null;
        NotifyAvailability();
    }

    public async Task RefreshAsync(bool throwOnError = false)
    {
        var repository = _context?.Repository;
        await RefreshCoreAsync(repository, throwOnError);
    }

    public WorktreeInfo? FindWorktreeForBranch(string branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        return _worktrees.FirstOrDefault(worktree =>
            string.Equals(worktree.Branch, branch, StringComparison.Ordinal));
    }

    public bool CanRemove(WorktreeInfo worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);
        return CanMutate &&
               !worktree.IsPrimary &&
               !worktree.IsCurrent &&
               !worktree.IsLocked;
    }

    public bool CanLock(WorktreeInfo worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);
        return CanMutate && !worktree.IsLocked;
    }

    public bool CanUnlock(WorktreeInfo worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);
        return CanMutate && worktree.IsLocked;
    }

    public async Task<WorktreeOperationResult> CreateFromBranchAsync(
        Repository repository,
        string requestedPath,
        GitBranch branch)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(branch);

        if (!CanRunMutation(repository))
            return CanceledResult();

        if (FindWorktreeForBranch(branch.Name) is { } existing)
            return Failure(
                "Branch already has a worktree",
                $"Branch '{branch.Name}' is already checked out in '{existing.Path}'.");

        string path;
        try
        {
            path = NormalizeRequestedWorktreePath(repository, requestedPath);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure("Could not create worktree", FormatWorktreeError(exception));
        }

        return await RunMutationAsync(
            repository,
            "Could not create worktree",
            cancellationToken => _worktreeService.AddAsync(repository, path, branch.Name, cancellationToken),
            path);
    }

    public async Task<WorktreeOperationResult> CreateNewBranchAsync(
        Repository repository,
        string requestedPath,
        string newBranch,
        string startPoint)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (!CanRunMutation(repository))
            return CanceledResult();

        if (string.IsNullOrWhiteSpace(newBranch) || string.IsNullOrWhiteSpace(startPoint))
            return Failure(
                "Could not create worktree",
                "New branch and start point are required.");

        string path;
        try
        {
            path = NormalizeRequestedWorktreePath(repository, requestedPath);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure("Could not create worktree", FormatWorktreeError(exception));
        }

        var branch = newBranch.Trim();
        var start = startPoint.Trim();
        return await RunMutationAsync(
            repository,
            "Could not create worktree",
            cancellationToken => _worktreeService.AddNewBranchAsync(
                repository,
                path,
                branch,
                start,
                cancellationToken),
            path);
    }

    public Task<WorktreeOperationResult> LockAsync(
        Repository repository,
        WorktreeInfo worktree,
        string? reason)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(worktree);

        if (!CanRunMutation(repository))
            return Task.FromResult(CanceledResult());

        if (worktree.IsLocked)
            return Task.FromResult(Failure("Could not lock worktree", "The worktree is already locked."));

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return RunMutationAsync(
            repository,
            "Could not lock worktree",
            cancellationToken => _worktreeService.LockAsync(
                repository,
                worktree,
                normalizedReason,
                cancellationToken));
    }

    public Task<WorktreeOperationResult> UnlockAsync(
        Repository repository,
        WorktreeInfo worktree)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(worktree);

        if (!CanRunMutation(repository))
            return Task.FromResult(CanceledResult());

        if (!worktree.IsLocked)
            return Task.FromResult(Failure("Could not unlock worktree", "The worktree is not locked."));

        return RunMutationAsync(
            repository,
            "Could not unlock worktree",
            cancellationToken => _worktreeService.UnlockAsync(repository, worktree, cancellationToken));
    }

    public Task<WorktreeOperationResult> RemoveAsync(
        Repository repository,
        WorktreeInfo worktree,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(worktree);

        if (!CanRunMutation(repository))
            return Task.FromResult(CanceledResult());

        if (worktree.IsPrimary || worktree.IsCurrent)
            return Task.FromResult(Failure(
                force ? "Could not force remove worktree" : "Could not remove worktree",
                "The primary or current worktree cannot be removed."));

        if (worktree.IsLocked && !force)
            return Task.FromResult(Failure(
                "Could not remove worktree",
                "The worktree is locked. Unlock it before removing it, or use an explicit force operation."));

        return RunMutationAsync(
            repository,
            force ? "Could not force remove worktree" : "Could not remove worktree",
            cancellationToken => _worktreeService.RemoveAsync(
                repository,
                worktree,
                force,
                cancellationToken));
    }

    public Task<WorktreeOperationResult> PruneAsync(Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (!CanRunMutation(repository))
            return Task.FromResult(CanceledResult());

        return RunMutationAsync(
            repository,
            "Could not prune worktrees",
            cancellationToken => _worktreeService.PruneAsync(repository, cancellationToken));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Detach();
    }

    private async Task<WorktreeOperationResult> RunMutationAsync(
        Repository repository,
        string errorTitle,
        Func<CancellationToken, Task> mutation,
        string? worktreePath = null)
    {
        var context = _context;
        if (!CanRunMutation(repository) || context is null)
            return CanceledResult();

        _operationInProgress = true;
        NotifyAvailability();

        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _operationCts, cancellation);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            Exception? mutationFailure = null;
            var mutationStarted = false;
            var lifecycleSucceeded = await context.RunWorktreeMutationAsync(
                repository,
                async () =>
                {
                    mutationStarted = true;
                    try
                    {
                        await mutation(cancellation.Token);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        mutationFailure = exception;
                    }
                });

            if (cancellation.IsCancellationRequested
                || !IsCurrentRepository(context, repository))
                return CanceledResult();

            if (mutationFailure is not null)
                return Failure(errorTitle, FormatWorktreeError(mutationFailure));

            if (!lifecycleSucceeded)
                return mutationStarted
                    ? Failure(
                        errorTitle,
                        string.IsNullOrWhiteSpace(context.ErrorMessage)
                            ? "The repository could not be refreshed after the worktree operation."
                            : context.ErrorMessage)
                    : CanceledResult();

            await RefreshCoreAsync(repository, throwOnError: true);
            return IsCurrentRepository(context, repository)
                ? Success(worktreePath)
                : CanceledResult();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return CanceledResult();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure(errorTitle, FormatWorktreeError(exception));
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _operationCts, null, cancellation), cancellation))
                cancellation.Dispose();

            _operationInProgress = false;
            NotifyAvailability();
        }
    }

    private async Task RefreshCoreAsync(
        Repository? expectedRepository,
        bool throwOnError)
    {
        var generation = Interlocked.Increment(ref _refreshGeneration);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _refreshCts, cancellation);
        previous?.Cancel();
        previous?.Dispose();

        if (expectedRepository is null || !IsCurrentRepository(_context, expectedRepository))
        {
            if (generation == Volatile.Read(ref _refreshGeneration))
            {
                SetWorktrees([]);
                RefreshErrorMessage = null;
                SetRefreshing(false);
            }
            Interlocked.CompareExchange(ref _refreshCts, null, cancellation);
            cancellation.Dispose();
            return;
        }

        SetRefreshing(true);
        try
        {
            var worktrees = await _worktreeService.ListAsync(expectedRepository, cancellation.Token);
            if (generation != Volatile.Read(ref _refreshGeneration)
                || !IsCurrentRepository(_context, expectedRepository))
                return;

            SetWorktrees(worktrees);
            RefreshErrorMessage = null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (!throwOnError && exception is not OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _refreshGeneration)
                && IsCurrentRepository(_context, expectedRepository))
                RefreshErrorMessage = FormatWorktreeError(exception);
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _refreshCts, null, cancellation), cancellation))
                cancellation.Dispose();

            if (generation == Volatile.Read(ref _refreshGeneration))
                SetRefreshing(false);
        }
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IWorktreesRepositoryContext.Repository))
        {
            _operationCts?.Cancel();
            SetWorktrees([]);
            RefreshErrorMessage = null;
            NotifyAvailability();
            _ = RefreshAsync();
            return;
        }

        if (args.PropertyName == nameof(IWorktreesRepositoryContext.HeadDisplay))
        {
            if (!_operationInProgress)
                _ = RefreshAsync();
            return;
        }

        if (args.PropertyName is nameof(IWorktreesRepositoryContext.IsBusy)
            or nameof(IWorktreesRepositoryContext.CurrentOperation))
            NotifyAvailability();
    }

    private void SetWorktrees(IReadOnlyList<WorktreeInfo> value)
    {
        if (ReferenceEquals(_worktrees, value)
            || _worktrees.Count == 0 && value.Count == 0)
            return;

        _worktrees = value;
        Notify(nameof(Worktrees));
    }

    private void SetRefreshing(bool value)
    {
        if (_isRefreshing == value) return;
        _isRefreshing = value;
        NotifyAvailability();
    }

    private void NotifyAvailability()
    {
        Notify(nameof(IsBusy));
        Notify(nameof(CanMutate));
    }

    private bool CanRunMutation(Repository expectedRepository) =>
        CanMutate && IsCurrentRepository(_context, expectedRepository);

    private static bool IsCurrentRepository(
        IWorktreesRepositoryContext? context,
        Repository expectedRepository) =>
        context is not null && ReferenceEquals(context.Repository, expectedRepository);

    private static string NormalizeRequestedWorktreePath(
        Repository repository,
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, repository.WorkingDirectory);
    }

    private static string FormatWorktreeError(Exception exception)
    {
        var message = exception.Message;
        var lower = message.ToLowerInvariant();
        if (lower.Contains("already checked out", StringComparison.Ordinal))
            return "The branch is already checked out in another worktree.\n\n" + message;
        if (lower.Contains("already exists", StringComparison.Ordinal)
            || lower.Contains("destination", StringComparison.Ordinal)
            && lower.Contains("exists", StringComparison.Ordinal))
            return "The destination already exists. Choose another directory.\n\n" + message;
        if (lower.Contains("contains modified or untracked", StringComparison.Ordinal)
            || lower.Contains("is dirty", StringComparison.Ordinal))
            return "The worktree contains modified or untracked files. Use Force Remove only if those changes may be discarded.\n\n" + message;
        if (lower.Contains("locked", StringComparison.Ordinal))
            return "The worktree is locked. Unlock it before removing it, or use an explicit force operation.\n\n" + message;
        if (lower.Contains("not a valid object name", StringComparison.Ordinal)
            || lower.Contains("unknown revision", StringComparison.Ordinal))
            return "The branch or start point does not exist.\n\n" + message;
        if (lower.Contains("not a working tree", StringComparison.Ordinal)
            || lower.Contains("no such file", StringComparison.Ordinal))
            return "The worktree path no longer exists.\n\n" + message;
        if (lower.Contains("not a git command", StringComparison.Ordinal)
            || lower.Contains("unknown subcommand", StringComparison.Ordinal))
            return "The installed Git version does not support the required worktree operation.\n\n" + message;
        return message;
    }

    private static WorktreeOperationResult Success(string? path) =>
        new(true, WorktreePath: path);

    private static WorktreeOperationResult Failure(string title, string message) =>
        new(false, title, message);

    private static WorktreeOperationResult CanceledResult() =>
        new(false, Canceled: true);

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        var cancellation = Interlocked.Exchange(ref source, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
