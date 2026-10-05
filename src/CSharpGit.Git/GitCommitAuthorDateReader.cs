using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitCommitAuthorDateReader : ICommitAuthorDateReader
{
    private readonly GitRepositoryCommandRunner _runner;

    internal GitCommitAuthorDateReader(GitRepositoryCommandRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadCommitAuthorDatesAsync(
        Repository repository,
        IReadOnlyList<string> commits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(commits);

        var requested = commits
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requested.Length == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var commit in requested)
            GitRefValidator.ValidateObjectId(commit, nameof(commits));

        var arguments = new List<string>
        {
            "log",
            "--no-walk=unsorted",
            "--format=%H%x00%aI"
        };
        arguments.AddRange(requested);

        string output;
        try
        {
            output = await _runner.RunAsync(
                repository.WorkingDirectory,
                cancellationToken,
                true,
                arguments.ToArray());
        }
        catch (RepositoryOpenException exception)
        {
            throw new InvalidOperationException(
                "Could not read the original author date for one or more selected commits.",
                exception);
        }

        var byFullHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split('\0');
            if (fields.Length != 2
                || string.IsNullOrWhiteSpace(fields[0])
                || string.IsNullOrWhiteSpace(fields[1]))
            {
                throw new InvalidOperationException(
                    "Git returned invalid author date metadata.");
            }

            byFullHash[fields[0]] = fields[1];
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var commit in requested)
        {
            string? authorDate = null;
            var matchCount = 0;
            foreach (var pair in byFullHash)
            {
                if (!pair.Key.StartsWith(commit, StringComparison.OrdinalIgnoreCase))
                    continue;

                authorDate = pair.Value;
                matchCount++;
                if (matchCount > 1)
                    break;
            }

            if (matchCount != 1 || string.IsNullOrWhiteSpace(authorDate))
            {
                var displayCommit = commit.Length <= 8
                    ? commit
                    : commit[..8];
                throw new InvalidOperationException(
                    $"Could not read the original author date for commit {displayCommit}.");
            }

            result[commit] = authorDate;
        }

        return result;
    }
}
