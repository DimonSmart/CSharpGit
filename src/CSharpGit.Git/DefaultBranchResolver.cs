using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class DefaultBranchResolver
{
    private readonly GitCommandExecutor _executor;

    internal DefaultBranchResolver(GitCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    internal async Task<string?> ResolveAsync(
        Repository repository,
        GitReferences references,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(references);

        var remote = SelectPrimaryRemote(references.Remotes);
        if (remote is null) return null;

        var localHead = await RunOptionalAsync(
            repository,
            cancellationToken,
            "symbolic-ref", "--quiet", "--short", $"refs/remotes/{remote}/HEAD");
        if (IsValidLocalRemoteHead(remote, localHead, references.RemoteBranches))
            return localHead;

        var advertisedHead = await RunOptionalAsync(
            repository,
            cancellationToken,
            "ls-remote", "--symref", remote, "HEAD");
        return ParseAdvertisedHead(remote, advertisedHead);
    }

    internal async Task RefreshRemoteHeadAsync(
        Repository repository,
        string remote,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remote)) return;
        _ = await RunOptionalAsync(repository, cancellationToken, "remote", "set-head", remote, "--auto");
    }

    internal async Task RefreshAllRemoteHeadsAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        var output = await RunOptionalAsync(repository, cancellationToken, "remote");
        if (string.IsNullOrWhiteSpace(output)) return;

        foreach (var remote in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            await RefreshRemoteHeadAsync(repository, remote, cancellationToken);
    }

    private async Task<string?> RunOptionalAsync(
        Repository repository,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        try
        {
            var result = await _executor.ExecuteForResultAsync(
                repository.WorkingDirectory,
                "DefaultBranch",
                GitCommandKind.Internal,
                cancellationToken,
                new Dictionary<string, string?> { ["GIT_TERMINAL_PROMPT"] = "0" },
                arguments);
            return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string? SelectPrimaryRemote(IReadOnlyList<GitRemote> remotes)
    {
        var origin = remotes.FirstOrDefault(remote => string.Equals(remote.Name, "origin", StringComparison.Ordinal));
        if (origin is not null) return origin.Name;
        return remotes.Count == 1 ? remotes[0].Name : null;
    }

    private static bool IsValidLocalRemoteHead(
        string remote,
        string? candidate,
        IReadOnlyList<GitBranch> remoteBranches)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            !candidate.StartsWith(remote + "/", StringComparison.Ordinal))
            return false;

        return remoteBranches.Any(branch => string.Equals(branch.Name, candidate, StringComparison.Ordinal));
    }

    private static string? ParseAdvertisedHead(string remote, string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        const string prefix = "ref: refs/heads/";
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t', 2);
            if (fields.Length != 2 || !string.Equals(fields[1], "HEAD", StringComparison.Ordinal)) continue;
            if (!fields[0].StartsWith(prefix, StringComparison.Ordinal)) continue;

            var branch = fields[0][prefix.Length..];
            if (!string.IsNullOrWhiteSpace(branch)) return $"{remote}/{branch}";
        }

        return null;
    }
}
