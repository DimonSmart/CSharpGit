using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

// Uses the index gitlinks and Git's own status, not directory-existence heuristics.
internal sealed class GitSubmoduleService(GitRepositoryCommandRunner runner) : ISubmoduleService
{
    private readonly GitRepositoryCommandRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    public Task<IReadOnlyList<SubmoduleInfo>> ReadAsync(
        Repository repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return ReadCoreAsync(repository.WorkingDirectory, cancellationToken);
    }

    private async Task<IReadOnlyList<SubmoduleInfo>> ReadCoreAsync(
        string directory, CancellationToken cancellationToken)
    {
        var index = await _runner.RunAsync(directory, cancellationToken, false,
            "ls-files", "--stage", "-z");
        var gitlinks = ParseGitlinks(index);
        if (gitlinks.Count == 0) return [];

        var urls = await ReadUrlsAsync(directory, cancellationToken);
        var porcelain = await _runner.RunAsync(directory, cancellationToken, false,
            "status", "--porcelain=v2", "-z", "--untracked-files=all");
        var changes = GitRepositoryStateService.ParseStatusV2(porcelain).Changes
            .Where(change => change.IsSubmodule)
            .ToDictionary(change => change.Path, StringComparer.Ordinal);
        var infos = new List<SubmoduleInfo>(gitlinks.Count);
        foreach (var gitlink in gitlinks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            changes.TryGetValue(gitlink.Path, out var change);
            urls.TryGetValue(gitlink.Path, out var url);
            var result = await _runner.RunForResultAsync(
                directory, "SubmoduleStatus", GitCommandKind.Internal,
                cancellationToken, GitRepositoryStateService.ReadOnlyEnvironment,
                ["submodule", "status", "--", GitPathValidator.LiteralPathspec(gitlink.Path)]);
            var line = result.StandardOutput.TrimEnd('\r', '\n');
            var hasStatus = result.ExitCode == 0 && line.Length >= 2
                && (line[0] is '-' or '+' or 'U' or ' ')
                && Uri.IsHexDigit(line[1]);
            var initialized = hasStatus && line[0] != '-';
            var conflicted = change?.IsConflicted == true || (hasStatus && line[0] == 'U');
            var currentCommit = initialized ? line[1..].Split(' ', 2)[0] : null;
            var error = hasStatus ? null :
                result.ExitCode != 0
                    ? (string.IsNullOrWhiteSpace(result.StandardError)
                        ? $"Git submodule status exited with code {result.ExitCode}."
                        : result.StandardError.Trim())
                    : "Git did not return a valid submodule status.";
            string? branch = null;
            if (initialized && !conflicted && error is null)
            {
                var child = ResolvePath(directory, gitlink.Path);
                var branchResult = await _runner.RunForResultAsync(
                    child, "SubmoduleBranch", GitCommandKind.Internal,
                    cancellationToken, GitRepositoryStateService.ReadOnlyEnvironment,
                    ["symbolic-ref", "--quiet", "--short", "HEAD"]);
                if (branchResult.ExitCode == 0)
                    branch = branchResult.StandardOutput.TrimEnd('\r', '\n');
                else if (branchResult.ExitCode != 1)
                    error = string.IsNullOrWhiteSpace(branchResult.StandardError)
                        ? $"Could not read submodule branch (exit {branchResult.ExitCode})."
                        : branchResult.StandardError.Trim();
            }
            infos.Add(new SubmoduleInfo(
                gitlink.Path, gitlink.Commit, currentCommit, branch, url,
                initialized, conflicted,
                (change?.SubmoduleCommitChanged ?? false) || (hasStatus && line[0] == '+'),
                change?.SubmoduleModifiedContent ?? false,
                change?.SubmoduleUntrackedContent ?? false,
                error));
        }
        return infos;
    }

