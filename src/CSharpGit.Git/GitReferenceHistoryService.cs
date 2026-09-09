using System.Diagnostics;
using System.Text;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed class GitReferenceHistoryService : IReferenceHistoryService, IHistoryService
{
    private readonly string _gitExecutable;
    private readonly GitCliRepositoryService _detailsService;

    public GitReferenceHistoryService() : this(new GitCliOptions()) { }

    public GitReferenceHistoryService(GitCliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _gitExecutable = string.IsNullOrWhiteSpace(options.ExecutablePath) ? "git" : options.ExecutablePath;
        _detailsService = new GitCliRepositoryService(options);
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
            query.Scope == HistoryScope.AllReferences ? ["--all"] : ["HEAD"],
            cancellationToken);
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
        return ReadHistoryCoreAsync(repository, filter, skip, take, [reference], cancellationToken);
    }

    public Task<CommitDetails> ReadCommitAsync(
        Repository repository,
        string hash,
        CancellationToken cancellationToken = default) =>
        _detailsService.ReadCommitAsync(repository, hash, cancellationToken);

    public Task<FileDiff> ReadDiffAsync(
        Repository repository,
        string hash,
        string path,
        CancellationToken cancellationToken = default) =>
        _detailsService.ReadDiffAsync(repository, hash, path, cancellationToken);

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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (string.IsNullOrWhiteSpace(filter))
        {
            var commits = await ReadHistoryPrefixAsync(repository, skip + take + 1, revisions, cancellationToken);
            var hasMore = commits.Count > skip + take;
            var rows = BuildTopology(commits.Take(skip + take).ToList());
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
        var rowsByHash = BuildTopology(commitsThroughPage)
            .ToDictionary(row => row.Commit.Hash, StringComparer.Ordinal);
        var requestedRows = requestedHashes
            .Select(hash => rowsByHash.TryGetValue(hash, out var row)
                ? row
                : throw new InvalidOperationException($"Commit {hash} was not found in the unfiltered topology traversal."))
            .ToList();

        return new HistoryPage(requestedRows, hasFilteredMore);
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
            "--format=%H%x00%P%x00%an%x00%aI%x00%D%x00%B%x1e"
        };
        arguments.AddRange(revisions);
        var output = await RunGitAsync(repository.WorkingDirectory, cancellationToken, arguments.ToArray());
        return ParseHistory(output).ToList();
    }

    private async Task<string> RunGitAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
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
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await stdout).TrimEnd('\r', '\n');
        var error = (await stderr).Trim();
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(error) ? "Git returned no diagnostic message." : error;
            throw new InvalidOperationException($"Git command exited with code {process.ExitCode}: {detail}");
        }
        return output;
    }

    private static IEnumerable<CommitHistoryItem> ParseHistory(string output)
    {
        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimStart('\r', '\n').Split('\0', 6);
            if (fields.Length != 6 || !DateTimeOffset.TryParse(fields[3], out var authoredAt)) continue;
            var message = fields[5].TrimEnd('\r', '\n');
            yield return new CommitHistoryItem(
                fields[0],
                fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                FirstLine(message),
                message,
                fields[2],
                authoredAt,
                ParseRefs(fields[4]));
        }
    }

    internal static IReadOnlyList<HistoryRow> BuildTopology(IReadOnlyList<CommitHistoryItem> commits)
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

            // Parent insertion can shift lanes that already exist. Build the complete
            // lower-boundary state first, then resolve numeric lane indexes by identity.
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

            rows.Add(new HistoryRow(
                commit,
                new CommitTopology(lane, outgoing)
                {
                    NodeTrackId = nodeTrackId,
                    IncomingEdges = incoming,
                    HasExactGraphTopology = true
                }));
        }
        return rows;
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

    private sealed record LaneState(string Commit, int TrackId);
}
