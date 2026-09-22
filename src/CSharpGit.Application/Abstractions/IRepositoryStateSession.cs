using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IRepositoryStateSession : IAsyncDisposable
{
    RepositoryState Current { get; }
    event EventHandler<RepositoryState>? StateChanged;
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task RunMutationAsync(Func<CancellationToken, Task> mutation, CancellationToken cancellationToken = default);
}

public interface IRepositoryStateSessionFactory
{
    Task<IRepositoryStateSession> CreateAsync(Repository repository, CancellationToken cancellationToken = default);
}
