using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : ICommitCreationRepositoryContext
{
    bool ICommitCreationRepositoryContext.CanRunRepositoryMutation => CanMutate();

    bool ICommitCreationRepositoryContext.CanRunBulkMutation => CanBulkMutate();

    bool ICommitCreationRepositoryContext.HasStagedChanges =>
        WorkingTree.Changes.Any(change => change.IsStaged);

    bool ICommitCreationRepositoryContext.HasUnstagedChanges =>
        WorkingTree.Changes.Any(change => change.IsUnstaged);

    Task<bool> ICommitCreationRepositoryContext.RunCommitMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string? errorContext) =>
        ReferenceEquals(Repository, expectedRepository) && CanMutate()
            ? MutateAsync(
                mutation,
                errorContext,
                expectedRepository: expectedRepository)
            : Task.FromResult(false);

    void ICommitCreationRepositoryContext.ClearWorkingTreePresentationSelection() =>
        WorkingTree.ClearPresentationSelection();

    void ICommitCreationRepositoryContext.ClearCommittedWorkingTreePresentationSelection() =>
        WorkingTree.ClearCommittedPresentationSelection();
}
