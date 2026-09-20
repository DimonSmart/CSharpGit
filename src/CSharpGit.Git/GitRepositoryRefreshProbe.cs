using System.Security.Cryptography;
using System.Text;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitRepositoryRefreshProbe(GitCommandExecutor executor) : IRepositoryRefreshProbe
{
    private const int HashChunkSize = 64;

    private static readonly IReadOnlyDictionary<string, string?> ProbeEnvironment =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GIT_OPTIONAL_LOCKS"] = "0"
        };

    private readonly GitCommandExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    public async Task<RepositoryRefreshFingerprint> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var status = await RunAsync(
            repository,
            cancellationToken,
            "status",
            "--porcelain=v2",
            "-z",
            "--branch",
            "--untracked-files=all");
        var index = await RunAsync(repository, cancellationToken, "ls-files", "--stage", "-z");
        var changedPaths = await RunAsync(
            repository,
            cancellationToken,
            "ls-files",
            "--modified",
            "--others",
            "--exclude-standard",
            "-z");
        var deletedPaths = await RunAsync(repository, cancellationToken, "ls-files", "--deleted", "-z");
        var workingTreeContent = await ReadWorkingTreeContentAsync(
            repository,
            changedPaths,
            deletedPaths,
            cancellationToken);
        var references = await RunAsync(
            repository,
            cancellationToken,
            "for-each-ref",
            "--sort=refname",
            "--format=%(refname)%00%(objectname)%00%(*objectname)%00%(symref)%00%(upstream:short)%00%(upstream:track)%1e",
            "refs/heads",
            "refs/remotes",
            "refs/tags");
        var stashes = await RunAsync(
            repository,
            cancellationToken,
            "stash",
            "list",
            "--format=%gd%x00%H%x00%gs%x1e");
        var remotes = await RunAsync(repository, cancellationToken, "remote", "-v");
        var mergeTool = await RunOptionalAsync(
            repository,
            cancellationToken,
            1,
            "config",
            "--local",
            "--get",
            "merge.tool");
        var operation = ReadOperationState(repository);

        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(fingerprint, "status", status);
        Append(fingerprint, "index", index);
        Append(fingerprint, "working-tree-content", workingTreeContent);
        Append(fingerprint, "references", references);
        Append(fingerprint, "stashes", stashes);
        Append(fingerprint, "remotes", remotes);
        Append(fingerprint, "merge-tool", mergeTool);
        Append(fingerprint, "operation", operation);
        return new RepositoryRefreshFingerprint(Convert.ToHexString(fingerprint.GetHashAndReset()));
    }

    private async Task<string> ReadWorkingTreeContentAsync(
        Repository repository,
        string changedPathsOutput,
        string deletedPathsOutput,
        CancellationToken cancellationToken)
    {
        var deleted = ParseNulList(deletedPathsOutput).ToHashSet(StringComparer.Ordinal);
        var paths = ParseNulList(changedPathsOutput)
            .Where(path => !deleted.Contains(path))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var result = new StringBuilder();
        foreach (var path in deleted.Order(StringComparer.Ordinal))
            result.Append(path).Append('\0').Append("<deleted>").Append('\0');

        var files = paths
            .Where(path => !Directory.Exists(Path.Combine(repository.WorkingDirectory, path)))
            .ToArray();
        foreach (var directory in paths.Except(files, StringComparer.Ordinal))
            result.Append(directory).Append('\0').Append("<directory>").Append('\0');

        for (var offset = 0; offset < files.Length; offset += HashChunkSize)
        {
            var chunk = files.Skip(offset).Take(HashChunkSize).ToArray();
            var arguments = new List<string>(chunk.Length + 2) { "hash-object", "--" };
            arguments.AddRange(chunk);
            var output = await RunAsync(repository, cancellationToken, arguments.ToArray());
            var hashes = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (hashes.Length != chunk.Length)
                throw new IOException("Git returned an unexpected number of working-tree content hashes.");

            for (var index = 0; index < chunk.Length; index++)
                result.Append(chunk[index]).Append('\0').Append(hashes[index]).Append('\0');
        }

        return result.ToString();
    }

    private Task<string> RunAsync(
        Repository repository,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        _executor.ExecuteAsync(
            repository.WorkingDirectory,
            "RepositoryRefreshProbe",
            GitCommandKind.Internal,
            cancellationToken,
            ProbeEnvironment,
            arguments);

    private async Task<string> RunOptionalAsync(
        Repository repository,
        CancellationToken cancellationToken,
        int allowedExitCode,
        params string[] arguments)
    {
        var result = await _executor.ExecuteForResultAsync(
            repository.WorkingDirectory,
            "RepositoryRefreshProbe",
            GitCommandKind.Internal,
            cancellationToken,
            ProbeEnvironment,
            arguments);
        if (result.ExitCode == 0) return result.StandardOutput;
        if (result.ExitCode == allowedExitCode) return string.Empty;

        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? "Git exited with code " + result.ExitCode + "."
            : result.StandardError.Trim();
        throw new InvalidOperationException("Repository refresh probe failed: " + detail);
    }

    private static IReadOnlyList<string> ParseNulList(string output) =>
        output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static string ReadOperationState(Repository repository)
    {
        var gitDirectory = repository.GitDirectory;
        var states = new List<string>(5);
        if (File.Exists(Path.Combine(gitDirectory, "MERGE_HEAD"))) states.Add("merge");
        if (Directory.Exists(Path.Combine(gitDirectory, "rebase-merge")) ||
            Directory.Exists(Path.Combine(gitDirectory, "rebase-apply")))
            states.Add("rebase");
        if (File.Exists(Path.Combine(gitDirectory, "CHERRY_PICK_HEAD"))) states.Add("cherry-pick");
        if (File.Exists(Path.Combine(gitDirectory, "REVERT_HEAD"))) states.Add("revert");
        if (File.Exists(Path.Combine(gitDirectory, "BISECT_LOG"))) states.Add("bisect");
        return string.Join('\0', states);
    }

    private static void Append(IncrementalHash hash, string name, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(name));
        hash.AppendData([0]);
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0x1e]);
    }
}
