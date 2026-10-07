using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : IRepositoryOperationsContext
{
    bool IRepositoryOperationsContext.CanRunRepositoryMutation => CanMutate();

    Task<bool> IRepositoryOperationsContext.RunRepositoryOperationMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string? errorContext) =>
        ReferenceEquals(Repository, expectedRepository) && CanMutate()
            ? MutateAsync(
                mutation,
                errorContext,
                expectedRepository: expectedRepository)
            : Task.FromResult(false);
}
