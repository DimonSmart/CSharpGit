using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed class GitRepositorySnapshotService : IRepositorySnapshotService
{
    private readonly GitCommandExecutor _executor;

    public GitRepositorySnapshotService() : this(GitCommandExecutor.Default) { }

    internal GitRepositorySnapshotService(GitCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public async Task<IReadOnlyList<RepositorySnapshotEntry>> ReadTreeAsync(
        Repository repository,
        string commitHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(commitHash);

        var output = await _executor.ExecuteAsync(
            repository.WorkingDirectory,
            "RepositorySnapshotTree",
            cancellationToken,
            "ls-tree", "-r", "-z", "--full-tree", commitHash);

        return ParseTree(output);
    }

    public async Task<IReadOnlyList<RepositoryContentSearchMatch>> SearchContentAsync(
        Repository repository,
        string commitHash,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(commitHash);
        if (string.IsNullOrWhiteSpace(query)) return [];

        var result = await _executor.ExecuteForResultAsync(
            repository.WorkingDirectory,
            "RepositorySnapshotContentSearch",
            GitCommandKind.Internal,
            cancellationToken,
            null,
            ["grep", "-n", "-I", "-F", "-i", "-z", "--full-name", "-e", query, commitHash, "--"]);

        if (result.ExitCode == 1) return [];
        if (result.ExitCode != 0) throw new GitCommandExecutionException(result);
        return ParseContentSearch(result.StandardOutput, commitHash);
    }

    public async Task<DiffFileVersion> ResolveFileVersionAsync(
        Repository repository,
        string commitHash,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(commitHash);
        ValidateGitPath(path);

        var output = await _executor.ExecuteAsync(
            repository.WorkingDirectory,
            "RepositorySnapshotResolveFile",
            cancellationToken,
            "ls-tree", "-z", "--full-tree", commitHash, "--", path);

        var entry = ParseTree(output).FirstOrDefault(candidate =>
            string.Equals(candidate.Path, path, StringComparison.Ordinal));
        if (entry is null)
            throw new InvalidOperationException("The requested path does not exist at the selected commit.");

        var entryKind = entry.Kind switch
        {
            RepositorySnapshotEntryKind.File => GitEntryKind.RegularFile,
            RepositorySnapshotEntryKind.Symlink => GitEntryKind.SymbolicLink,
            RepositorySnapshotEntryKind.Submodule => GitEntryKind.GitLink,
            _ => GitEntryKind.Unsupported
        };

        return new DiffFileVersion(
            DiffFileVersionLocation.GitSnapshot,
            entry.Path,
            commitHash,
            entry.ObjectId,
            entryKind,
            entryKind == GitEntryKind.RegularFile ? null : UnsupportedEntryMessage(entryKind));
    }

    internal static IReadOnlyList<RepositorySnapshotEntry> ParseTree(string output)
    {
        if (string.IsNullOrEmpty(output)) return [];

        var entries = new List<RepositorySnapshotEntry>();
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab <= 0 || tab == record.Length - 1)
                throw new FormatException("Git ls-tree returned an invalid NUL-delimited record.");

            var metadata = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length != 3)
                throw new FormatException("Git ls-tree returned invalid entry metadata.");

            var mode = metadata[0];
            var objectType = metadata[1];
            var objectId = metadata[2];
            var path = record[(tab + 1)..];
            entries.Add(new RepositorySnapshotEntry(
                path,
                EntryKind(mode, objectType),
                objectId,
                mode,
                objectType));
        }

        return entries;
    }

    internal static IReadOnlyList<RepositoryContentSearchMatch> ParseContentSearch(
        string output,
        string commitHash)
    {
        if (string.IsNullOrEmpty(output)) return [];

        var matches = new List<RepositoryContentSearchMatch>();
        var revisionPrefix = commitHash + ":";
        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r');
            var nul = line.IndexOf('\0');
            if (nul <= 0 || nul == line.Length - 1)
                throw new FormatException("Git grep returned an invalid NUL-delimited result.");

            var revisionAndPath = line[..nul];
            var path = revisionAndPath.StartsWith(revisionPrefix, StringComparison.Ordinal)
                ? revisionAndPath[revisionPrefix.Length..]
                : revisionAndPath;
            var rest = line[(nul + 1)..];

            var separator = rest.IndexOfAny(['\0', ':']);
            if (separator <= 0 || !int.TryParse(rest[..separator], out var lineNumber))
                throw new FormatException("Git grep returned an invalid line number.");

            var snippet = rest[(separator + 1)..];
            matches.Add(new RepositoryContentSearchMatch(path, lineNumber, snippet));
        }

        return matches;
    }

    private static RepositorySnapshotEntryKind EntryKind(string mode, string objectType) =>
        (mode, objectType) switch
        {
            ("100644" or "100755", "blob") => RepositorySnapshotEntryKind.File,
            ("120000", "blob") => RepositorySnapshotEntryKind.Symlink,
            ("160000", "commit") => RepositorySnapshotEntryKind.Submodule,
            _ => RepositorySnapshotEntryKind.Unsupported
        };

    private static string UnsupportedEntryMessage(GitEntryKind kind) => kind switch
    {
        GitEntryKind.SymbolicLink => "Opening symbolic-link snapshots is not supported.",
        GitEntryKind.GitLink => "Opening submodule entries is not supported.",
        _ => "Opening this Git entry type is not supported."
    };

    private static void ValidateObjectName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Invalid Git object id.", nameof(value));
    }

    private static void ValidateGitPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0') || Path.IsPathRooted(path))
            throw new ArgumentException("Invalid Git file path.", nameof(path));
        if (path.Replace('\\', '/').Split('/').Any(part => part is ".." or "."))
            throw new ArgumentException("Git file path traversal is not allowed.", nameof(path));
    }
}
