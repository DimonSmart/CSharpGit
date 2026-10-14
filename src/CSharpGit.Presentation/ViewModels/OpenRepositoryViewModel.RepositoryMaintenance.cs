using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

internal enum RepositoryMaintenanceNotStartedReason
{
    None,
    GateBusy,
    StaleContext
}

internal sealed record RepositoryMaintenanceMutationResult(
    bool Started,
    Exception? MutationFailure,
    Exception? RefreshFailure,
    RepositoryMaintenanceNotStartedReason NotStartedReason = RepositoryMaintenanceNotStartedReason.None)
{
    public bool MutationSucceeded => Started && MutationFailure is null;
}

internal interface IRepositoryMaintenanceContext
{
    event EventHandler? MaintenanceAvailabilityChanged;
    Repository? CurrentMaintenanceRepository { get; }
    bool CanStartRepositoryMaintenance { get; }
    bool ShowMaintenanceReflog { get; }
    bool IsCurrentMaintenanceRepository(Repository repository);
    Task<RepositoryStorageStatistics?> ReadMaintenanceStatisticsAsync(
        IRepositoryMaintenanceService service, Repository repository);
    Task<RepositoryMaintenanceMutationResult> RunRepositoryMaintenanceMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        bool includeHistory,
        Func<Task>? additionalRefresh);
}

public sealed partial class OpenRepositoryViewModel : IRepositoryMaintenanceContext
{
    internal event EventHandler? MaintenanceAvailabilityChanged;

    event EventHandler? IRepositoryMaintenanceContext.MaintenanceAvailabilityChanged
    {
        add => MaintenanceAvailabilityChanged += value;
        remove => MaintenanceAvailabilityChanged -= value;
    }

    Repository? IRepositoryMaintenanceContext.CurrentMaintenanceRepository => Repository;

    bool IRepositoryMaintenanceContext.ShowMaintenanceReflog => History.ShowReflog;

    bool IRepositoryMaintenanceContext.CanStartRepositoryMaintenance =>
        Volatile.Read(ref _disposed) == 0
        && Volatile.Read(ref _repositoryChangeInProgress) == 0
        && Repository is not null
        && !IsBusy
        && !_isMutating
        && RepositoryOperations.CurrentOperation == RepositoryOperation.None;

    bool IRepositoryMaintenanceContext.IsCurrentMaintenanceRepository(Repository repository) =>
        IsMaintenanceSessionCurrent(repository);

    private bool IsMaintenanceSessionCurrent(Repository repository) =>
        Volatile.Read(ref _disposed) == 0
        && Volatile.Read(ref _repositoryChangeInProgress) == 0
        && ReferenceEquals(repository, Repository)
        && _repositoryLifetime?.IsActive == true;

    async Task<RepositoryStorageStatistics?> IRepositoryMaintenanceContext.ReadMaintenanceStatisticsAsync(
        IRepositoryMaintenanceService service, Repository repository)
    {
        var lifetime = _repositoryLifetime;
        if (lifetime is null || !IsMaintenanceSessionCurrent(repository)
            || !lifetime.TryAcquire(out var cancellationToken))
            return null;

        try
        {
            var result = await service.GetStorageStatisticsAsync(repository, cancellationToken);
            return ReferenceEquals(lifetime, _repositoryLifetime)
                && IsMaintenanceSessionCurrent(repository) ? result : null;
        }
        catch (OperationCanceledException) when (!lifetime.IsActive)
        {
            return null;
        }
        finally
        {
            lifetime.Release();
        }
    }

    Task<RepositoryMaintenanceMutationResult> IRepositoryMaintenanceContext.RunRepositoryMaintenanceMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        bool includeHistory,
        Func<Task>? additionalRefresh) =>
        RunRepositoryMaintenanceMutationAsync(expectedRepository, mutation, includeHistory, additionalRefresh);

    internal async Task<RepositoryMaintenanceMutationResult> RunRepositoryMaintenanceMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        bool includeHistory,
        Func<Task>? additionalRefresh = null)
    {
        ArgumentNullException.ThrowIfNull(expectedRepository);
        ArgumentNullException.ThrowIfNull(mutation);

        if (Volatile.Read(ref _disposed) != 0
            || Volatile.Read(ref _repositoryChangeInProgress) != 0)
            return new RepositoryMaintenanceMutationResult(false, null, null, RepositoryMaintenanceNotStartedReason.StaleContext);

        if (!await _mutationGate.WaitAsync(0))
            return new RepositoryMaintenanceMutationResult(false, null, null, RepositoryMaintenanceNotStartedReason.GateBusy);

        try
        {
            // Revalidate after acquisition, before changing busy state or invoking Git.
            // Do not check IsBusy after EnterBusy: that would reject our own mutation.
            if (!IsMaintenanceSessionCurrent(expectedRepository)
                || _isMutating
                || IsBusy
                || RepositoryOperations.CurrentOperation != RepositoryOperation.None)
                return new RepositoryMaintenanceMutationResult(false, null, null, RepositoryMaintenanceNotStartedReason.StaleContext);

            _isMutating = true;
            EnterBusy();
            RaiseCommands();

            try
            {
                Exception? mutationFailure = null;
                try
                {
                    await mutation();
                }
                catch (Exception exception)
                {
                    mutationFailure = exception;
                }

                Exception? refreshFailure = null;
                if (mutationFailure is null)
                {
                    try
                    {
                        await RefreshStateLocalOnlyAsync(includeHistory);
                    }
                    catch (Exception exception)
                    {
                        refreshFailure = exception;
                    }

                    if (additionalRefresh is not null)
                    {
                        try
                        {
                            await additionalRefresh();
                        }
                        catch (Exception exception)
                        {
                            refreshFailure ??= exception;
                        }
                    }
                }

                return new RepositoryMaintenanceMutationResult(
                    true, mutationFailure, refreshFailure);
            }
            finally
            {
                _isMutating = false;
                if (Volatile.Read(ref _disposed) == 0)
                {
                    ExitBusy();
                    RaiseCommands();
                }
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }
}
