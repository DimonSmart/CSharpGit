using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public sealed record RepositoryStorageStatistics(
    long LooseObjectCount,
    long PackedObjectCount,
    int PackCount,
    long LooseObjectsSizeBytes,
    long PackedObjectsSizeBytes)
{
    public long ObjectStorageSizeBytes =>
        checked(LooseObjectsSizeBytes + PackedObjectsSizeBytes);
}

public sealed record RepositoryGcOptions(
    bool Aggressive = false,
    bool PruneNow = false,
    bool KeepLargestPack = false);

public interface IRepositoryMaintenanceService
{
    Task<RepositoryStorageStatistics> GetStorageStatisticsAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    Task GarbageCollectAsync(
        Repository repository,
        RepositoryGcOptions options,
        CancellationToken cancellationToken = default);
}
