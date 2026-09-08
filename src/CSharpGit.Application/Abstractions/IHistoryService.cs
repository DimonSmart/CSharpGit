using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IHistoryService
{
    Task<HistoryPage> ReadHistoryAsync(Repository repository, HistoryQuery query, CancellationToken cancellationToken = default);
    Task<CommitDetails> ReadCommitAsync(Repository repository, string hash, CancellationToken cancellationToken = default);
    Task<FileDiff> ReadDiffAsync(Repository repository, string hash, string path, CancellationToken cancellationToken = default);
}
