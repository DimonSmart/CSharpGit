using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed record GitRepositoryStateReadResult(
    RepositoryState State,
    string? EffectiveTagSort,
    string? LocalDefaultRemoteBranch,
    IReadOnlyList<string> RelevantConfiguration);

internal sealed class GitRepositoryStateService : IRepositoryStateService
{
    internal static readonly IReadOnlyDictionary<string, string?> ReadOnlyEnvironment =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GIT_OPTIONAL_LOCKS"] = "0"
        };

    private readonly GitRepositoryCommandRunner _runner;
    internal GitRepositoryCommandRunner Runner => _runner;

    internal GitRepositoryStateService(GitCommandExecutor executor)
        : this(new GitRepositoryCommandRunner(executor))
    {
    }

    internal GitRepositoryStateService(GitRepositoryCommandRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<RepositoryState> ReadLocalOnlyAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        (await ReadDetailedAsync(repository, cancellationToken)).State;

    public async Task<RepositoryState> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        (await ReadDetailedAsync(repository, cancellationToken)).State;

    internal async Task<GitRepositoryStateReadResult> ReadDetailedAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        await _runner.EnsureGitAvailableAsync(cancellationToken);

        var statusOutput = await RunReadAsync(
            repository,
            cancellationToken,
            "status",
            "--porcelain=v2",
            "-z",
            "--branch",
            "--untracked-files=all");
        var status = ParseStatusV2(statusOutput);

        var configurationOutput = await RunReadAsync(
            repository,
            cancellationToken,
            "config",
            "--null",
            "--list",
            "--show-scope");
        var configuration = ParseConfiguration(configurationOutput);

        var referenceOutput = await RunReadAsync(
            repository,
            cancellationToken,
            "for-each-ref",
            "--format=%(refname)%00%(objectname)%00%(upstream:short)%00%(upstream:track)%00%(symref:short)%1e",
            "refs/heads",
            "refs/remotes");
        var referenceRead = ParseReferences(referenceOutput, status.HeadReference);

        var remoteOutput = await RunReadAsync(
            repository,
            cancellationToken,
            "remote",
            "-v");
        var remotes = ParseRemotes(remoteOutput);
        var references = referenceRead.References with { Remotes = remotes };

        var stashOutput = await RunReadAsync(
            repository,
            cancellationToken,
            "stash",
            "list",
            "--format=%gd%x00%H%x00%gs%x1e");
        var stashes = ParseStashes(stashOutput);

        var operation = GitOperationDetector.Detect(repository);
        var operationState = await ReadOperationStateAsync(
            repository,
            operation,
            status.Changes,
            cancellationToken);

        var localDefaultRemoteBranch = ResolveLocalDefaultRemoteBranch(
            referenceRead.RemoteHeads,
            remotes,
            references.RemoteBranches);

        var state = new RepositoryState(
            repository,
            status.HeadReference,
            status.HeadCommit,
            status.IsDetached,
            operation,
            status.Changes,
            configuration.Global,
            configuration.Local,
            DateTimeOffset.UtcNow,
            references,
            stashes,
            operationState);

        return new GitRepositoryStateReadResult(
            state,
            configuration.EffectiveTagSort,
            localDefaultRemoteBranch,
            configuration.Relevant);
    }

    private async Task<RepositoryOperationState> ReadOperationStateAsync(
        Repository repository,
        RepositoryOperation operation,
        IReadOnlyList<WorkingTreeChange> changes,
        CancellationToken cancellationToken)
    {
        if (operation == RepositoryOperation.None)
            return RepositoryOperationState.None;

        var unmerged = await RunReadAsync(
            repository,
            cancellationToken,
            "ls-files",
            "--unmerged",
            "-z");
        var stages = ParseUnmergedStages(unmerged);
        var knownPaths = new HashSet<string>(stages.Keys, StringComparer.Ordinal);

        AddConflictPathsFromMessage(Path.Combine(repository.GitDirectory, "MERGE_MSG"), knownPaths);
        AddConflictPathsFromMessage(Path.Combine(repository.GitDirectory, "rebase-merge", "message"), knownPaths);
        AddConflictPathsFromMessage(Path.Combine(repository.GitDirectory, "rebase-apply", "final-commit"), knownPaths);

        var changesByPath = changes.ToDictionary(change => change.Path, StringComparer.Ordinal);
        var conflicts = new List<ConflictFile>();

        foreach (var path in knownPaths.Order(StringComparer.Ordinal))
        {
            stages.TryGetValue(path, out var presentStages);
            presentStages ??= [];
            changesByPath.TryGetValue(path, out var change);

            var unresolved = presentStages.Count > 0;
            var currentExists = presentStages.Contains(operation == RepositoryOperation.Rebase ? 3 : 2);
            var incomingExists = presentStages.Contains(operation == RepositoryOperation.Rebase ? 2 : 3);
            var isBinary = unresolved && await IsBinaryConflictAsync(repository, path, cancellationToken);
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
                ? ("Current/local (replayed commit)", "Incoming/remote (rebase base)")
                : ("Current/local", "Incoming/remote");

            conflicts.Add(new ConflictFile(
                path,
                kind,
                !unresolved,
                unresolved && !isBinary && File.Exists(Path.Combine(repository.WorkingDirectory, path)),
                unresolved && currentExists,
                unresolved && incomingExists,
                unresolved && (!currentExists || !incomingExists),
                unresolved && File.Exists(Path.Combine(repository.WorkingDirectory, path)),
                unresolved,
                labels.Item1,
                labels.Item2));
        }

        return new RepositoryOperationState(
            operation,
            conflicts,
            operation is RepositoryOperation.Merge or RepositoryOperation.Rebase or RepositoryOperation.CherryPick or RepositoryOperation.Revert,
            operation is RepositoryOperation.Merge or RepositoryOperation.Rebase or RepositoryOperation.CherryPick or RepositoryOperation.Revert,
            operation is RepositoryOperation.Rebase or RepositoryOperation.CherryPick or RepositoryOperation.Revert);
    }

    private async Task<bool> IsBinaryConflictAsync(
        Repository repository,
        string path,
        CancellationToken cancellationToken)
    {
        var output = await RunReadAsync(
            repository,
            cancellationToken,
            "diff",
            "--cached",
            "--numstat",
            "--",
            path);

        if (output.Split('\n').Any(line => line.StartsWith("-\t-\t", StringComparison.Ordinal)))
            return true;

        foreach (var stage in new[] { 2, 3 })
        {
            var blob = await RunOptionalReadAsync(
                repository,
                cancellationToken,
                "show",
                $":{stage}:{path}");
            if (blob.IndexOf('\0') >= 0) return true;
        }

        return false;
    }

    internal Task<RepositoryRefreshFingerprint> BuildRefreshFingerprintAsync(
        Repository repository,
        RepositoryState repositoryState,
        IReadOnlyList<string> relevantConfiguration,
        CancellationToken cancellationToken = default) =>
        RepositoryRefreshFingerprintBuilder.BuildAsync(
            _runner,
            repository,
            repositoryState,
            relevantConfiguration,
            cancellationToken);

    private Task<string> RunReadAsync(
        Repository repository,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            ReadOnlyEnvironment,
            arguments);

    private async Task<string> RunOptionalReadAsync(
        Repository repository,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var result = await _runner.RunForResultAsync(
            repository.WorkingDirectory,
            "RepositoryOptional",
            GitCommandKind.Internal,
            cancellationToken,
            ReadOnlyEnvironment,
            arguments);
        if (result.ExitCode == 0) return result.StandardOutput;
        if (arguments.Length > 0 && string.Equals(arguments[0], "show", StringComparison.Ordinal) && result.ExitCode == 128)
            return string.Empty;
        throw GitRepositoryCommandRunner.CreateCommandFailure(result);
    }

    private static StatusReadResult ParseStatusV2(string output)
    {
        string? headReference = null;
        string? headCommit = null;
        var detached = false;
        var changes = new List<WorkingTreeChange>();
        var records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            if (record.StartsWith("# branch.oid ", StringComparison.Ordinal))
            {
                var value = record[13..];
                headCommit = value is "(initial)" ? null : EmptyToNull(value);
                continue;
            }

            if (record.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                var value = record[14..];
                detached = string.Equals(value, "(detached)", StringComparison.Ordinal);
                headReference = detached ? null : EmptyToNull(value);
                continue;
            }

            if (record.StartsWith("1 ", StringComparison.Ordinal))
            {
                var fields = record.Split(' ', 9, StringSplitOptions.None);
                if (fields.Length == 9)
                    changes.Add(CreateChange(fields[8], fields[1], null));
                continue;
            }

            if (record.StartsWith("2 ", StringComparison.Ordinal))
            {
                var fields = record.Split(' ', 10, StringSplitOptions.None);
                var originalPath = index + 1 < records.Length ? records[++index] : null;
                if (fields.Length == 10)
                    changes.Add(CreateChange(fields[9], fields[1], originalPath));
                continue;
            }

            if (record.StartsWith("u ", StringComparison.Ordinal))
            {
                var fields = record.Split(' ', 11, StringSplitOptions.None);
                if (fields.Length == 11)
                    changes.Add(CreateChange(fields[10], fields[1], null));
                continue;
            }

            if (record.StartsWith("? ", StringComparison.Ordinal))
                changes.Add(new WorkingTreeChange(record[2..], '?', '?'));
        }

        return new StatusReadResult(headReference, headCommit, detached, changes);
    }

    private static WorkingTreeChange CreateChange(string path, string xy, string? originalPath)
    {
        var indexStatus = xy.Length > 0 ? NormalizeStatus(xy[0]) : ' ';
        var workingTreeStatus = xy.Length > 1 ? NormalizeStatus(xy[1]) : ' ';
        return new WorkingTreeChange(path, indexStatus, workingTreeStatus, originalPath);
    }

    private static char NormalizeStatus(char value) => value == '.' ? ' ' : value;

    private static ReferenceReadResult ParseReferences(string output, string? currentBranch)
    {
        var local = new List<GitBranch>();
        var remote = new List<GitBranch>();
        var remoteHeads = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimStart('\r', '\n').Split('\0');
            if (fields.Length < 5) continue;

            var fullName = fields[0];
            if (fullName.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                var name = fullName[11..];
                var (ahead, behind) = ParseTracking(fields[3]);
                local.Add(new GitBranch(
                    name,
                    fields[1],
                    string.Equals(name, currentBranch, StringComparison.Ordinal),
                    EmptyToNull(fields[2]),
                    ahead,
                    behind));
                continue;
            }

            if (!fullName.StartsWith("refs/remotes/", StringComparison.Ordinal))
                continue;

            var shortName = fullName[13..];
            if (shortName.EndsWith("/HEAD", StringComparison.Ordinal))
            {
                var remoteName = shortName[..^5];
                if (!string.IsNullOrWhiteSpace(fields[4]))
                    remoteHeads[remoteName] = fields[4];
                continue;
            }

            remote.Add(new GitBranch(shortName, fields[1]));
        }

        return new ReferenceReadResult(
            new GitReferences(local, remote, [], []),
            remoteHeads);
    }

    private static IReadOnlyList<GitRemote> ParseRemotes(string output)
    {
        var values = new Dictionary<string, (string? Fetch, string? Push)>(StringComparer.Ordinal);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;

            var name = line[..tab];
            var value = line[(tab + 1)..];
            var isFetch = value.EndsWith(" (fetch)", StringComparison.Ordinal);
            var isPush = value.EndsWith(" (push)", StringComparison.Ordinal);
            if (!isFetch && !isPush) continue;

            var url = value[..^8];
            values.TryGetValue(name, out var current);
            if (isFetch && current.Fetch is null) current.Fetch = url;
            if (isPush && current.Push is null) current.Push = url;
            values[name] = current;
        }

        return values
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var fetch = pair.Value.Fetch ?? pair.Value.Push ?? string.Empty;
                var push = pair.Value.Push ?? fetch;
                return new GitRemote(pair.Key, fetch, push);
            })
            .ToArray();
    }

    private static IReadOnlyList<GitStash> ParseStashes(string output)
    {
        var result = new List<GitStash>();
        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimStart('\r', '\n').Split('\0', 3);
            if (fields.Length == 3)
                result.Add(new GitStash(fields[0], fields[1], fields[2].TrimEnd('\r', '\n')));
        }

        return result;
    }

    private static ConfigurationReadResult ParseConfiguration(string output)
    {
        var global = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var local = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relevant = new List<string>();
        string? effectiveTagSort = null;
        var tokens = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index + 1 < tokens.Length; index += 2)
        {
            var scope = tokens[index];
            var entry = tokens[index + 1];
            var separator = entry.IndexOf('\n');
            if (separator < 0) separator = entry.IndexOf('=');

            var key = separator > 0 ? entry[..separator] : entry;
            var value = separator > 0 ? entry[(separator + 1)..] : string.Empty;

            if (string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase))
                global[key] = value;
            else if (string.Equals(scope, "local", StringComparison.OrdinalIgnoreCase))
                local[key] = value;

            if (string.Equals(key, "tag.sort", StringComparison.OrdinalIgnoreCase))
                effectiveTagSort = value;

            if (string.Equals(key, "tag.sort", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "versionsort.suffix", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "merge.tool", StringComparison.OrdinalIgnoreCase))
                relevant.Add(scope + "\0" + key.ToLowerInvariant() + "\0" + value);
        }

        return new ConfigurationReadResult(global, local, effectiveTagSort, relevant);
    }

    private static string? ResolveLocalDefaultRemoteBranch(
        IReadOnlyDictionary<string, string> remoteHeads,
        IReadOnlyList<GitRemote> remotes,
        IReadOnlyList<GitBranch> remoteBranches)
    {
        var remote = remotes.FirstOrDefault(candidate => string.Equals(candidate.Name, "origin", StringComparison.Ordinal))
                     ?? (remotes.Count == 1 ? remotes[0] : null);
        if (remote is null || !remoteHeads.TryGetValue(remote.Name, out var candidate))
            return null;

        if (!candidate.StartsWith(remote.Name + "/", StringComparison.Ordinal))
            return null;

        return remoteBranches.Any(branch => string.Equals(branch.Name, candidate, StringComparison.Ordinal))
            ? candidate
            : null;
    }

    private static Dictionary<string, HashSet<int>> ParseUnmergedStages(string output)
    {
        var result = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t');
            if (tab < 0) continue;

            var metadata = entry[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length != 3 || !int.TryParse(metadata[2], out var stage))
                continue;

            var path = entry[(tab + 1)..];
            if (!result.TryGetValue(path, out var values))
                result[path] = values = [];
            values.Add(stage);
        }

        return result;
    }

    private static void AddConflictPathsFromMessage(string messagePath, HashSet<string> paths)
    {
        if (!File.Exists(messagePath)) return;
        foreach (var line in File.ReadLines(messagePath))
        {
            if (!line.StartsWith("#\t", StringComparison.Ordinal)) continue;
            var path = line[2..].TrimEnd();
            if (!string.IsNullOrWhiteSpace(path)) paths.Add(path);
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
            return int.TryParse(text[start..(end < 0 ? text.Length : end)].Trim(), out var count)
                ? count
                : 0;
        }

        return (ReadCount(value, "ahead "), ReadCount(value, "behind "));
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record StatusReadResult(
        string? HeadReference,
        string? HeadCommit,
        bool IsDetached,
        IReadOnlyList<WorkingTreeChange> Changes);

    private sealed record ReferenceReadResult(
        GitReferences References,
        IReadOnlyDictionary<string, string> RemoteHeads);

    private sealed record ConfigurationReadResult(
        IReadOnlyDictionary<string, string> Global,
        IReadOnlyDictionary<string, string> Local,
        string? EffectiveTagSort,
        IReadOnlyList<string> Relevant);
}
