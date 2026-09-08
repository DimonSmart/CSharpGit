using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

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
