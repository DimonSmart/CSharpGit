using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

internal interface IFileActionsContext
{
    Repository? FileActionsRepository { get; }
    bool IsCurrentFileActionsRepository(Repository repository);
    FileActionsReadLease? AcquireFileActionsRead(Repository repository);
}

internal sealed class FileActionsReadLease(RepositorySessionLifetime lifetime, CancellationToken token) : IDisposable
{
    private int _disposed;
    public CancellationToken Token { get; } = token;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            lifetime.Release();
    }
}

