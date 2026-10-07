using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : IInteractiveRebaseRepositoryContext
{
    RepositoryOperation IInteractiveRebaseRepositoryContext.CurrentOperation =>
        RepositoryOperations.CurrentOperation;

    Task<bool> IInteractiveRebaseRepositoryContext.RunInteractiveRebaseMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string? errorContext) =>
        CanRunInteractiveRebaseMutation(expectedRepository)
            ? MutateAsync(
                mutation,
                errorContext,
                expectedRepository: expectedRepository)
            : Task.FromResult(false);

    void IInteractiveRebaseRepositoryContext.PublishOperationMessage(string message) =>
        RepositoryOperations.SetOperationDisplay(message);

    void IInteractiveRebaseRepositoryContext.ReportInteractiveRebaseError(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            ErrorMessage = message;
    }

    private bool CanRunInteractiveRebaseMutation(Repository expectedRepository) =>
        ReferenceEquals(Repository, expectedRepository)
        && CanMutate()
        && RepositoryOperations.CurrentOperation == RepositoryOperation.None;
}
