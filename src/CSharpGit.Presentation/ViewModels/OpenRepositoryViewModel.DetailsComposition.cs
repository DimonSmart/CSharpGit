using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel :
    IStashesRepositoryContext,
    ICommitDetailsRepositoryContext
{
    private int _detailsSelectionCoordinationDepth;

    Repository? IStashesRepositoryContext.Repository => Repository;

    bool IStashesRepositoryContext.CanCreateStash =>
        CanMutate()
        && RepositoryOperations.CurrentOperation == RepositoryOperation.None
        && !RepositoryOperations.Conflicts.Any(conflict => !conflict.IsResolved);

    bool IStashesRepositoryContext.CanMutateStash => CanMutate();

    IReadOnlyList<WorkingTreeChange> IStashesRepositoryContext.WorkingTreeChanges =>
        WorkingTree.Changes;

    Task<bool> IStashesRepositoryContext.RunStashMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string errorContext) =>
        MutateAsync(
            mutation,
            errorContext,
            expectedRepository: expectedRepository);

    Repository? ICommitDetailsRepositoryContext.Repository => Repository;

    void ICommitDetailsRepositoryContext.ReportCommitDetailsError(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            ErrorMessage = message;
    }

    private void Stashes_SelectedStashChanged(GitStash? stash)
    {
        if (_detailsSelectionCoordinationDepth != 0)
            return;

        if (stash is null)
        {
            if (History.SelectedRow is { } selected)
                CommitDetails.ShowCommit(selected);
            else
                CommitDetails.ClearSelection();
            return;
        }

        if (History.SelectedRow is not { } historyRow
            || !string.Equals(historyRow.Commit.Hash, stash.Commit, StringComparison.Ordinal))
        {
            _detailsSelectionCoordinationDepth++;
            try
            {
                History.SelectedRow = null;
            }
            finally
            {
                _detailsSelectionCoordinationDepth--;
            }
        }

        _ = CommitDetails.ShowStashAsync(stash);
    }
}
