using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IMergeService
{
    Task<MergeResult> MergeAsync(
        Repository repository,
        string branch,
        CancellationToken cancellationToken = default);
}
