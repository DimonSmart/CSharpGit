using System.ComponentModel;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : IHistoryRepositoryContext
{
    private HistoryRow? _lastHistorySelection;

    bool? IHistoryRepositoryContext.HeadExists => _headExists;
    IReadOnlyList<GitBranch> IHistoryRepositoryContext.LocalBranches => Branches.LocalBranches;
    IReadOnlyList<GitBranch> IHistoryRepositoryContext.RemoteBranches => Branches.RemoteBranches;
    IReadOnlyList<GitRemote> IHistoryRepositoryContext.Remotes => Remotes;
    IReadOnlyList<GitTag> IHistoryRepositoryContext.Tags => Tags;
    bool IHistoryRepositoryContext.ShouldAutoSelectHistoryRow => SelectedStash is null;

    void IHistoryRepositoryContext.EnterHistoryBusy() => EnterBusy();
    void IHistoryRepositoryContext.ExitHistoryBusy() => ExitBusy();
    void IHistoryRepositoryContext.ReportHistoryError(string message) => ErrorMessage = message;

    private void History_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HistoryViewModel.SelectedRow)) return;

        var current = History.SelectedRow;
        if (current is not null
            && SelectedStash is { } selectedStash
            && !string.Equals(current.Commit.Hash, selectedStash.Commit, StringComparison.Ordinal))
        {
            ClearSelectedStashSelection();
        }

        var previous = _lastHistorySelection;
        _lastHistorySelection = current;
        Notify(nameof(HasSelectedCommit));
        Notify(nameof(HasSelectedDetailsObject));
        Notify(nameof(SelectedObjectCommit));
        Notify(nameof(SelectedDetailsTitle));
        Notify(nameof(SelectedDiffCommitHash));
        OnSelectedHistoryRowChanged(previous);
    }
}
