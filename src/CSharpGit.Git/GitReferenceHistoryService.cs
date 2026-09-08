using System.Diagnostics;
using System.Text;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed class GitReferenceHistoryService : IReferenceHistoryService
{
    private readonly string _gitExecutable;

    public GitReferenceHistoryService() : this(new GitCliOptions()) { }

    public GitReferenceHistoryService(GitCliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _gitExecutable = string.IsNullOrWhiteSpace(options.ExecutablePath) ? "git" : options.ExecutablePath;
    }

    public async Task<HistoryPage> ReadHistoryAsync(
        Repository repository,
        string reference,
        string? filter,
        int skip,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateReference(reference);
        if (skip < 0 || take is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(take));

        var arguments = new List<string>
        {
            "log",
            "--topo-order",
            "--date=iso-strict",
            $"--max-count={skip + take + 1}",
            "--format=%H%x00%P%x00%an%x00%aI%x00%D%x00%B%x1e"
        };
        if (!string.IsNullOrWhiteSpace(filter))
        {
            arguments.Add("--regexp-ignore-case");
            arguments.Add($"--grep={filter.Trim()}");
        }
        arguments.Add(reference);

        var output = await RunGitAsync(repository.WorkingDirectory, cancellationToken, arguments.ToArray());
        var commits = ParseHistory(output).ToList();
        var hasMore = commits.Count > skip + take;
        var rows = BuildTopology(commits.Take(skip + take).ToList());
        return new HistoryPage(rows.Skip(skip).ToList(), hasMore);
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

    private static IReadOnlyList<HistoryRow> BuildTopology(IReadOnlyList<CommitHistoryItem> commits)
    {
        var lanes = new List<string>();
        var rows = new List<HistoryRow>(commits.Count);
        foreach (var commit in commits)
        {
            var lane = lanes.IndexOf(commit.Hash);
            if (lane < 0)
            {
                lane = lanes.Count;
                lanes.Add(commit.Hash);
            }

            var edges = new List<TopologyEdge>();
            lanes.RemoveAt(lane);
            for (var parentIndex = 0; parentIndex < commit.Parents.Count; parentIndex++)
            {
                var parent = commit.Parents[parentIndex];
                var parentLane = lanes.IndexOf(parent);
                if (parentLane < 0)
                {
                    parentLane = Math.Min(lane + parentIndex, lanes.Count);
                    lanes.Insert(parentLane, parent);
                }
                edges.Add(new TopologyEdge(lane, parentLane));
            }
            rows.Add(new HistoryRow(commit, new CommitTopology(lane, edges)));
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
}
