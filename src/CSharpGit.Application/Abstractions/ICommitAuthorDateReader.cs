using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface ICommitAuthorDateReader
{
    Task<IReadOnlyDictionary<string, string>> ReadCommitAuthorDatesAsync(
        Repository repository,
        IReadOnlyList<string> commits,
        CancellationToken cancellationToken = default);
}
