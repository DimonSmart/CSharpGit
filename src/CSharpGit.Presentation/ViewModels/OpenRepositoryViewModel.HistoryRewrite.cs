namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel
{
    internal Task<bool> RunHistoryRewriteMutationAsync(
        Func<Task> mutation,
        string? errorContext = null) =>
        MutateAsync(
            mutation,
            errorContext,
            includeHistory: true,
            localOnlyRefresh: true);

    internal void InvalidateForHistoryRewrite()
    {
        History.Invalidate(clearRows: true, exitReferenceScope: true);
        ResetCommitChangesSession();
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
}
