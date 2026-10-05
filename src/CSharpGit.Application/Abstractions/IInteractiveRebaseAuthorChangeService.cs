namespace CSharpGit.Application.Abstractions;

public enum InteractiveRebaseAuthorChangeScope
{
    SelectedCommitLines,
    AllEligibleCommits
}

public sealed record InteractiveRebaseAuthorChangeAnalysis(
    int SelectedEligibleCount,
    int AllEligibleCount,
    int SelectedUnsupportedCount,
    int AllUnsupportedCount);

public sealed record InteractiveRebaseAuthorChangeRequest(
    string TodoText,
    int SelectionStart,
    int SelectionLength,
    InteractiveRebaseAuthorChangeScope Scope,
    string AuthorName,
    string AuthorEmail,
    bool ResetAuthorDate);

public sealed record InteractiveRebaseAuthorChangeResult(
    string TodoText,
    int EligibleCount,
    int ChangedCount,
    int UnsupportedCount);

public interface IInteractiveRebaseAuthorChangeService
{
    InteractiveRebaseAuthorChangeAnalysis Analyze(
        string todoText,
        int selectionStart,
        int selectionLength);

    InteractiveRebaseAuthorChangeResult Apply(
        InteractiveRebaseAuthorChangeRequest request);
}
