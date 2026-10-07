using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : IHistoryRepositoryContext
{
    bool? IHistoryRepositoryContext.HeadExists => _headExists;
    string? IHistoryRepositoryContext.HeadReference => _currentBranchName;
    string? IHistoryRepositoryContext.HeadCommit => _currentHeadCommit;
    bool IHistoryRepositoryContext.IsDetachedHead => _isDetachedHead;
    IReadOnlyList<GitBranch> IHistoryRepositoryContext.LocalBranches => Branches.LocalBranches;
    IReadOnlyList<GitBranch> IHistoryRepositoryContext.RemoteBranches => Branches.RemoteBranches;
    IReadOnlyList<GitRemote> IHistoryRepositoryContext.Remotes => Remotes;
    IReadOnlyList<GitTag> IHistoryRepositoryContext.Tags => Tags;
    bool IHistoryRepositoryContext.CanUpdateHistorySelection => SelectedStash is null;

    void IHistoryRepositoryContext.EnterHistoryBusy() => EnterBusy();

    void IHistoryRepositoryContext.ExitHistoryBusy() => ExitBusy();

    void IHistoryRepositoryContext.ReportHistoryError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ErrorMessage = message;
    }

    private void History_SelectedRowChanged(HistoryRow? previous)
    {
        if (History.SelectedRow is { } selected
            && SelectedStash is { } selectedStash
            && !string.Equals(selected.Commit.Hash, selectedStash.Commit, StringComparison.Ordinal))
        {
            ClearSelectedStashSelection();
        }

        Notify(nameof(HasSelectedCommit));
        Notify(nameof(HasSelectedDetailsObject));
        Notify(nameof(SelectedObjectCommit));
        Notify(nameof(SelectedDetailsTitle));
        Notify(nameof(SelectedDiffCommitHash));
        OnSelectedHistoryRowChanged(previous);
    }
}
