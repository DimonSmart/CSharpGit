using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : IBranchesRepositoryContext
{
    IReadOnlyList<GitRemote> IBranchesRepositoryContext.Remotes => Remotes;

    Task<bool> IBranchesRepositoryContext.RunBranchMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string errorContext,
        bool includeHistory,
        Action? afterSuccessfulMutation) =>
        MutateAsync(
            mutation,
            errorContext,
            includeHistory: includeHistory,
            afterSuccessfulMutation: afterSuccessfulMutation,
            expectedRepository: expectedRepository);
}
