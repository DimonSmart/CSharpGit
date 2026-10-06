using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : ITagsRepositoryContext
{
    IReadOnlyList<GitRemote> ITagsRepositoryContext.Remotes => Remotes;
    IReadOnlyList<GitBranch> ITagsRepositoryContext.LocalBranches => LocalBranches;

    Task<bool> ITagsRepositoryContext.RunTagMutationAsync(
        Func<Task> mutation,
        string errorContext,
        bool includeHistory) =>
        RunMutationAsync(mutation, errorContext, includeHistory);
}
