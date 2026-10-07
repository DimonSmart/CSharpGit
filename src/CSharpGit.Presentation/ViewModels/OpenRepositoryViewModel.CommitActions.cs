using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : ICommitActionsRepositoryContext
{
    RepositoryOperation ICommitActionsRepositoryContext.CurrentOperation =>
        RepositoryOperations.CurrentOperation;

    Task<bool> ICommitActionsRepositoryContext.RunCommitMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string errorContext) =>
        CanRunCommitAction(expectedRepository)
            ? MutateAsync(
                mutation,
                errorContext,
                expectedRepository: expectedRepository)
            : Task.FromResult(false);

    Task<bool> ICommitActionsRepositoryContext.RunCommitHistoryRewriteMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string errorContext) =>
        CanRunCommitAction(expectedRepository)
            ? RunHistoryRewriteMutationAsync(
                mutation,
                errorContext,
                expectedRepository)
            : Task.FromResult(false);

    private bool CanRunCommitAction(Repository expectedRepository) =>
        ReferenceEquals(Repository, expectedRepository)
        && !IsBusy
        && RepositoryOperations.CurrentOperation == RepositoryOperation.None;
}
