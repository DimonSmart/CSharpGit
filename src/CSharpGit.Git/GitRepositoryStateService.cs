using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitRepositoryStateService : IRepositoryStateService
{
    private readonly GitRepositoryCommandRunner _runner;

    internal GitRepositoryStateService(GitCommandExecutor executor)
        : this(new GitRepositoryCommandRunner(executor))
    {
    }

    internal GitRepositoryStateService(GitRepositoryCommandRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public Task<RepositoryState> ReadLocalOnlyAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        ReadAsync(repository, cancellationToken);

    public async Task<RepositoryState> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        await _runner.EnsureGitAvailableAsync(cancellationToken);

        var headReference = await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "symbolic-ref",
            "--quiet",
            "--short",
            "HEAD");
        var headCommit = await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD");
        var status = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "status",
            "--porcelain=v1",
            "-z",
            "--untracked-files=all");
        var globalConfig = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "config",
            "--global",
            "--null",
            "--list");
        var localConfig = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "config",
            "--local",
            "--null",
            "--list");
        var references = await ReadReferencesAsync(
            repository,
            headReference,
            cancellationToken);
        var stashes = await ReadStashesAsync(repository, cancellationToken);

        var operation = GitOperationDetector.Detect(repository);
        var operationState = await ReadOperationStateAsync(
            repository,
            operation,
            status,
            cancellationToken);

        return new RepositoryState(
            repository,
            EmptyToNull(headReference),
            EmptyToNull(headCommit),
            string.IsNullOrWhiteSpace(headReference),
            operation,
            ParseStatus(status),
            ParseConfiguration(globalConfig),
            ParseConfiguration(localConfig),
            DateTimeOffset.UtcNow,
            references,
            stashes,
            operationState);
    }

    private async Task<GitReferences> ReadReferencesAsync(
        Repository repository,
        string currentBranch,
        CancellationToken cancellationToken)
    {
        const string format =
            "%(refname)%00%(objectname)%00%(*objectname)%00%(upstream:short)%00%(upstream:track)%1e";
        var output = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "for-each-ref",
            $"--format={format}",
            "refs/heads",
            "refs/remotes",
            "refs/tags");

        var local = new List<GitBranch>();
        var remote = new List<GitBranch>();
        var tags = new List<GitTag>();

        foreach (var record in output.Split(
                     '\x1e',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimStart('\r', '\n').Split('\0');
            if (fields.Length < 5) continue;

            var fullName = fields[0];
            if (fullName.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                var name = fullName[11..];
                var (ahead, behind) = ParseTracking(fields[4]);
                local.Add(
                    new GitBranch(
                        name,
                        fields[1],
                        string.Equals(
                            name,
                            currentBranch,
                            StringComparison.Ordinal),
                        EmptyToNull(fields[3]),
                        ahead,
                        behind));
            }
            else if (fullName.StartsWith(
                         "refs/remotes/",
                         StringComparison.Ordinal)
                     && !fullName.EndsWith(
                         "/HEAD",
                         StringComparison.Ordinal))
            {
                remote.Add(new GitBranch(fullName[13..], fields[1]));
            }
            else if (fullName.StartsWith(
                         "refs/tags/",
                         StringComparison.Ordinal))
            {
                tags.Add(
                    new GitTag(
                        fullName[10..],
                        string.IsNullOrEmpty(fields[2])
                            ? fields[1]
                            : fields[2]));
            }
        }

        var remotes = new List<GitRemote>();
        var remoteNames = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "remote");

        foreach (var name in remoteNames.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            var fetchUrl = await _runner.RunAsync(
                repository.WorkingDirectory,
                cancellationToken,
                false,
                "remote",
                "get-url",
                name);
            var pushUrl = await _runner.RunAsync(
                repository.WorkingDirectory,
                cancellationToken,
                false,
                "remote",
                "get-url",
                "--push",
                name);
            remotes.Add(new GitRemote(name, fetchUrl, pushUrl));
        }

        return new GitReferences(local, remote, remotes, tags);
    }

    private async Task<IReadOnlyList<GitStash>> ReadStashesAsync(
        Repository repository,
        CancellationToken cancellationToken)
    {
        var output = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "stash",
            "list",
            "--format=%gd%x00%H%x00%gs%x1e");

        var result = new List<GitStash>();
        foreach (var record in output.Split(
                     '\x1e',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimStart('\r', '\n').Split('\0', 3);
            if (fields.Length == 3)
            {
                result.Add(
                    new GitStash(
                        fields[0],
                        fields[1],
                        fields[2].TrimEnd('\r', '\n')));
            }
        }

        return result;
    }

    private async Task<RepositoryOperationState> ReadOperationStateAsync(
        Repository repository,
        RepositoryOperation operation,
        string status,
        CancellationToken cancellationToken)
    {
        if (operation == RepositoryOperation.None)
            return RepositoryOperationState.None;

        var unmerged = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "ls-files",
            "--unmerged",
            "-z");
        var stages = ParseUnmergedStages(unmerged);
        var knownPaths = new HashSet<string>(
            stages.Keys,
            StringComparer.Ordinal);

        AddConflictPathsFromMessage(
            Path.Combine(repository.GitDirectory, "MERGE_MSG"),
            knownPaths);
        AddConflictPathsFromMessage(
            Path.Combine(
                repository.GitDirectory,
                "rebase-merge",
                "message"),
            knownPaths);
        AddConflictPathsFromMessage(
            Path.Combine(
                repository.GitDirectory,
                "rebase-apply",
                "final-commit"),
            knownPaths);

        var changes = ParseStatus(status)
            .ToDictionary(change => change.Path, StringComparer.Ordinal);
        var conflicts = new List<ConflictFile>();

        foreach (var path in knownPaths.Order(StringComparer.Ordinal))
        {
            stages.TryGetValue(path, out var presentStages);
            presentStages ??= [];
            changes.TryGetValue(path, out var change);

            var unresolved = presentStages.Count > 0;
            var currentExists = presentStages.Contains(
                operation == RepositoryOperation.Rebase ? 3 : 2);
            var incomingExists = presentStages.Contains(
                operation == RepositoryOperation.Rebase ? 2 : 3);
            var isBinary = unresolved
                           && await IsBinaryConflictAsync(
                               repository,
                               path,
                               cancellationToken);
            var kind = isBinary
                ? ConflictKind.Binary
                : change is { IndexStatus: 'A', WorkingTreeStatus: 'A' }
                    ? ConflictKind.AddAdd
                    : currentExists && !incomingExists
                        ? ConflictKind.ModifyDelete
                        : !currentExists && incomingExists
                            ? ConflictKind.DeleteModify
                            : ConflictKind.Textual;

            var labels = operation == RepositoryOperation.Rebase
                ? (
                    "Current/local (replayed commit)",
                    "Incoming/remote (rebase base)")
                : ("Current/local", "Incoming/remote");

            conflicts.Add(
                new ConflictFile(
                    path,
                    kind,
                    !unresolved,
                    unresolved
                    && !isBinary
                    && File.Exists(
                        Path.Combine(
                            repository.WorkingDirectory,
                            path)),
                    unresolved && currentExists,
                    unresolved && incomingExists,
                    unresolved && (!currentExists || !incomingExists),
                    unresolved
                    && File.Exists(
                        Path.Combine(
                            repository.WorkingDirectory,
                            path)),
                    unresolved,
                    labels.Item1,
                    labels.Item2));
        }

        return new RepositoryOperationState(
            operation,
            conflicts,
            operation is RepositoryOperation.Merge
                or RepositoryOperation.Rebase
                or RepositoryOperation.CherryPick
                or RepositoryOperation.Revert,
            operation is RepositoryOperation.Merge
                or RepositoryOperation.Rebase
                or RepositoryOperation.CherryPick
                or RepositoryOperation.Revert,
            operation is RepositoryOperation.Rebase
                or RepositoryOperation.CherryPick
                or RepositoryOperation.Revert);
    }

    private async Task<bool> IsBinaryConflictAsync(
        Repository repository,
        string path,
        CancellationToken cancellationToken)
    {
        var output = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "diff",
            "--cached",
            "--numstat",
            "--",
            path);

        if (output.Split('\n').Any(
                line => line.StartsWith(
                    "-\t-\t",
                    StringComparison.Ordinal)))
            return true;

        foreach (var stage in new[] { 2, 3 })
        {
            var blob = await _runner.RunOptionalAsync(
                repository.WorkingDirectory,
                cancellationToken,
                "show",
                $":{stage}:{path}");
            if (blob.IndexOf('\0') >= 0) return true;
        }

        return false;
    }

    private static IReadOnlyList<WorkingTreeChange> ParseStatus(
        string output)
    {
        var entries = output.Split(
            '\0',
            StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<WorkingTreeChange>();

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (entry.Length < 4) continue;

            string? originalPath = null;
            if ((entry[0] is 'R' or 'C'
                 || entry[1] is 'R' or 'C')
                && index + 1 < entries.Length)
                originalPath = entries[++index];

            changes.Add(
                new WorkingTreeChange(
                    entry[3..],
                    entry[0],
                    entry[1],
                    originalPath));
        }

        return changes;
    }

    private static IReadOnlyDictionary<string, string> ParseConfiguration(
        string output)
    {
        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in output.Split(
                     '\0',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('\n');
            if (separator < 0)
                separator = entry.IndexOf('=');

            if (separator > 0)
                result[entry[..separator]] = entry[(separator + 1)..];
            else
                result[entry] = string.Empty;
        }

        return result;
    }

    private static Dictionary<string, HashSet<int>> ParseUnmergedStages(
        string output)
    {
        var result = new Dictionary<string, HashSet<int>>(
            StringComparer.Ordinal);

        foreach (var entry in output.Split(
                     '\0',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t');
            if (tab < 0) continue;

            var metadata = entry[..tab].Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length != 3
                || !int.TryParse(metadata[2], out var stage))
                continue;

            var path = entry[(tab + 1)..];
            if (!result.TryGetValue(path, out var values))
                result[path] = values = [];

            values.Add(stage);
        }

        return result;
    }

    private static void AddConflictPathsFromMessage(
        string messagePath,
        HashSet<string> paths)
    {
        if (!File.Exists(messagePath)) return;

        foreach (var line in File.ReadLines(messagePath))
        {
            if (!line.StartsWith("#\t", StringComparison.Ordinal))
                continue;

            var path = line[2..].TrimEnd();
            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(path);
        }
    }

    private static (int Ahead, int Behind) ParseTracking(string value)
    {
        static int ReadCount(string text, string marker)
        {
            var start = text.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return 0;

            start += marker.Length;
            var end = text.IndexOfAny([',', ']'], start);
            return int.TryParse(
                text[start..(end < 0 ? text.Length : end)].Trim(),
                out var count)
                ? count
                : 0;
        }

        return (
            ReadCount(value, "ahead "),
            ReadCount(value, "behind "));
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
