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
    bool ResetAuthorDate,
    bool ResetToCurrentGitIdentity = false);

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

    IReadOnlyList<string> GetTargetCommits(
        InteractiveRebaseAuthorChangeRequest request) =>
        throw new NotSupportedException("Target commit discovery is not supported by this implementation.");

    InteractiveRebaseAuthorChangeResult Apply(
        InteractiveRebaseAuthorChangeRequest request);

    InteractiveRebaseAuthorChangeResult Apply(
        InteractiveRebaseAuthorChangeRequest request,
        IReadOnlyDictionary<string, string> originalAuthorDates) =>
        Apply(request);
}
