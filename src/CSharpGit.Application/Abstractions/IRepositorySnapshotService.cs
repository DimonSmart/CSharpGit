using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public enum RepositorySnapshotEntryKind
{
    File,
    Symlink,
    Submodule,
    Unsupported
}

public sealed record RepositorySnapshotEntry(
    string Path,
    RepositorySnapshotEntryKind Kind,
    string ObjectId,
    string Mode,
    string ObjectType);

public sealed record RepositoryContentSearchMatch(
    string Path,
    int LineNumber,
    string Snippet);

public interface IRepositorySnapshotService
{
    Task<IReadOnlyList<RepositorySnapshotEntry>> ReadTreeAsync(
        Repository repository,
        string commitHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryContentSearchMatch>> SearchContentAsync(
        Repository repository,
        string commitHash,
        string query,
        CancellationToken cancellationToken = default);

    Task<DiffFileVersion> ResolveFileVersionAsync(
        Repository repository,
        string commitHash,
        string path,
        CancellationToken cancellationToken = default);
}
