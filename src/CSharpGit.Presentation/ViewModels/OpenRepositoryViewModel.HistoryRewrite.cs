using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel
{
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
    }

    internal async Task SelectHistoryCommitAfterRewriteAsync(string commitHash)
    {
        if (string.IsNullOrWhiteSpace(commitHash)) return;

        var target = History.Rows.FirstOrDefault(row =>
            string.Equals(row.Commit.Hash, commitHash, StringComparison.Ordinal));
        target ??= await History.NavigateToCommitAsync(commitHash);
        if (target is not null)
            History.SelectedRow = target;
    }

    internal async Task<EditCommitMessageResult?> EditCommitMessageAsync(
        Repository expectedRepository,
        string commitHash,
        string newMessage)
    {
        EditCommitMessageResult? result = null;
        var succeeded = await MutateAsync(
            async () => result = await _commitActionService.EditCommitMessageAsync(
                expectedRepository,
                commitHash,
                newMessage),
            "Could not edit commit message",
            expectedRepository: expectedRepository);

        return succeeded ? result : null;
    }
}
