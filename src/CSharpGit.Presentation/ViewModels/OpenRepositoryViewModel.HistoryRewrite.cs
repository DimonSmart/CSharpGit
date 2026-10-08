using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : IRepositoryHistoryRewriteContext
{
    Task<bool> IRepositoryHistoryRewriteContext.RunHistoryRewriteMutationAsync(
        Repository repository, Func<Task> mutation, string errorContext) =>
        RunHistoryRewriteMutationAsync(mutation, errorContext, repository);

    internal Task<bool> RunHistoryRewriteMutationAsync(
        Func<Task> mutation,
        string? errorContext = null,
        Repository? expectedRepository = null) =>
        MutateAsync(
            mutation,
            errorContext,
            beforeMutation: InvalidateForHistoryRewrite,
            includeHistory: true,
            localOnlyRefresh: true,
            expectedRepository: expectedRepository);

    internal void InvalidateForHistoryRewrite()
    {
        History.ResetForRepositoryMutation();
        Stashes.ClearSelection();
        CommitDetails.Invalidate();
        InteractiveRebase.Invalidate();
    }

}