    public async Task InitializeAsync(
        Repository repository, string path, CancellationToken cancellationToken = default)
    {
        var info = await RequiredAsync(repository, path, cancellationToken);
        if (info.IsInitialized)
            throw new InvalidOperationException("The submodule is already initialized.");
        if (info.ErrorMessage is not null || info.IsConflicted)
            throw new InvalidOperationException(info.ErrorMessage ?? "Resolve the gitlink conflict first.");
        await _runner.RunMutationAsync(repository, cancellationToken,
            "submodule", "update", "--init", "--", GitPathValidator.LiteralPathspec(path));
    }

    public async Task UpdateAsync(
        Repository repository, string path, CancellationToken cancellationToken = default)
    {
        var info = await RequiredAsync(repository, path, cancellationToken);
        if (!info.IsInitialized)
            throw new InvalidOperationException("Initialize the submodule first.");
        EnsureSafe(info);
        await EnsureSafeNestedAsync(
            ResolvePath(repository.WorkingDirectory, path), new HashSet<string>(PathComparer()), cancellationToken);
        await _runner.RunMutationAsync(repository, cancellationToken,
            "submodule", "update", "--", GitPathValidator.LiteralPathspec(path));
    }

    public async Task UpdateAllAsync(
        Repository repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        await EnsureSafeNestedAsync(repository.WorkingDirectory,
            new HashSet<string>(PathComparer()), cancellationToken);
        await _runner.RunMutationAsync(repository, cancellationToken,
            "submodule", "update", "--init", "--recursive");
    }

    private async Task EnsureSafeNestedAsync(
        string directory, HashSet<string> visited, CancellationToken cancellationToken)
    {
        if (!visited.Add(Path.GetFullPath(directory))) return;
        foreach (var info in await ReadCoreAsync(directory, cancellationToken))
        {
            if (!info.IsInitialized)
            {
                if (info.IsConflicted || info.ErrorMessage is not null) EnsureSafe(info);
                continue;
            }
            EnsureSafe(info);
            await EnsureSafeNestedAsync(
                ResolvePath(directory, info.Path), visited, cancellationToken);
        }
    }

    private static void EnsureSafe(SubmoduleInfo info)
    {
        if (info.IsConflicted || info.ErrorMessage is not null || info.HasLocalChanges)
            throw new InvalidOperationException(
                $"Cannot update submodule '{info.Path}': " +
                (info.ErrorMessage ?? (info.IsConflicted
                    ? "resolve the gitlink conflict first."
                    : "commit or stash its local changes first.")));
    }

    private async Task<SubmoduleInfo> RequiredAsync(
        Repository repository, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        GitPathValidator.ValidateRepositoryRelative(path);
        var infos = await ReadAsync(repository, cancellationToken);
        return infos.SingleOrDefault(info => string.Equals(info.Path, path, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The path is not a gitlink in the parent index.");
    }

    private static string ResolvePath(string root, string relativePath)
    {
        GitPathValidator.ValidateRepositoryRelative(relativePath);
        return Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static List<(string Path, string Commit)> ParseGitlinks(string output)
    {
        var result = new List<(string, string)>();
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab < 0) continue;
            var metadata = record[..tab].Split(' ');
            if (metadata.Length != 3 || metadata[0] != "160000" || metadata[2] != "0")
                continue;
            result.Add((record[(tab + 1)..], metadata[1]));
        }
        return result;
    }

    private async Task<Dictionary<string, string>> ReadUrlsAsync(
        string directory, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(Path.Combine(directory, ".gitmodules"))) return result;

        var output = await _runner.RunAsync(directory, cancellationToken, false,
            "config", "--null", "--file", ".gitmodules", "--list");
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = record.IndexOf('\n');
            if (split < 0) continue;
            var key = record[..split];
            var value = record[(split + 1)..];
            if (!key.StartsWith("submodule.", StringComparison.Ordinal)) continue;
            if (key.EndsWith(".path", StringComparison.Ordinal))
                paths[key[10..^5]] = value;
            else if (key.EndsWith(".url", StringComparison.Ordinal))
                urls[key[10..^4]] = value;
        }
        foreach (var (name, path) in paths)
            if (urls.TryGetValue(name, out var url))
                result[path] = url;
        return result;
    }
}
