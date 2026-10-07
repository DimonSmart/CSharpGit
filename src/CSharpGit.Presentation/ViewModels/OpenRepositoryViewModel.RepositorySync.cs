using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : IRepositorySyncContext
{
    RepositoryOperation IRepositorySyncContext.CurrentOperation =>
        RepositoryOperations.CurrentOperation;

    GitBranch? IRepositorySyncContext.CurrentLocalBranch =>
        Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent);

    bool IRepositorySyncContext.CanRunSyncMutation => CanMutate();

    Task<bool> IRepositorySyncContext.RunSyncMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string? errorContext) =>
        MutateAsync(
            mutation,
            errorContext,
            expectedRepository: expectedRepository);
}
