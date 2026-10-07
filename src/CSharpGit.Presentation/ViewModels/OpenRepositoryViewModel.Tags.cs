using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : ITagsRepositoryContext
{
    RepositoryOperation ITagsRepositoryContext.CurrentOperation =>
        RepositoryOperations.CurrentOperation;

    IReadOnlyList<GitRemote> ITagsRepositoryContext.Remotes => RepositorySync.Remotes;
    IReadOnlyList<GitBranch> ITagsRepositoryContext.LocalBranches => Branches.LocalBranches;

    Task<bool> ITagsRepositoryContext.RunTagMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string errorContext,
        bool includeHistory) =>
        MutateAsync(
            mutation,
            errorContext,
            includeHistory: includeHistory,
            expectedRepository: expectedRepository);
}
