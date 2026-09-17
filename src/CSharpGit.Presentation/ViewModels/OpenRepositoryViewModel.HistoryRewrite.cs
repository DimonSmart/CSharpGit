namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel
{
    internal void InvalidateForHistoryRewrite()
    {
        InvalidateHistoryLoad();
        ResetCommitChangesSession();
        History.Clear();
        HasMore = false;
        SelectedHistoryRow = null;
    }

    internal async Task SelectHistoryCommitAfterRewriteAsync(string commitHash)
    {
        if (string.IsNullOrWhiteSpace(commitHash)) return;

        var target = History.FirstOrDefault(row =>
            string.Equals(row.Commit.Hash, commitHash, StringComparison.Ordinal));
        target ??= await EnsureHistoryCommitVisibleAsync(commitHash);
        if (target is not null)
            SelectedHistoryRow = target;
    }
}
