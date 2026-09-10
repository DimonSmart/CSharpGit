using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public enum DiffFileVersionLocation
{
    Unavailable,
    GitSnapshot,
    WorkingCopy
}

public enum GitEntryKind
{
    Missing,
    RegularFile,
    SymbolicLink,
    GitLink,
    Unsupported
}

public enum DiffFileSide
{
    Original,
    Changed
}

public sealed record DiffFileVersion(
    DiffFileVersionLocation Location,
    string GitPath,
    string? RevisionIdentity = null,
    string? BlobId = null,
    GitEntryKind EntryKind = GitEntryKind.RegularFile,
    string? UnavailableReason = null)
{
    public bool CanOpen =>
        Location != DiffFileVersionLocation.Unavailable && EntryKind == GitEntryKind.RegularFile;
}

public sealed record DiffFileVersionPair(
    DiffFileVersion Original,
    DiffFileVersion Changed,
    string RevealPath,
    string Status);

public sealed record MaterializedFileVersion(string Path);

public interface IRepositoryFileVersionService
{
    Task<DiffFileVersionPair> ResolveCommitAsync(
        Repository repository,
        string commitHash,
        string selectedPath,
        CancellationToken cancellationToken = default);

    Task<DiffFileVersionPair> ResolveWorkingTreeAsync(
        Repository repository,
        WorkingTreeChange change,
        WorkingTreeDiffKind kind,
        CancellationToken cancellationToken = default);

    Task<MaterializedFileVersion> MaterializeAsync(
        Repository repository,
        DiffFileVersion version,
        DiffFileSide side,
        CancellationToken cancellationToken = default);
}

public interface IRepositoryPathService
{
    string ResolveExistingWorkingTreeFile(
        Repository repository,
        string gitPath,
        bool allowFinalLink = false);
}

public interface IDesktopShellService
{
    string RevealDescription { get; }

    Task OpenFileAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task RevealFileAsync(
        string path,
        CancellationToken cancellationToken = default);
}
