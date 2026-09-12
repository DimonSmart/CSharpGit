namespace CSharpGit.Domain;

public sealed record WorktreeInfo(
    string Path,
    string Head,
    string? Branch,
    bool IsCurrent,
    bool IsDetached,
    bool IsLocked,
    string? LockReason,
    bool IsPrunable);
