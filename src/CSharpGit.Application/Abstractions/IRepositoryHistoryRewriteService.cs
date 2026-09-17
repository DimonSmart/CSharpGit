using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public enum HistoryRewriteFailureKind
{
    ToolUnavailable,
    InvalidPath,
    UnsupportedRepository,
    UnsafeRepositoryState,
    ShallowOrPartialRepository,
    RepositoryChangedDuringBackup,
    BackupCreationFailed,
    BackupVerificationFailed,
    RewriteFailed,
    RefTopologyChanged,
    PathVerificationFailed,
    CleanupFailed,
    FinalVerificationFailed
}

public sealed record HistoryRewriteToolStatus(
    bool IsAvailable,
    string? Version);

public sealed record PathRemovalAnalysis(
    string Path,
    int PathHistoryCommitCount,
    IReadOnlyList<string> AffectedLocalBranches,
    IReadOnlyList<string> AffectedTags,
    IReadOnlyList<string> AffectedRemoteTrackingBranches);

public sealed record PathRemovalResult(
    string Path,
    string BackupPath,
    int RewrittenCommitCount,
    string HeadObjectId);

public sealed class RepositoryHistoryRewriteException : InvalidOperationException
{
    public RepositoryHistoryRewriteException(
        HistoryRewriteFailureKind kind,
        string message,
        string? backupPath = null,
        bool destructivePhaseStarted = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        BackupPath = backupPath;
        DestructivePhaseStarted = destructivePhaseStarted;
    }

    public HistoryRewriteFailureKind Kind { get; }
    public string? BackupPath { get; }
    public bool DestructivePhaseStarted { get; }
}

public interface IRepositoryHistoryRewriteService
{
    Task<HistoryRewriteToolStatus> GetToolStatusAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    Task<PathRemovalAnalysis> AnalyzePathRemovalAsync(
        Repository repository,
        string path,
        CancellationToken cancellationToken = default);

    Task<PathRemovalResult> RemovePathFromHistoryAsync(
        Repository repository,
        string path,
        CancellationToken cancellationToken = default);
}
