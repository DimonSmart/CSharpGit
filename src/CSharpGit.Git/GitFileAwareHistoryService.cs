using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed class GitFileAwareHistoryService : IHistoryService, IReferenceHistoryService
{
    private readonly GitReferenceHistoryService _history;
    private readonly GitProcessRunner _runner;

    public GitFileAwareHistoryService(
        GitReferenceHistoryService history,
        IRepositoryFileVersionService versions)
        : this(history, versions, new GitCliOptions())
    {
    }

    public GitFileAwareHistoryService(
        GitReferenceHistoryService history,
        IRepositoryFileVersionService versions,
        GitCliOptions options)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(options);
        _runner = new GitProcessRunner(string.IsNullOrWhiteSpace(options.ExecutablePath) ? "git" : options.ExecutablePath);
    }

    internal int GitInvocationCount => _runner.InvocationCount;

    public Task<HistoryPage> ReadHistoryAsync(
        Repository repository,
        HistoryQuery query,
        CancellationToken cancellationToken = default) =>
        _history.ReadHistoryAsync(repository, query, cancellationToken);

    public Task<HistoryPage> ReadHistoryThroughCommitAsync(
        Repository repository,
        HistoryScope scope,
        string targetHash,
        int trailingCount = 100,
        CancellationToken cancellationToken = default) =>
        _history.ReadHistoryThroughCommitAsync(repository, scope, targetHash, trailingCount, cancellationToken);

    public Task<HistoryPage> ReadHistoryAsync(
        Repository repository,
        string reference,
        string? filter,
        int skip,
        int take = 100,
        CancellationToken cancellationToken = default) =>
        _history.ReadHistoryAsync(repository, reference, filter, skip, take, cancellationToken);

    // Retained for explicit/non-navigation callers. History navigation itself does not call it.
    public async Task<CommitDetails> ReadCommitAsync(
        Repository repository,
        string hash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(hash);
        var metadata = await _history.ReadCommitAsync(repository, hash, cancellationToken);
        var parentHash = metadata.Commit.Parents.FirstOrDefault();
        var files = await ReadChangedFilesAsync(repository, hash, parentHash, cancellationToken);
        return new CommitDetails(metadata.Commit, files);
    }

    public async Task<IReadOnlyList<ChangedFile>> ReadChangedFilesAsync(
        Repository repository,
        string commitHash,
        string? parentHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(commitHash);
        if (parentHash is not null) ValidateObjectName(parentHash);

        string output;
        if (parentHash is null)
        {
            output = await _runner.RunAsync(
                repository.WorkingDirectory,
                "ChangedFiles",
                cancellationToken,
                "diff-tree", "--root", "--no-commit-id", "--raw", "--numstat", "-r", "-z",
                "--find-renames", "--find-copies", commitHash);
        }
        else
        {
            output = await _runner.RunAsync(
                repository.WorkingDirectory,
                "ChangedFiles",
                cancellationToken,
                "diff", "--raw", "--numstat", "-z", "--no-ext-diff",
                "--find-renames", "--find-copies", parentHash, commitHash);
        }

        var stats = ParseNumStat(output).ToList();
        var files = new List<ChangedFile>();
        foreach (var entry in ParseRawDiff(output))
        {
            var stat = stats.FirstOrDefault(candidate =>
                string.Equals(candidate.NewPath, entry.NewPath, StringComparison.Ordinal) &&
                string.Equals(candidate.OldPath, entry.OldPath, StringComparison.Ordinal));
            stat ??= stats.FirstOrDefault(candidate => string.Equals(candidate.NewPath, entry.NewPath, StringComparison.Ordinal));

            files.Add(new ChangedFile(
                entry.NewPath,
                stat?.AddedLines,
                stat?.RemovedLines,
                stat?.IsBinary ?? false,
                entry.Status is 'R' or 'C' ? entry.OldPath : null,
                entry.Status.ToString()));
        }
        return files;
    }

    // Legacy overload retained for non-navigation callers. It resolves metadata once and then
    // delegates to the same first-parent lazy operations used by the UI.
    public async Task<FileDiff> ReadDiffAsync(
        Repository repository,
        string hash,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(hash);
        ValidateGitPath(path);
        var metadata = await _history.ReadCommitAsync(repository, hash, cancellationToken);
        var parentHash = metadata.Commit.Parents.FirstOrDefault();
        var files = await ReadChangedFilesAsync(repository, hash, parentHash, cancellationToken);
        var file = files.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, path, StringComparison.Ordinal) ||
            string.Equals(candidate.OriginalPath, path, StringComparison.Ordinal));
        if (file is null) throw new InvalidOperationException("The selected file is no longer part of this commit diff.");
        return await ReadDiffAsync(repository, hash, parentHash, file, cancellationToken);
    }

    public async Task<FileDiff> ReadDiffAsync(
        Repository repository,
        string commitHash,
        string? parentHash,
        ChangedFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(file);
        ValidateObjectName(commitHash);
        if (parentHash is not null) ValidateObjectName(parentHash);
        ValidateGitPath(file.Path);
        if (file.OriginalPath is not null) ValidateGitPath(file.OriginalPath);

        var paths = file.OriginalPath is { } originalPath &&
                    !string.Equals(originalPath, file.Path, StringComparison.Ordinal)
            ? new[] { originalPath, file.Path }
            : new[] { file.Path };

        var arguments = new List<string>();
        if (parentHash is null)
        {
            arguments.AddRange(["show", "--format=", "--root", "--no-ext-diff", "--find-renames", "--find-copies", commitHash, "--"]);
        }
        else
        {
            arguments.AddRange(["diff", "--no-ext-diff", "--find-renames", "--find-copies", parentHash, commitHash, "--"]);
        }
        arguments.AddRange(paths);

        var output = await _runner.RunAsync(
            repository.WorkingDirectory,
            "Diff",
            cancellationToken,
            arguments.ToArray());
        var binary = output.Contains("Binary files ", StringComparison.Ordinal) ||
                     output.Contains("GIT binary patch", StringComparison.Ordinal);
        return new FileDiff(file.Path, binary, binary ? [] : ParseDiffLines(output));
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadFileStatusesAsync(
        Repository repository,
        string commitHash,
        CancellationToken cancellationToken = default)
    {
        var details = await ReadCommitAsync(repository, commitHash, cancellationToken);
        return details.Files.ToDictionary(file => file.Path, file => file.Status, StringComparer.Ordinal);
    }

    private static IEnumerable<RawDiffEntry> ParseRawDiff(string output)
    {
        var records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < records.Length; index++)
        {
            var metadataText = records[index].TrimStart('\r', '\n');
            if (!metadataText.StartsWith(':')) continue;
            var metadata = metadataText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length < 5 || index + 1 >= records.Length) continue;
            var statusText = metadata[4];
            if (statusText.Length == 0) continue;

            var status = statusText[0];
            var firstPath = records[++index];
            var oldPath = firstPath;
            var newPath = firstPath;
            if (status is 'R' or 'C' && index + 1 < records.Length)
                newPath = records[++index];
            yield return new RawDiffEntry(status, oldPath, newPath);
        }
    }

    private static IEnumerable<NumStatEntry> ParseNumStat(string output)
    {
        var records = output.Split('\0');
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index].TrimStart('\r', '\n');
            if (record.Length == 0) continue;
            var fields = record.Split('\t', 3);
            if (fields.Length != 3) continue;

            var binary = fields[0] == "-" || fields[1] == "-";
            int? added = int.TryParse(fields[0], out var addedValue) ? addedValue : null;
            int? removed = int.TryParse(fields[1], out var removedValue) ? removedValue : null;
            if (fields[2].Length > 0)
            {
                yield return new NumStatEntry(fields[2], fields[2], added, removed, binary);
                continue;
            }

            if (index + 2 >= records.Length) continue;
            var oldPath = records[++index];
            var newPath = records[++index];
            yield return new NumStatEntry(oldPath, newPath, added, removed, binary);
        }
    }

    private static IReadOnlyList<DiffLine> ParseDiffLines(string output) => output.Split('\n')
        .Select(line => new DiffLine(
            line.TrimEnd('\r'),
            line.StartsWith("+++") || line.StartsWith("---") || line.StartsWith("@@") || line.StartsWith("diff ") || line.StartsWith("index ")
                ? DiffLineKind.Header
                : line.StartsWith('+')
                    ? DiffLineKind.Added
                    : line.StartsWith('-')
                        ? DiffLineKind.Removed
                        : DiffLineKind.Context))
        .ToList();

    private static void ValidateObjectName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Invalid Git object id.", nameof(value));
    }

    private static void ValidateGitPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0') || Path.IsPathRooted(path))
            throw new ArgumentException("Invalid Git file path.", nameof(path));
    }

    private sealed record RawDiffEntry(char Status, string OldPath, string NewPath);
    private sealed record NumStatEntry(string OldPath, string NewPath, int? AddedLines, int? RemovedLines, bool IsBinary);
}
