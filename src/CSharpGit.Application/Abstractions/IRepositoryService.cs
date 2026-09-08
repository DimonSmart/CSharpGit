using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IRepositoryService
{
    Task<Repository> OpenAsync(string path, CancellationToken cancellationToken = default);
}
