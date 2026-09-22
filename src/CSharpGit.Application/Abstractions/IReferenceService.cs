using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IReferenceService
{
    Task SwitchBranchAsync(Repository repository, string branch, CancellationToken cancellationToken = default);
    Task CheckoutAsync(Repository repository, string reference, CancellationToken cancellationToken = default);
    Task CreateBranchAsync(Repository repository, string branch, string? startPoint = null, bool switchToBranch = true, CancellationToken cancellationToken = default);
    Task RenameBranchAsync(Repository repository, string oldName, string newName, CancellationToken cancellationToken = default);
    Task DeleteBranchAsync(Repository repository, string branch, CancellationToken cancellationToken = default);
    Task DeleteBranchAsync(Repository repository, string branch, BranchDeletionMode mode, CancellationToken cancellationToken = default);
    Task DeleteRemoteBranchAsync(Repository repository, string remote, string branch, CancellationToken cancellationToken = default);
    Task CheckoutRemoteBranchAsync(Repository repository, string remoteBranch, string localBranch, CancellationToken cancellationToken = default);
}
