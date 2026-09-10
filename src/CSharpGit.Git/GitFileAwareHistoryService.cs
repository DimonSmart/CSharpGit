using System.Diagnostics;
using System.Text;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed class GitFileAwareHistoryService : IHistoryService, IReferenceHistoryService
{
    private readonly GitReferenceHistoryService _history;
    private readonly IRepositoryFileVersionService _versions;
    private readonly string _gitExecutable;

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
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        ArgumentNullException.ThrowIfNull(options);
        _gitExecutable = string.IsNullOrWhiteSpace(options.ExecutablePath) ? "git" : options.ExecutablePath;
    }

    public Task<HistoryPage> ReadHistoryAsync(
        Repository repository,
        HistoryQuery query,
        CancellationToken cancellationToken = default) =>
        _history.ReadHistoryAsync(repository, query, cancellationToken);

    public Task<HistoryPage> ReadHistoryAsync(
        Repository repository,
        string reference,
        string? filter,
        int skip,
        int take = 100,
        CancellationToken cancellationToken = default) =>
        _history.ReadHistoryAsync(repository, reference, filter, skip, take, cancellationToken);

    public async Task<CommitDetails> ReadCommitAsync(
        Repository repository,
        string hash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(hash);

        var baseDetails = await _history.ReadCommitAsync(repository, hash, cancellationToken);
        var firstParent = baseDetails.Commit.Parents.FirstOrDefault();
        var raw = await ReadCommitDeltaAsync(repository, hash, firstParent, "--raw", cancellationToken);
        var numstat = await ReadCommitDeltaAsync(repository, hash, firstParent, "--numstat", cancellationToken);
        var stats = ParseNumStat(numstat).ToList();

        var files = new List<ChangedFile>();
        foreach (var entry in ParseRawDiff(raw))
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
                string.Equals(entry.OldPath, entry.NewPath, StringComparison.Ordinal) ? null : entry.OldPath,
                entry.Status.ToString()));
        }

        return new CommitDetails(baseDetails.Commit, files);
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

        var pair = await _versions.ResolveCommitAsync(repository, hash, path, cancellationToken);
        var parents = await RunGitAsync(repository.WorkingDirectory, cancellationToken, "show", "-s", "--format=%P", hash);
        var firstParent = parents.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        string output;
        if (firstParent is null)
        {
            output = await RunGitAsync(
                repository.WorkingDirectory,
                cancellationToken,
                "show", "--format=", "--root", "--no-ext-diff", "--find-renames", "--find-copies",
                hash, "--", pair.Original.GitPath, pair.Changed.GitPath);
        }
        else
        {
            output = await RunGitAsync(
                repository.WorkingDirectory,
                cancellationToken,
                "diff", "--no-ext-diff", "--find-renames", "--find-copies",
                firstParent, hash, "--", pair.Original.GitPath, pair.Changed.GitPath);
        }

        var binary = output.Contains("Binary files ", StringComparison.Ordinal) ||
                     output.Contains("GIT binary patch", StringComparison.Ordinal);
        return new FileDiff(path, binary, binary ? [] : ParseDiffLines(output));
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadFileStatusesAsync(
        Repository repository,
        string commitHash,
        CancellationToken cancellationToken = default)
    {
        var details = await ReadCommitAsync(repository, commitHash, cancellationToken);
        return details.Files.ToDictionary(file => file.Path, file => file.Status, StringComparer.Ordinal);
    }

    private Task<string> ReadCommitDeltaAsync(
        Repository repository,
        string hash,
        string? firstParent,
        string format,
        CancellationToken cancellationToken)
    {
        if (firstParent is null)
        {
            return RunGitAsync(
                repository.WorkingDirectory,
                cancellationToken,
                "diff-tree", "--root", "--no-commit-id", format, "-r", "-z",
                "--find-renames", "--find-copies", hash);
        }

        return RunGitAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "diff", format, "-z", "--no-ext-diff", "--find-renames", "--find-copies",
            firstParent, hash);
    }

    private async Task<string> RunGitAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(_gitExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"Git exited with code {process.ExitCode}."
                : $"Git exited with code {process.ExitCode}: {error}");
        return output.TrimEnd('\r', '\n');
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
