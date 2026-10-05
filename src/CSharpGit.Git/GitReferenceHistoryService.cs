using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed class GitReferenceHistoryService : IReferenceHistoryService
{
    private const int ReflogMetadataRecordLimit = 4096;
    private const int MaxCachedReflogSessions = 8;

    private readonly GitCommandExecutor _executor;
    private readonly object _reflogMetadataGate = new();
    private readonly Dictionary<ReflogMetadataCacheKey, IReadOnlyDictionary<string, ReflogPresentation>> _reflogMetadataCache = [];
    private readonly Queue<ReflogMetadataCacheKey> _reflogMetadataCacheOrder = new();
internal GitReferenceHistoryService(GitCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public Task<HistoryPage> ReadHistoryAsync(
        Repository repository,
        HistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Skip < 0 || query.Take is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(query));

        return ReadHistoryCoreAsync(
            repository,
            query.Filter,
            query.Skip,
            query.Take,
            GetHistoryRevisions(query),
            ShouldIncludeReflog(query),
            query.HeadExists,
            cancellationToken);
    }

    public Task<HistoryPage> ReadHistoryThroughCommitAsync(
        Repository repository,
        HistoryScope scope,
        string targetHash,
        int trailingCount = 100,
        CancellationToken cancellationToken = default) =>
        ReadHistoryThroughCommitAsync(
            repository,
            new HistoryQuery(scope, null, 0),
            targetHash,
            trailingCount,
            cancellationToken);

    public async Task<HistoryPage> ReadHistoryThroughCommitAsync(
        Repository repository,
        HistoryQuery query,
        string targetHash,
        int trailingCount = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(query);
        ValidateCommitHash(targetHash);
        if (trailingCount is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(trailingCount));

        var revisions = GetHistoryRevisions(query);
        var targetIndex = await FindCommitIndexAsync(repository, revisions, targetHash, cancellationToken);
        if (targetIndex < 0)
            throw new InvalidOperationException($"Commit {targetHash} is not reachable from the selected history scope.");

        var requestedCount = checked(targetIndex + 1 + trailingCount);
        var commits = await ReadHistoryPrefixAsync(repository, checked(requestedCount + 1), revisions, cancellationToken);
        var hasMore = commits.Count > requestedCount;
        var reflogOnlyHashes = ShouldIncludeReflog(query)
            ? await ReadReflogOnlyHashesAsync(repository, cancellationToken)
            : null;
        var topologyCommits = commits.Take(requestedCount).ToList();
        var reflogPresentations = reflogOnlyHashes is not null
                                  && HasReflogOnlyCommits(topologyCommits, reflogOnlyHashes)
            ? await ReadReflogPresentationsAsync(repository, query, cancellationToken)
            : null;
        var rows = BuildTopology(
            topologyCommits,
            reflogOnlyHashes,
            reflogPresentations,
            BuildReferenceDetails(query));
        return new HistoryPage(rows, hasMore);
    }

    public Task<HistoryPage> ReadHistoryAsync(
        Repository repository,
        string reference,
        string? filter,
        int skip,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateReference(reference);
        if (skip < 0 || take is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(take));
        return ReadHistoryCoreAsync(repository, filter, skip, take, [reference], false, null, cancellationToken);
    }

    public async Task<CommitDetails> ReadCommitAsync(
        Repository repository,
        string hash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateCommitHash(hash);
        var metadata = await RunGitAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "show", "-s", "--date=iso-strict", "--format=%H%x00%P%x00%an%x00%ae%x00%aI%x00%D%x00%B%x1e", hash);
        var commit = ParseHistory(metadata).Single();
        var stats = await RunGitAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "diff-tree", "--root", "-m", "--no-commit-id", "--numstat", "-r", "-z", hash);
        return new CommitDetails(commit, ParseChangedFiles(stats));
    }

    public async Task<FileDiff> ReadDiffAsync(
        Repository repository,
        string hash,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateCommitHash(hash);
        ValidateGitPath(path);
        var output = await RunGitAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "show", "--format=", "--no-ext-diff", "--find-renames", hash, "--", path);
        var binary = GitDiffParser.IsBinary(output);
        return new FileDiff(path, binary, binary ? [] : GitDiffParser.ParseLines(output));
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadFileStatusesAsync(
        Repository repository,
        string commitHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateCommitHash(commitHash);

        var output = await RunGitAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "diff-tree", "--root", "-m", "--no-commit-id", "--name-status", "-r", commitHash);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length < 2) continue;
            var status = fields[0];
            var path = fields[^1];
            if (status.Length > 0 && path.Length > 0) result[path] = status[0].ToString();
        }
        return result;
    }

    private async Task<HistoryPage> ReadHistoryCoreAsync(
        Repository repository,
        string? filter,
        int skip,
        int take,
        IReadOnlyList<string> revisions,
        bool includeReflog,
        bool? headExists,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (IsHeadOnly(revisions))
        {
            if (headExists == false)
                return new HistoryPage([], false);
            if (headExists is null && await IsUnbornHeadAsync(repository, cancellationToken))
                return new HistoryPage([], false);
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            var commits = await ReadHistoryPrefixAsync(repository, skip + take + 1, revisions, cancellationToken);
            var hasMore = commits.Count > skip + take;
            var pageReflogOnlyHashes = includeReflog
                ? await ReadReflogOnlyHashesAsync(repository, cancellationToken)
                : null;
            var topologyCommits = commits.Take(skip + take).ToList();
            var reflogPresentations = pageReflogOnlyHashes is not null
                                      && HasReflogOnlyCommits(topologyCommits, pageReflogOnlyHashes)
                ? await ReadReflogPresentationsAsync(repository, query, cancellationToken)
                : null;
            var rows = BuildTopology(
                topologyCommits,
                pageReflogOnlyHashes,
                reflogPresentations,
                BuildReferenceDetails(query));
            return new HistoryPage(rows.Skip(skip).ToList(), hasMore);
        }

        var matchArguments = new List<string>
        {
            "log",
            "--topo-order",
            $"--max-count={skip + take + 1}",
            "--format=%H",
            "--regexp-ignore-case",
            $"--grep={filter.Trim()}"
        };
        matchArguments.AddRange(revisions);

        var matchOutput = await RunGitAsync(repository.WorkingDirectory, cancellationToken, matchArguments.ToArray());
        var matchingHashes = matchOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var hasFilteredMore = matchingHashes.Count > skip + take;
        var requestedHashes = matchingHashes.Skip(skip).Take(take).ToList();
        if (requestedHashes.Count == 0)
            return new HistoryPage([], hasFilteredMore);

        var commitsThroughPage = await ReadHistoryThroughAsync(
            repository,
            revisions,
            requestedHashes[^1],
            cancellationToken);
        var reflogOnlyHashes = includeReflog
            ? await ReadReflogOnlyHashesAsync(repository, cancellationToken)
            : null;
        var reflogPresentations = reflogOnlyHashes is not null
                                  && HasReflogOnlyCommits(commitsThroughPage, reflogOnlyHashes)
            ? await ReadReflogPresentationsAsync(repository, query, cancellationToken)
            : null;
        var rowsByHash = BuildTopology(
                commitsThroughPage,
                reflogOnlyHashes,
                reflogPresentations,
                BuildReferenceDetails(query))
            .ToDictionary(row => row.Commit.Hash, StringComparer.Ordinal);
        var requestedRows = requestedHashes
            .Select(hash => rowsByHash.TryGetValue(hash, out var row)
                ? row
                : throw new InvalidOperationException($"Commit {hash} was not found in the unfiltered topology traversal."))
            .ToList();

        return new HistoryPage(requestedRows, hasFilteredMore);
    }

    private static bool IsHeadOnly(IReadOnlyList<string> revisions) =>
        revisions.Count == 1
        && string.Equals(revisions[0], "HEAD", StringComparison.Ordinal);

    private async Task<bool> IsUnbornHeadAsync(
        Repository repository,
        CancellationToken cancellationToken)
    {
        var head = await _executor.ExecuteForResultAsync(
            repository.WorkingDirectory,
            "ReferenceHistoryHeadProbe",
            GitCommandKind.Internal,
            cancellationToken,
            environment: null,
            new[] { "rev-parse", "--verify", "HEAD" });
        if (head.ExitCode == 0)
            return false;

        var symbolicHead = await _executor.ExecuteForResultAsync(
            repository.WorkingDirectory,
            "ReferenceHistoryHeadProbe",
            GitCommandKind.Internal,
            cancellationToken,
            environment: null,
            new[] { "symbolic-ref", "--quiet", "HEAD" });
        return symbolicHead.ExitCode == 0
               && !string.IsNullOrWhiteSpace(symbolicHead.StandardOutput);
    }

    private static bool ShouldIncludeReflog(HistoryQuery query) =>
        query.Scope == HistoryScope.AllReferences && query.IncludeReflog;

    private static IReadOnlyList<string> GetHistoryRevisions(HistoryQuery query)
    {
        if (query.Scope != HistoryScope.AllReferences) return ["HEAD"];
        return query.IncludeReflog ? ["--all", "--reflog"] : ["--all"];
    }

    private async Task<HashSet<string>> ReadReflogOnlyHashesAsync(
        Repository repository,
        CancellationToken cancellationToken)
    {
        var output = await RunGitAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-list", "--reflog", "--not", "--all");
        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, ReflogPresentation>> ReadReflogPresentationsAsync(
        Repository repository,
        HistoryQuery query,
        CancellationToken cancellationToken)
    {
        var cacheKey = new ReflogMetadataCacheKey(
            repository.WorkingDirectory,
            query.ReflogSessionId,
            query.HeadReference);

        if (query.ReflogSessionId != 0 && TryGetCachedReflogPresentations(cacheKey, out var cached))
            return cached;

        var output = await RunGitAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "reflog",
            "show",
            "--all",
            $"--max-count={ReflogMetadataRecordLimit}",
            "--format=%H%x00%gD%x00%gd%x00%gs%x1e");

        var parsed = ParseReflogPresentations(output, query.HeadReference);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.ReflogSessionId != 0)
            CacheReflogPresentations(cacheKey, parsed);

        return parsed;
    }

    private bool TryGetCachedReflogPresentations(
        ReflogMetadataCacheKey key,
        out IReadOnlyDictionary<string, ReflogPresentation> presentations)
    {
        lock (_reflogMetadataGate)
            return _reflogMetadataCache.TryGetValue(key, out presentations!);
    }

    private void CacheReflogPresentations(
        ReflogMetadataCacheKey key,
        IReadOnlyDictionary<string, ReflogPresentation> presentations)
    {
        lock (_reflogMetadataGate)
        {
            if (_reflogMetadataCache.ContainsKey(key))
            {
                _reflogMetadataCache[key] = presentations;
                return;
            }

            _reflogMetadataCache[key] = presentations;
            _reflogMetadataCacheOrder.Enqueue(key);
            while (_reflogMetadataCacheOrder.Count > MaxCachedReflogSessions)
            {
                var expired = _reflogMetadataCacheOrder.Dequeue();
                _reflogMetadataCache.Remove(expired);
            }
        }
    }

    private static IReadOnlyDictionary<string, ReflogPresentation> ParseReflogPresentations(
        string output,
        string? currentBranch)
    {
        var candidates = new Dictionary<string, ReflogCandidate>(StringComparer.Ordinal);
        var sequence = 0;

        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimStart('\r', '\n').Split('\0', 4);
            if (fields.Length != 4 || string.IsNullOrWhiteSpace(fields[0]))
                continue;

            var fullSelector = fields[1].Trim();
            var shortSelector = fields[2].Trim();
            var subject = fields[3].Trim();
            var candidate = new ReflogCandidate(
                new ReflogPresentation(
                    string.IsNullOrWhiteSpace(shortSelector) ? null : shortSelector,
                    CompactReflogEventKind(subject),
                    string.IsNullOrWhiteSpace(subject) ? null : subject),
                ReflogPriority(fullSelector, currentBranch),
                sequence++);

            if (!candidates.TryGetValue(fields[0], out var existing)
                || candidate.Priority < existing.Priority
                || candidate.Priority == existing.Priority && candidate.Sequence < existing.Sequence)
            {
                candidates[fields[0]] = candidate;
            }
        }

        return candidates.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Presentation,
            StringComparer.Ordinal);
    }

    private static int ReflogPriority(string fullSelector, string? currentBranch)
    {
        if (!string.IsNullOrWhiteSpace(currentBranch)
            && fullSelector.StartsWith($"refs/heads/{currentBranch}@{{", StringComparison.Ordinal))
            return 0;
        if (fullSelector.StartsWith("refs/heads/", StringComparison.Ordinal))
            return 1;
        if (fullSelector.StartsWith("HEAD@{", StringComparison.Ordinal))
            return 2;
        return 3;
    }

    private static string? CompactReflogEventKind(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var separator = subject.IndexOf(':');
        var value = (separator > 0 ? subject[..separator] : subject).Trim();
        if (value.Length <= 36)
            return value;
        return value[..33] + "...";
    }

    private static bool HasReflogOnlyCommits(
        IEnumerable<CommitHistoryItem> commits,
        IReadOnlySet<string> reflogOnlyHashes) =>
        commits.Any(commit => reflogOnlyHashes.Contains(commit.Hash));

    private static IReadOnlyDictionary<string, IReadOnlyList<HistoryReferenceDecoration>> BuildReferenceDetails(
        HistoryQuery query)
    {
        var result = new Dictionary<string, List<HistoryReferenceDecoration>>(StringComparer.Ordinal);

        static void Add(
            Dictionary<string, List<HistoryReferenceDecoration>> target,
            string commit,
            HistoryReferenceDecoration reference)
        {
            if (string.IsNullOrWhiteSpace(commit))
                return;
            if (!target.TryGetValue(commit, out var values))
            {
                values = [];
                target[commit] = values;
            }
            values.Add(reference);
        }

        if (query.RepositoryReferences is { } references)
        {
            foreach (var branch in references.LocalBranches)
            {
                var isCurrent = branch.IsCurrent
                    || (!query.IsDetachedHead
                        && string.Equals(branch.Name, query.HeadReference, StringComparison.Ordinal));
                Add(
                    result,
                    branch.Commit,
                    new HistoryReferenceDecoration(
                        branch.Name,
                        isCurrent ? HistoryReferenceKind.CurrentLocalBranch : HistoryReferenceKind.LocalBranch,
                        branch.IsDefault,
                        branch.Upstream,
                        branch.Ahead,
                        branch.Behind));
            }

            foreach (var branch in references.RemoteBranches)
            {
                Add(
                    result,
                    branch.Commit,
                    new HistoryReferenceDecoration(
                        branch.Name,
                        HistoryReferenceKind.RemoteTrackingBranch,
                        branch.IsDefault));
            }

            foreach (var tag in references.Tags)
            {
                Add(
                    result,
                    tag.TargetCommit,
                    new HistoryReferenceDecoration(
                        $"tag: {tag.Name}",
                        HistoryReferenceKind.Tag));
            }
        }

        if (query.IsDetachedHead && !string.IsNullOrWhiteSpace(query.HeadCommit))
        {
            Add(
                result,
                query.HeadCommit,
                new HistoryReferenceDecoration("HEAD", HistoryReferenceKind.DetachedHead));
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<HistoryReferenceDecoration>)pair.Value,
            StringComparer.Ordinal);
    }

    private async Task<int> FindCommitIndexAsync(
        Repository repository,
        IReadOnlyList<string> revisions,
        string targetHash,
        CancellationToken cancellationToken)
    {
        var maxCount = 256;
        while (true)
        {
            var arguments = new List<string>
            {
                "log",
                "--topo-order",
                $"--max-count={maxCount}",
                "--format=%H"
            };
            arguments.AddRange(revisions);

            var output = await RunGitAsync(repository.WorkingDirectory, cancellationToken, arguments.ToArray());
            var hashes = output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var targetIndex = hashes.FindIndex(hash => string.Equals(hash, targetHash, StringComparison.Ordinal));
            if (targetIndex >= 0) return targetIndex;
            if (hashes.Count < maxCount || maxCount == int.MaxValue) return -1;

            maxCount = (int)Math.Min((long)maxCount * 2, int.MaxValue);
        }
    }

    private async Task<List<CommitHistoryItem>> ReadHistoryThroughAsync(
        Repository repository,
        IReadOnlyList<string> revisions,
        string targetHash,
        CancellationToken cancellationToken)
    {
        var maxCount = 256;
        while (true)
        {
            var commits = await ReadHistoryPrefixAsync(repository, maxCount, revisions, cancellationToken);
            var targetIndex = commits.FindIndex(commit => string.Equals(commit.Hash, targetHash, StringComparison.Ordinal));
            if (targetIndex >= 0)
                return commits.Take(targetIndex + 1).ToList();

            if (commits.Count < maxCount)
                throw new InvalidOperationException($"Filtered commit {targetHash} was not found in the unfiltered history traversal.");

            if (maxCount == int.MaxValue)
                throw new InvalidOperationException($"History traversal is too large to resolve filtered commit {targetHash}.");

            maxCount = (int)Math.Min((long)maxCount * 2, int.MaxValue);
        }
    }

    private async Task<List<CommitHistoryItem>> ReadHistoryPrefixAsync(
        Repository repository,
        int maxCount,
        IReadOnlyList<string> revisions,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "log",
            "--topo-order",
            "--date=iso-strict",
            $"--max-count={maxCount}",
            "--format=%H%x00%P%x00%an%x00%ae%x00%aI%x00%D%x00%B%x1e"
        };
        arguments.AddRange(revisions);
        var output = await RunGitAsync(repository.WorkingDirectory, cancellationToken, arguments.ToArray());
        return ParseHistory(output).ToList();
    }

    private Task<string> RunGitAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments) =>
        _executor.ExecuteAsync(workingDirectory, "ReferenceHistory", cancellationToken, arguments);

    private static IEnumerable<CommitHistoryItem> ParseHistory(string output)
    {
        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimStart('\r', '\n').Split('\0', 7);
            if (fields.Length != 7 || !DateTimeOffset.TryParse(fields[4], out var authoredAt)) continue;
            var message = fields[6].TrimEnd('\r', '\n');
            yield return new CommitHistoryItem(
                fields[0],
                fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                FirstLine(message),
                message,
                fields[2],
                authoredAt,
                ParseRefs(fields[5]),
                fields[3]);
        }
    }

    private static IReadOnlyList<ChangedFile> ParseChangedFiles(string output)
    {
        var files = new List<ChangedFile>();
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = entry.TrimStart('\r', '\n').Split('\t', 3);
            if (fields.Length != 3) continue;
            var binary = fields[0] == "-" || fields[1] == "-";
            var file = new ChangedFile(
                fields[2],
                int.TryParse(fields[0], out var added) ? added : null,
                int.TryParse(fields[1], out var removed) ? removed : null,
                binary);
            if (!files.Contains(file)) files.Add(file);
        }
        return files;
    }

    internal static IReadOnlyList<HistoryRow> BuildTopology(
        IReadOnlyList<CommitHistoryItem> commits,
        IReadOnlySet<string>? reflogOnlyHashes = null,
        IReadOnlyDictionary<string, ReflogPresentation>? reflogPresentations = null,
        IReadOnlyDictionary<string, IReadOnlyList<HistoryReferenceDecoration>>? referenceDetailsByCommit = null)
    {
        var lanes = new List<LaneState>();
        var rows = new List<HistoryRow>(commits.Count);
        var nextTrackId = 0;

        foreach (var commit in commits)
        {
            var lane = lanes.FindIndex(state => state.Commit == commit.Hash);
            var laneAlreadyExisted = lane >= 0;
            if (!laneAlreadyExisted)
            {
                lane = lanes.Count;
                lanes.Add(new LaneState(commit.Hash, nextTrackId++));
            }

            var before = lanes.ToList();
            var nodeTrackId = before[lane].TrackId;
            var incoming = before
                .Select((state, index) => new TopologyEdge(index, index, state.TrackId))
                .Where(edge => laneAlreadyExisted || edge.FromLane != lane)
                .ToList();

            lanes.RemoveAt(lane);

            for (var parentIndex = 0; parentIndex < commit.Parents.Count; parentIndex++)
            {
                var parent = commit.Parents[parentIndex];
                if (lanes.Any(state => state.Commit == parent)) continue;

                var parentLane = Math.Min(lane + parentIndex, lanes.Count);
                var trackId = parentIndex == 0 ? nodeTrackId : nextTrackId++;
                lanes.Insert(parentLane, new LaneState(parent, trackId));
            }

            var outgoing = new List<TopologyEdge>();
            for (var beforeLane = 0; beforeLane < before.Count; beforeLane++)
            {
                if (beforeLane == lane) continue;
                var state = before[beforeLane];
                var afterLane = lanes.FindIndex(candidate => candidate.Commit == state.Commit);
                if (afterLane >= 0)
                    outgoing.Add(new TopologyEdge(beforeLane, afterLane, state.TrackId));
            }

            foreach (var parent in commit.Parents)
            {
                var parentLane = lanes.FindIndex(state => state.Commit == parent);
                if (parentLane >= 0)
                    outgoing.Add(new TopologyEdge(lane, parentLane, lanes[parentLane].TrackId));
            }

            var isReflogOnly = reflogOnlyHashes?.Contains(commit.Hash) == true;
            ReflogPresentation? reflogPresentation = null;
            if (isReflogOnly)
                reflogPresentations?.TryGetValue(commit.Hash, out reflogPresentation);

            var references = BuildRowReferenceDetails(commit, referenceDetailsByCommit);
            rows.Add(new HistoryRow(
                commit,
                new CommitTopology(lane, outgoing)
                {
                    NodeTrackId = nodeTrackId,
                    LaneCount = Math.Max(lane + 1, Math.Max(before.Count, lanes.Count)),
                    IncomingEdges = incoming,
                    HasExactGraphTopology = true
                },
                isReflogOnly,
                reflogPresentation,
                references));
        }
        return rows;
    }

    private static IReadOnlyList<HistoryReferenceDecoration> BuildRowReferenceDetails(
        CommitHistoryItem commit,
        IReadOnlyDictionary<string, IReadOnlyList<HistoryReferenceDecoration>>? referenceDetailsByCommit)
    {
        var result = new List<HistoryReferenceDecoration>();
        if (referenceDetailsByCommit is not null
            && referenceDetailsByCommit.TryGetValue(commit.Hash, out var semanticReferences))
        {
            result.AddRange(semanticReferences);
        }

        foreach (var reference in commit.References)
        {
            if (result.Any(item => string.Equals(item.DisplayName, reference, StringComparison.Ordinal)))
                continue;
            result.Add(new HistoryReferenceDecoration(reference, HistoryReferenceKind.Other));
        }

        return result;
    }

    private static IReadOnlyList<string> ParseRefs(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(reference => reference.StartsWith("HEAD -> ", StringComparison.Ordinal) ? reference[8..] : reference)
            .ToList();

    private static string FirstLine(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    private static void ValidateReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.StartsWith('-') || reference.Contains('\0') || reference.Any(char.IsWhiteSpace))
            throw new ArgumentException("Invalid Git reference.", nameof(reference));
    }

    private static void ValidateCommitHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Invalid commit hash.", nameof(hash));
    }

    private static void ValidateGitPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0') || Path.IsPathRooted(path))
            throw new ArgumentException("Invalid Git file path.", nameof(path));
    }

    private readonly record struct ReflogMetadataCacheKey(
        string WorkingDirectory,
        long SessionId,
        string? CurrentBranch);

    private sealed record ReflogCandidate(
        ReflogPresentation Presentation,
        int Priority,
        int Sequence);

    private sealed record LaneState(string Commit, int TrackId);
}
