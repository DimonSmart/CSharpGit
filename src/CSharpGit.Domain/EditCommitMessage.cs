namespace CSharpGit.Domain;

public enum EditCommitMessageResultKind
{
    Completed,
    Conflicts,
    Failed
}

public sealed record EditCommitMessageResult(
    EditCommitMessageResultKind Kind,
    string Message,
    string OldCommit,
    string? NewCommit = null,
    string? NewHead = null);
