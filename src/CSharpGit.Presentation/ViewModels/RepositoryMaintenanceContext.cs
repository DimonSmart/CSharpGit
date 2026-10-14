using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public enum RepositoryMaintenanceNotStartedReason
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

