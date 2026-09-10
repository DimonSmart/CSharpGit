namespace CSharpGit.Domain;

public sealed record ForcePushWithLeaseSnapshot(
    string LocalBranch,
    string LocalCommit,
    string Remote,
    string RemotePushDestination,
    string RemoteBranch,
    string ExpectedRemoteCommit,
    string? LocalCommitSubject = null,
    string? RemoteCommitSubject = null);
