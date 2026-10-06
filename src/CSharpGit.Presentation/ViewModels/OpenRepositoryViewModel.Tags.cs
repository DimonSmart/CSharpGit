using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : ITagsRepositoryContext
{
    IReadOnlyList<GitRemote> ITagsRepositoryContext.Remotes => Remotes;
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
