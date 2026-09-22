using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IWorkingTreeService
{
    Task StageFileAsync(Repository repository, WorkingTreeChange change, CancellationToken cancellationToken = default);
    Task StageFilesAsync(Repository repository, IReadOnlyCollection<WorkingTreeChange> changes, CancellationToken cancellationToken = default);
    Task StageAllAsync(Repository repository, CancellationToken cancellationToken = default);
    Task UnstageFileAsync(Repository repository, WorkingTreeChange change, CancellationToken cancellationToken = default);
    Task UnstageFilesAsync(Repository repository, IReadOnlyCollection<WorkingTreeChange> changes, CancellationToken cancellationToken = default);
    Task UnstageAllAsync(Repository repository, CancellationToken cancellationToken = default);
    Task DiscardFileAsync(Repository repository, WorkingTreeChange change, CancellationToken cancellationToken = default);
    Task DiscardAllFileChangesAsync(
        Repository repository,
        WorkingTreeChange change,
        CancellationToken cancellationToken = default);
    Task CommitAsync(Repository repository, string message, bool amend = false, bool intentionalEmpty = false, CancellationToken cancellationToken = default);
}
