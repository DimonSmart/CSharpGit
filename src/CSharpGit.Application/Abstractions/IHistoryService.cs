using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IHistoryService
{
    Task<HistoryPage> ReadHistoryAsync(Repository repository, HistoryQuery query, CancellationToken cancellationToken = default);
    Task<HistoryPage> ReadHistoryThroughCommitAsync(
        Repository repository,
        HistoryScope scope,
        string targetHash,
        int trailingCount = 100,
        CancellationToken cancellationToken = default);
    Task<CommitDetails> ReadCommitAsync(Repository repository, string hash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChangedFile>> ReadChangedFilesAsync(
        Repository repository,
        string commitHash,
        string? parentHash,
        CancellationToken cancellationToken = default);
    Task<FileDiff> ReadDiffAsync(Repository repository, string hash, string path, CancellationToken cancellationToken = default);
    Task<FileDiff> ReadDiffAsync(
        Repository repository,
        string commitHash,
        string? parentHash,
        ChangedFile file,
        CancellationToken cancellationToken = default);
}

public interface IReferenceHistoryService
{
    Task<HistoryPage> ReadHistoryAsync(
        Repository repository,
        string reference,
        string? filter,
        int skip,
        int take = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> ReadFileStatusesAsync(
        Repository repository,
        string commitHash,
        CancellationToken cancellationToken = default);
}
