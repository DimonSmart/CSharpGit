using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : IFileActionsContext
{
    Repository? IFileActionsContext.FileActionsRepository => Repository;
    bool IFileActionsContext.CanStartFileActionsRead => CanStartUserGitOperation;

    bool IFileActionsContext.IsCurrentFileActionsRepository(Repository repository) =>
        Volatile.Read(ref _disposed) == 0
        && Volatile.Read(ref _repositoryChangeInProgress) == 0
        && ReferenceEquals(Repository, repository)
        && _repositoryLifetime?.IsActive == true;

    FileActionsReadLease? IFileActionsContext.AcquireFileActionsRead(Repository repository)
    {
        var admission = TryBeginUserGitOperation(repository);
        if (admission is null) return null;

        try
        {
            var lifetime = _repositoryLifetime;
            if (lifetime is null
                || !((IFileActionsContext)this).IsCurrentFileActionsRepository(repository)
                || !lifetime.TryAcquire(out var token))
            {
                admission.Dispose();
                return null;
            }

            return new FileActionsReadLease(lifetime, token, admission);
        }
        catch
        {
            admission.Dispose();
            throw;
        }
    }
}
