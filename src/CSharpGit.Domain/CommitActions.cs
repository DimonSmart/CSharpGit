namespace CSharpGit.Domain;

public enum ApplyCommitResultKind
{
    Completed,
    Conflicts,
    Failed
}

public sealed record ApplyCommitResult(
    ApplyCommitResultKind Kind,
    string Message,
    string? HeadCommit = null);

public enum ResetMode
{
    Soft,
    Mixed,
    Hard
}
