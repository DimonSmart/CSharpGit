namespace CSharpGit.Domain;

public sealed record CreateTagRequest(
    string Name,
    string TargetCommit,
    GitTagKind Kind,
    string? Message = null);

public sealed record RemoteTagInfo(
    string Remote,
    string Name,
    string ObjectId,
    string TargetCommit,
    bool IsAnnotated);

public sealed record RemoteTagConflictSnapshot(
    string Remote,
    string TagName,
    string CurrentRemoteObjectId,
    string CurrentRemoteTarget,
    string NewLocalObjectId,
    string NewLocalTarget);

public enum PushTagResultKind
{
    Pushed,
    AlreadyUpToDate,
    Conflict
}

public sealed record PushTagResult(
    PushTagResultKind Kind,
    string Message,
    RemoteTagConflictSnapshot? Conflict = null);
