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
    IReadOnlyList<GitRemote> IHistoryRepositoryContext.Remotes => RepositorySync.Remotes;
    IReadOnlyList<GitTag> IHistoryRepositoryContext.Tags => Tags.Items;
    bool IHistoryRepositoryContext.CanUpdateHistorySelection => Stashes.SelectedStash is null;

    void IHistoryRepositoryContext.EnterHistoryBusy() => EnterBusy();

    void IHistoryRepositoryContext.ExitHistoryBusy() => ExitBusy();

    void IHistoryRepositoryContext.ReportHistoryError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ErrorMessage = message;
    }

    private void History_SelectedRowChanged(HistoryRow? previous)
    {
        var selected = History.SelectedRow;
        Notify(nameof(HasSelectedCommit));

        if (_detailsSelectionCoordinationDepth != 0)
            return;

        if (selected is { } selectedRow && Stashes.SelectedStash is { } selectedStash)
        {
            if (string.Equals(selectedRow.Commit.Hash, selectedStash.Commit, StringComparison.Ordinal))
                return;

            _detailsSelectionCoordinationDepth++;
            try
            {
                Stashes.ClearSelection();
            }
            finally
            {
                _detailsSelectionCoordinationDepth--;
            }
        }

        if (selected is null)
        {
            if (Stashes.SelectedStash is null)
                CommitDetails.ClearSelection();
            return;
        }

        CommitDetails.ShowCommit(selected);
    }
}
