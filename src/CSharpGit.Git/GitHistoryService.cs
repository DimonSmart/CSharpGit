using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed class GitHistoryService : IHistoryService
{
    private readonly GitCommitHistoryReader _history;
    private readonly GitCommandExecutor _executor;

    internal GitHistoryService(
        GitCommitHistoryReader history,
        GitCommandExecutor executor)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

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

    public Task<HistoryPage> ReadHistoryThroughCommitAsync(
        Repository repository,
        HistoryQuery query,
        string targetHash,
        int trailingCount = 100,
        CancellationToken cancellationToken = default) =>
        _history.ReadHistoryThroughCommitAsync(repository, query, targetHash, trailingCount, cancellationToken);

    public async Task<CommitDetails> ReadCommitAsync(
        Repository repository,
        string hash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(hash);
        var commit = await _history.ReadCommitMetadataAsync(repository, hash, cancellationToken);
        var parentHash = commit.Parents.FirstOrDefault();
        var files = await ReadChangedFilesAsync(repository, hash, parentHash, cancellationToken);
        return new CommitDetails(commit, files);
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
            output = await _executor.ExecuteAsync(
                repository.WorkingDirectory,
                "ChangedFiles",
                cancellationToken,
                "diff-tree", "--root", "--no-commit-id", "--raw", "--numstat", "-r", "-z",
                "--find-renames", "--find-copies", commitHash);
        }
        else
        {
            output = await _executor.ExecuteAsync(
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

    public async Task<FileDiff> ReadDiffAsync(
        Repository repository,
        string hash,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(hash);
        ValidateGitPath(path);
        var commit = await _history.ReadCommitMetadataAsync(repository, hash, cancellationToken);
        var parentHash = commit.Parents.FirstOrDefault();
        var files = await ReadChangedFilesAsync(repository, hash, parentHash, cancellationToken);
        var file = files.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, path, StringComparison.Ordinal) ||
            string.Equals(candidate.OriginalPath, path, StringComparison.Ordinal));
        if (file is null) throw new InvalidOperationException("The selected file is no longer part of this commit diff.");
        return await ReadDiffAsync(repository, hash, parentHash, file, cancellationToken);
    }

    public Task<FileDiff> ReadDiffAsync(
        Repository repository,
        string commitHash,
        string? parentHash,
        ChangedFile file,
        CancellationToken cancellationToken = default) =>
        ReadDiffAsync(repository, commitHash, parentHash, file, DiffLoadMode.Preview, cancellationToken);

    public async Task<FileDiff> ReadDiffAsync(
        Repository repository,
        string commitHash,
        string? parentHash,
        ChangedFile file,
        DiffLoadMode mode,
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
            arguments.AddRange(["show", "--format=", "--root", "--no-ext-diff", commitHash, "--"]);
        }
        else
        {
            arguments.AddRange(["diff", "--no-ext-diff"]);
            if (file.Status is "R" or "C")
                arguments.AddRange(["--find-renames", "--find-copies"]);
            arguments.AddRange([parentHash, commitHash, "--"]);
        }
        arguments.AddRange(paths);

        string output;
        try
        {
            output = await _executor.ExecuteAsyncPreservingOutputEndings(
                repository.WorkingDirectory,
                "Diff",
                cancellationToken,
                mode == DiffLoadMode.Preview ? DiffPreviewPolicy.AutomaticOutputBytes : null,
                arguments);
        }
        catch (GitCommandOutputLimitExceededException exception) when (mode == DiffLoadMode.Preview)
        {
            throw new DiffPreviewTooLargeException(exception.LimitBytes);
        }
        var binary = GitDiffParser.IsBinary(output);
        if (binary) return new FileDiff(file.Path, true, []);

        var parsed = GitDiffParser.Parse(output);
        return new FileDiff(file.Path, false, parsed.Lines)
        {
            Diagnostics = parsed.Diagnostics
        };
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
