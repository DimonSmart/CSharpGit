using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed class GitRepositoryHistoryRewriteService : IRepositoryHistoryRewriteService
{
    private const string HeadsPrefix = "refs/heads/";
    private const string TagsPrefix = "refs/tags/";
    private const string RemotesPrefix = "refs/remotes/";

    private readonly GitCommandExecutor _executor;

    public GitRepositoryHistoryRewriteService() : this(GitCommandExecutor.Default)
    {
    }

    internal GitRepositoryHistoryRewriteService(GitCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public async Task<HistoryRewriteToolStatus> GetToolStatusAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var result = await RunForResultAsync(
            repository.WorkingDirectory,
            "HistoryRewriteToolCheck",
            GitCommandKind.Internal,
            cancellationToken,
            ["filter-repo", "--version"]);

        return result.ExitCode == 0
            ? new HistoryRewriteToolStatus(true, string.IsNullOrWhiteSpace(result.StandardOutput) ? null : result.StandardOutput.Trim())
            : new HistoryRewriteToolStatus(false, null);
    }

    public async Task<PathRemovalAnalysis> AnalyzePathRemovalAsync(
        Repository repository,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidatePath(path);
        await EnsureToolAvailableAsync(repository, cancellationToken);
        _ = await ReadSafeFingerprintAsync(repository, cancellationToken);

        var refs = await ReadRelevantRefsAsync(repository.WorkingDirectory, cancellationToken);
        var pathHistoryCommitCount = await CountPathHistoryCommitsAsync(repository, refs.Keys, path, cancellationToken);
        if (pathHistoryCommitCount == 0)
        {
            return new PathRemovalAnalysis(path, 0, [], [], []);
        }

        var affected = new List<string>();
        foreach (var reference in refs.Keys.Order(StringComparer.Ordinal))
        {
            if (await ReferenceContainsPathAsync(repository, reference, path, cancellationToken))
                affected.Add(reference);
        }

        return new PathRemovalAnalysis(
            path,
            pathHistoryCommitCount,
            affected.Where(reference => reference.StartsWith(HeadsPrefix, StringComparison.Ordinal))
                .Select(reference => reference[HeadsPrefix.Length..]).ToArray(),
            affected.Where(reference => reference.StartsWith(TagsPrefix, StringComparison.Ordinal))
                .Select(reference => reference[TagsPrefix.Length..]).ToArray(),
            affected.Where(reference => reference.StartsWith(RemotesPrefix, StringComparison.Ordinal))
                .Select(reference => reference[RemotesPrefix.Length..]).ToArray());
    }

    public async Task<PathRemovalResult> RemovePathFromHistoryAsync(
        Repository repository,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidatePath(path);
        await EnsureToolAvailableAsync(repository, cancellationToken);

        var sourceFingerprint = await ReadSafeFingerprintAsync(repository, cancellationToken);
        var topology = await ReadTopologySnapshotAsync(repository, cancellationToken);
        var relevantRefs = topology.RefNames;
        var historyCount = await CountPathHistoryCommitsAsync(repository, relevantRefs, path, cancellationToken);
        if (historyCount == 0)
        {
            throw Failure(
                HistoryRewriteFailureKind.PathVerificationFailed,
                "The file is no longer present in repository history.");
        }

        var backupPath = await CreateAndVerifyBackupAsync(repository, sourceFingerprint, cancellationToken);

        RepositoryFingerprint afterBackup;
        try
        {
            afterBackup = await ReadSafeFingerprintAsync(repository, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Failure(
                HistoryRewriteFailureKind.RepositoryChangedDuringBackup,
                "The repository changed while the safety backup was being created. History rewrite was not started.",
                backupPath,
                false,
                exception);
        }

        if (!FingerprintsEqual(sourceFingerprint, afterBackup))
        {
            throw Failure(
                HistoryRewriteFailureKind.RepositoryChangedDuringBackup,
                "The repository changed while the safety backup was being created. History rewrite was not started.",
                backupPath);
        }

        var topologyAfterBackup = await ReadTopologySnapshotAsync(repository, cancellationToken);
        if (!TopologyEqual(topology, topologyAfterBackup))
        {
            throw Failure(
                HistoryRewriteFailureKind.RepositoryChangedDuringBackup,
                "The repository configuration changed while the safety backup was being created. History rewrite was not started.",
                backupPath);
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _executor.ExecuteAsync(
                repository.WorkingDirectory,
                "HistoryRewriteFilterRepo",
                GitCommandKind.User,
                CancellationToken.None,
                "filter-repo",
                "--force",
                "--partial",
                "--prune-empty", "never",
                "--invert-paths",
                "--path", path);
        }
        catch (Exception exception)
        {
            throw Failure(
                HistoryRewriteFailureKind.RewriteFailed,
                "git-filter-repo did not complete successfully. The repository may have been partially rewritten.",
                backupPath,
                true,
                exception);
        }

        await VerifyTopologyAsync(repository, topology, backupPath, destructivePhaseStarted: true);
        await VerifyPathAbsentFromAllRefsAsync(repository, path, backupPath, destructivePhaseStarted: true);

        try
        {
            await _executor.ExecuteAsync(
                repository.WorkingDirectory,
                "HistoryRewriteExpireReflogs",
                GitCommandKind.User,
                CancellationToken.None,
                "reflog", "expire", "--expire=now", "--all");
            await _executor.ExecuteAsync(
                repository.WorkingDirectory,
                "HistoryRewriteGarbageCollect",
                GitCommandKind.User,
                CancellationToken.None,
                "gc", "--prune=now");
        }
        catch (Exception exception)
        {
            throw Failure(
                HistoryRewriteFailureKind.CleanupFailed,
                "Old repository history could not be cleaned up completely.",
                backupPath,
                true,
                exception);
        }

        try
        {
            await VerifyTopologyAsync(repository, topology, backupPath, destructivePhaseStarted: true);
            await VerifyPathAbsentFromAllRefsAsync(repository, path, backupPath, destructivePhaseStarted: true);

            var finalStatus = await _executor.ExecuteAsync(
                repository.WorkingDirectory,
                "HistoryRewriteFinalStatus",
                CancellationToken.None,
                "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none");
            if (!string.IsNullOrEmpty(finalStatus))
                throw new InvalidOperationException("The working tree is not clean after history rewrite.");

            var headObjectId = await _executor.ExecuteAsync(
                repository.WorkingDirectory,
                "HistoryRewriteFinalHead",
                CancellationToken.None,
                "rev-parse", "--verify", "HEAD");
            var rewrittenCommitCount = ReadRewrittenCommitCount(repository);
            return new PathRemovalResult(path, backupPath, rewrittenCommitCount, headObjectId.Trim());
        }
        catch (RepositoryHistoryRewriteException exception)
            when (exception.Kind is HistoryRewriteFailureKind.RefTopologyChanged or HistoryRewriteFailureKind.PathVerificationFailed)
        {
            throw Failure(
                HistoryRewriteFailureKind.FinalVerificationFailed,
                "Final repository verification failed after old history was cleaned up.",
                backupPath,
                true,
                exception);
        }
        catch (Exception exception)
        {
            throw Failure(
                HistoryRewriteFailureKind.FinalVerificationFailed,
                "Final repository verification failed after old history was cleaned up.",
                backupPath,
                true,
                exception);
        }
    }

    internal static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path[0] == '/'
            || path.IndexOf('\0') >= 0
            || (path.Length >= 3
                && char.IsLetter(path[0])
                && path[1] == ':'
                && path[2] is '/' or '\\'))
        {
            throw Failure(
                HistoryRewriteFailureKind.InvalidPath,
                "The selected path is not a valid repository-relative Git path.");
        }

        var segments = path.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw Failure(
                HistoryRewriteFailureKind.InvalidPath,
                "The selected path is not a valid repository-relative Git path.");
        }
    }

    private async Task EnsureToolAvailableAsync(Repository repository, CancellationToken cancellationToken)
    {
        var status = await GetToolStatusAsync(repository, cancellationToken);
        if (!status.IsAvailable)
        {
            throw Failure(
                HistoryRewriteFailureKind.ToolUnavailable,
                "git-filter-repo is required for this operation.");
        }
    }

    private async Task<RepositoryFingerprint> ReadSafeFingerprintAsync(
        Repository repository,
        CancellationToken cancellationToken)
    {
        var bare = await _executor.ExecuteAsync(
            repository.WorkingDirectory,
            "HistoryRewriteBareCheck",
            cancellationToken,
            "rev-parse", "--is-bare-repository");
        if (string.Equals(bare.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                HistoryRewriteFailureKind.UnsupportedRepository,
                "History rewrite is not supported for bare repositories.");
        }

        if (repository.IsWorktree)
        {
            throw Failure(
                HistoryRewriteFailureKind.UnsupportedRepository,
                "History rewrite must be started from the primary worktree.");
        }

        var shallow = await _executor.ExecuteAsync(
            repository.WorkingDirectory,
            "HistoryRewriteShallowCheck",
            cancellationToken,
            "rev-parse", "--is-shallow-repository");
        if (string.Equals(shallow.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            || await IsPartialCloneAsync(repository, cancellationToken))
        {
            throw Failure(
                HistoryRewriteFailureKind.ShallowOrPartialRepository,
                "History rewrite is not supported for shallow or partial/promisor clones.");
        }

        var operation = DetectOperation(repository);
        if (operation != RepositoryOperation.None)
        {
            throw Failure(
                HistoryRewriteFailureKind.UnsafeRepositoryState,
                $"History cannot be rewritten while Git operation '{operation}' is in progress.");
        }

        var headReferenceResult = await RunForResultAsync(
            repository.WorkingDirectory,
            "HistoryRewriteHeadReference",
            GitCommandKind.Internal,
            cancellationToken,
            ["symbolic-ref", "--quiet", "HEAD"]);
        if (headReferenceResult.ExitCode != 0 || string.IsNullOrWhiteSpace(headReferenceResult.StandardOutput))
        {
            throw Failure(
                HistoryRewriteFailureKind.UnsafeRepositoryState,
                "History cannot be rewritten while HEAD is detached.");
        }

        var status = await _executor.ExecuteAsync(
            repository.WorkingDirectory,
            "HistoryRewriteStatus",
            cancellationToken,
            "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none");
        if (!string.IsNullOrEmpty(status))
            throw Failure(HistoryRewriteFailureKind.UnsafeRepositoryState, DescribeDirtyStatus(status));

        var stashResult = await RunForResultAsync(
            repository.WorkingDirectory,
            "HistoryRewriteStashCheck",
            GitCommandKind.Internal,
            cancellationToken,
            ["rev-parse", "--verify", "--quiet", "refs/stash"]);
        if (stashResult.ExitCode == 0)
        {
            throw Failure(
                HistoryRewriteFailureKind.UnsafeRepositoryState,
                "History cannot be rewritten while the repository contains stashes. Apply, drop, or otherwise preserve the stashes first.");
        }

        var worktrees = await _executor.ExecuteAsync(
            repository.WorkingDirectory,
            "HistoryRewriteWorktreeCheck",
            cancellationToken,
            "worktree", "list", "--porcelain");
        var worktreeCount = worktrees.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.StartsWith("worktree ", StringComparison.Ordinal));
        if (worktreeCount != 1)
        {
            throw Failure(
                HistoryRewriteFailureKind.UnsafeRepositoryState,
                "History cannot be rewritten while additional linked worktrees exist.");
        }

        var replaceRefs = await _executor.ExecuteAsync(
            repository.WorkingDirectory,
            "HistoryRewriteReplaceRefsCheck",
            cancellationToken,
            "for-each-ref", "--format=%(refname)", "refs/replace/");
        if (!string.IsNullOrWhiteSpace(replaceRefs))
        {
            throw Failure(
                HistoryRewriteFailureKind.UnsafeRepositoryState,
                "History cannot be rewritten while refs/replace entries exist.");
        }

        var headObjectId = await _executor.ExecuteAsync(
            repository.WorkingDirectory,
            "HistoryRewriteHeadObject",
            cancellationToken,
            "rev-parse", "--verify", "HEAD");
        var refs = await ReadAllRefsAsync(repository.WorkingDirectory, cancellationToken);

        return new RepositoryFingerprint(
            headReferenceResult.StandardOutput.Trim(),
            headObjectId.Trim(),
            refs,
            operation,
            status);
    }

    private async Task<bool> IsPartialCloneAsync(Repository repository, CancellationToken cancellationToken)
    {
        var partialClone = await RunForResultAsync(
            repository.WorkingDirectory,
            "HistoryRewritePartialCloneCheck",
            GitCommandKind.Internal,
            cancellationToken,
            ["config", "--local", "--get", "extensions.partialClone"]);
        if (partialClone.ExitCode == 0 && !string.IsNullOrWhiteSpace(partialClone.StandardOutput))
            return true;

        var promisor = await RunForResultAsync(
            repository.WorkingDirectory,
            "HistoryRewritePromisorCheck",
            GitCommandKind.Internal,
            cancellationToken,
            ["config", "--local", "--get-regexp", "^remote\\..*\\.promisor$"]);
        return promisor.ExitCode == 0 && !string.IsNullOrWhiteSpace(promisor.StandardOutput);
    }

    private async Task<string> CreateAndVerifyBackupAsync(
        Repository repository,
        RepositoryFingerprint fingerprint,
        CancellationToken cancellationToken)
    {
        var backupPath = CreateBackupPath(repository);
        try
        {
            var backupDirectory = Path.GetDirectoryName(backupPath)!;
            Directory.CreateDirectory(backupDirectory);
            await _executor.ExecuteAsync(
                backupDirectory,
                "HistoryRewriteCreateBackup",
                GitCommandKind.User,
                cancellationToken,
                "clone", "--mirror", "--no-local", repository.RepositoryRoot, backupPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Failure(
                HistoryRewriteFailureKind.BackupCreationFailed,
                "The safety backup could not be created. History rewrite was not started.",
                null,
                false,
                exception);
        }

        try
        {
            var backupRefs = await ReadAllRefsAsync(backupPath, cancellationToken);
            foreach (var expected in fingerprint.Refs)
            {
                if (!backupRefs.TryGetValue(expected.Key, out var actual)
                    || !string.Equals(expected.Value.ObjectId, actual.ObjectId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Backup ref verification failed for {expected.Key}.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Failure(
                HistoryRewriteFailureKind.BackupVerificationFailed,
                "The safety backup could not be verified. History rewrite was not started.",
                backupPath,
                false,
                exception);
        }

        return backupPath;
    }

    private static string CreateBackupPath(Repository repository)
    {
        var candidates = new List<string>();

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localApplicationData))
            candidates.Add(Path.Combine(localApplicationData, "CSharpGit", "HistoryBackups"));

        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(applicationData))
            candidates.Add(Path.Combine(applicationData, "CSharpGit", "HistoryBackups"));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            candidates.Add(Path.Combine(userProfile, ".local", "share", "CSharpGit", "HistoryBackups"));

        var repositoryParent = Directory.GetParent(Path.TrimEndingDirectorySeparator(repository.RepositoryRoot))?.FullName;
        if (!string.IsNullOrWhiteSpace(repositoryParent))
            candidates.Add(Path.Combine(repositoryParent, ".CSharpGit-HistoryBackups"));

        var backupRoot = candidates
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .FirstOrDefault(candidate =>
                !IsPathWithin(candidate, repository.RepositoryRoot)
                && !IsPathWithin(candidate, repository.GitDirectory));

        if (backupRoot is null)
        {
            throw Failure(
                HistoryRewriteFailureKind.BackupCreationFailed,
                "No persistent safety-backup location is available outside the repository. History rewrite was not started.");
        }

        var rawName = new DirectoryInfo(repository.RepositoryRoot).Name;
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safeName = new string(rawName.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Repository";

        return Path.Combine(
            backupRoot,
            $"{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.git");
    }

    private static bool IsPathWithin(string path, string directory)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(fullPath, fullDirectory, comparison)) return true;

        return fullPath.StartsWith(
            fullDirectory + Path.DirectorySeparatorChar,
            comparison);
    }

    private async Task<TopologySnapshot> ReadTopologySnapshotAsync(
        Repository repository,
        CancellationToken cancellationToken)
    {
        var refs = await ReadRelevantRefsAsync(repository.WorkingDirectory, cancellationToken);
        var symbolicRefs = refs.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.SymbolicTarget,
            StringComparer.Ordinal);
        var configuration = await ReadRemoteAndBranchConfigurationAsync(repository.WorkingDirectory, cancellationToken);
        return new TopologySnapshot(symbolicRefs, configuration);
    }

    private async Task VerifyTopologyAsync(
        Repository repository,
        TopologySnapshot expected,
        string backupPath,
        bool destructivePhaseStarted)
    {
        var actual = await ReadTopologySnapshotAsync(repository, CancellationToken.None);
        if (!DictionaryEqual(expected.SymbolicRefs, actual.SymbolicRefs)
            || !expected.Configuration.SequenceEqual(actual.Configuration, StringComparer.Ordinal))
        {
            throw Failure(
                HistoryRewriteFailureKind.RefTopologyChanged,
                "Repository references or remote configuration changed unexpectedly during history rewrite.",
                backupPath,
                destructivePhaseStarted);
        }
    }

    private async Task VerifyPathAbsentFromAllRefsAsync(
        Repository repository,
        string path,
        string backupPath,
        bool destructivePhaseStarted)
    {
        var refs = await ReadAllRefsAsync(repository.WorkingDirectory, CancellationToken.None);
        await VerifyPathAbsentAsync(
            repository,
            path,
            refs.Keys.Order(StringComparer.Ordinal).ToArray(),
            backupPath,
            destructivePhaseStarted);
    }

    private async Task VerifyPathAbsentAsync(
        Repository repository,
        string path,
        IReadOnlyList<string> references,
        string backupPath,
        bool destructivePhaseStarted)
    {
        foreach (var reference in references)
        {
            if (await ReferenceContainsPathAsync(repository, reference, path, CancellationToken.None))
            {
                throw Failure(
                    HistoryRewriteFailureKind.PathVerificationFailed,
                    $"The path '{path}' is still reachable from {reference} after history rewrite.",
                    backupPath,
                    destructivePhaseStarted);
            }
        }
    }

    private async Task<int> CountPathHistoryCommitsAsync(
        Repository repository,
        IEnumerable<string> references,
        string path,
        CancellationToken cancellationToken)
    {
        var refs = references.Distinct(StringComparer.Ordinal).ToArray();
        if (refs.Length == 0) return 0;

        var arguments = new List<string> { "rev-list" };
        arguments.AddRange(refs);
        arguments.Add("--");
        arguments.Add(path);

        var output = await _executor.ExecuteAsync(
            repository.WorkingDirectory,
            "HistoryRewriteAnalyzePath",
            GitCommandKind.Internal,
            cancellationToken,
            arguments.ToArray());

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private async Task<bool> ReferenceContainsPathAsync(
        Repository repository,
        string reference,
        string path,
        CancellationToken cancellationToken)
    {
        var output = await _executor.ExecuteAsync(
            repository.WorkingDirectory,
            "HistoryRewriteCheckReference",
            GitCommandKind.Internal,
            cancellationToken,
            "rev-list", "-1", reference, "--", path);
        return !string.IsNullOrWhiteSpace(output);
    }

    private async Task<Dictionary<string, RefState>> ReadAllRefsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var output = await _executor.ExecuteAsync(
            workingDirectory,
            "HistoryRewriteReadRefs",
            cancellationToken,
            "for-each-ref", "--format=%(refname)%09%(objectname)%09%(symref)");

        var result = new Dictionary<string, RefState>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.TrimEnd('\r').Split('\t', 3);
            if (fields.Length < 2 || string.IsNullOrWhiteSpace(fields[0])) continue;
            result[fields[0]] = new RefState(
                fields[1],
                fields.Length == 3 && !string.IsNullOrWhiteSpace(fields[2]) ? fields[2] : null);
        }

        return result;
    }

    private async Task<Dictionary<string, RefState>> ReadRelevantRefsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var all = await ReadAllRefsAsync(workingDirectory, cancellationToken);
        return all
            .Where(pair => IsRelevantRef(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private async Task<string[]> ReadRemoteAndBranchConfigurationAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var output = await _executor.ExecuteAsync(
            workingDirectory,
            "HistoryRewriteReadRemoteConfiguration",
            cancellationToken,
            "config", "--local", "--null", "--list");

        var entries = new List<string>();
        foreach (var rawEntry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = rawEntry.IndexOf('\n');
            if (separator < 0) separator = rawEntry.IndexOf('=');
            var key = separator < 0 ? rawEntry : rawEntry[..separator];
            if (!key.StartsWith("remote.", StringComparison.OrdinalIgnoreCase)
                && !key.StartsWith("branch.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = separator < 0 ? string.Empty : rawEntry[(separator + 1)..];
            entries.Add($"{key.ToLowerInvariant()}\n{value}");
        }

        entries.Sort(StringComparer.Ordinal);
        return entries.ToArray();
    }

    private async Task<GitCommandResult> RunForResultAsync(
        string workingDirectory,
        string operation,
        GitCommandKind commandKind,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments)
    {
        return await _executor.ExecuteForResultAsync(
            workingDirectory,
            operation,
            commandKind,
            cancellationToken,
            null,
            arguments);
    }

    private static string DescribeDirtyStatus(string status)
    {
        var entries = status.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (entries.Any(entry => entry.StartsWith("??", StringComparison.Ordinal)))
            return "History cannot be rewritten while the repository contains untracked files. Move, add, or remove them first.";

        if (entries.Any(entry => entry.Length >= 2 && entry[0] is not (' ' or '?')))
            return "History cannot be rewritten while the index contains staged changes. Commit or unstage the changes first.";

        return "History cannot be rewritten while the repository has uncommitted changes. Commit or discard the changes first.";
    }

    private static RepositoryOperation DetectOperation(Repository repository)
    {
        if (Directory.Exists(Path.Combine(repository.GitDirectory, "rebase-merge"))
            || Directory.Exists(Path.Combine(repository.GitDirectory, "rebase-apply")))
            return RepositoryOperation.Rebase;
        if (File.Exists(Path.Combine(repository.GitDirectory, "MERGE_HEAD")))
            return RepositoryOperation.Merge;
        if (File.Exists(Path.Combine(repository.GitDirectory, "CHERRY_PICK_HEAD")))
            return RepositoryOperation.CherryPick;
        if (File.Exists(Path.Combine(repository.GitDirectory, "REVERT_HEAD")))
            return RepositoryOperation.Revert;
        if (File.Exists(Path.Combine(repository.GitDirectory, "BISECT_LOG")))
            return RepositoryOperation.Bisect;
        return RepositoryOperation.None;
    }

    private static int ReadRewrittenCommitCount(Repository repository)
    {
        var mapPath = Path.Combine(repository.GitDirectory, "filter-repo", "commit-map");
        if (!File.Exists(mapPath))
            throw new InvalidOperationException("git-filter-repo commit-map was not found.");

        var count = 0;
        foreach (var line in File.ReadLines(mapPath))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2
                || !LooksLikeObjectId(fields[0])
                || !LooksLikeObjectId(fields[1]))
            {
                continue;
            }

            if (!string.Equals(fields[0], fields[1], StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    private static bool LooksLikeObjectId(string value) =>
        value.Length >= 40 && value.All(Uri.IsHexDigit);

    private static bool FingerprintsEqual(RepositoryFingerprint left, RepositoryFingerprint right) =>
        string.Equals(left.HeadSymbolicRef, right.HeadSymbolicRef, StringComparison.Ordinal)
        && string.Equals(left.HeadObjectId, right.HeadObjectId, StringComparison.Ordinal)
        && left.Operation == right.Operation
        && string.Equals(left.Status, right.Status, StringComparison.Ordinal)
        && RefMapsEqual(left.Refs, right.Refs);

    private static bool RefMapsEqual(
        IReadOnlyDictionary<string, RefState> left,
        IReadOnlyDictionary<string, RefState> right)
    {
        if (left.Count != right.Count) return false;
        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var other) || pair.Value != other)
                return false;
        }

        return true;
    }

    private static bool TopologyEqual(TopologySnapshot left, TopologySnapshot right) =>
        DictionaryEqual(left.SymbolicRefs, right.SymbolicRefs)
        && left.Configuration.SequenceEqual(right.Configuration, StringComparer.Ordinal);

    private static bool DictionaryEqual(
        IReadOnlyDictionary<string, string?> left,
        IReadOnlyDictionary<string, string?> right)
    {
        if (left.Count != right.Count) return false;
        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var other)
                || !string.Equals(pair.Value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRelevantRef(string reference) =>
        reference.StartsWith(HeadsPrefix, StringComparison.Ordinal)
        || reference.StartsWith(TagsPrefix, StringComparison.Ordinal)
        || reference.StartsWith(RemotesPrefix, StringComparison.Ordinal);

    private static RepositoryHistoryRewriteException Failure(
        HistoryRewriteFailureKind kind,
        string message,
        string? backupPath = null,
        bool destructivePhaseStarted = false,
        Exception? innerException = null) =>
        new(kind, message, backupPath, destructivePhaseStarted, innerException);

    private sealed record RefState(string ObjectId, string? SymbolicTarget);

    private sealed record RepositoryFingerprint(
        string HeadSymbolicRef,
        string HeadObjectId,
        IReadOnlyDictionary<string, RefState> Refs,
        RepositoryOperation Operation,
        string Status);

    private sealed record TopologySnapshot(
        IReadOnlyDictionary<string, string?> SymbolicRefs,
        string[] Configuration)
    {
        public IReadOnlyList<string> RefNames => SymbolicRefs.Keys.Order(StringComparer.Ordinal).ToArray();
    }
}
