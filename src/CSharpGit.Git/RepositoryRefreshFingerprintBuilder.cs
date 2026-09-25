using System.Security.Cryptography;
using System.Text;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal static class RepositoryRefreshFingerprintBuilder
{
    private const int HashChunkSize = 64;

    internal static async Task<RepositoryRefreshFingerprint> BuildAsync(
        GitRepositoryCommandRunner runner,
        Repository repository,
        RepositoryState state,
        IReadOnlyList<string> relevantConfiguration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(relevantConfiguration);

        var indexContent = await ReadIndexContentAsync(
            runner,
            repository,
            state.Changes,
            cancellationToken);
        var workingTreeContent = await ReadWorkingTreeContentAsync(
            runner,
            repository,
            state.Changes,
            cancellationToken);

        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(fingerprint, "head-reference", state.HeadReference ?? string.Empty);
        Append(fingerprint, "head-commit", state.HeadCommit ?? string.Empty);
        Append(fingerprint, "detached", state.IsDetached ? "1" : "0");
        Append(fingerprint, "operation", state.Operation.ToString());

        foreach (var change in state.Changes
                     .OrderBy(change => change.Path, StringComparer.Ordinal)
                     .ThenBy(change => change.IndexStatus)
                     .ThenBy(change => change.WorkingTreeStatus))
        {
            Append(
                fingerprint,
                "change",
                string.Join(
                    '\0',
                    change.Path,
                    change.IndexStatus.ToString(),
                    change.WorkingTreeStatus.ToString(),
                    change.OriginalPath ?? string.Empty));
        }

        foreach (var branch in state.Refs.LocalBranches.OrderBy(branch => branch.Name, StringComparer.Ordinal))
        {
            Append(
                fingerprint,
                "local-ref",
                string.Join(
                    '\0',
                    branch.Name,
                    branch.Commit,
                    branch.Upstream ?? string.Empty,
                    branch.Ahead.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    branch.Behind.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    branch.IsCurrent ? "1" : "0",
                    branch.IsDefault ? "1" : "0"));
        }

        foreach (var branch in state.Refs.RemoteBranches.OrderBy(branch => branch.Name, StringComparer.Ordinal))
        {
            Append(
                fingerprint,
                "remote-ref",
                string.Join('\0', branch.Name, branch.Commit, branch.IsDefault ? "1" : "0"));
        }

        foreach (var tag in state.Refs.Tags.OrderBy(tag => tag.Name, StringComparer.Ordinal))
        {
            Append(
                fingerprint,
                "tag",
                string.Join('\0', tag.Name, tag.ObjectId, tag.TargetCommit));
        }

        foreach (var remote in state.Refs.Remotes.OrderBy(remote => remote.Name, StringComparer.Ordinal))
        {
            Append(
                fingerprint,
                "remote",
                string.Join('\0', remote.Name, remote.FetchUrl, remote.PushUrl));
        }

        foreach (var stash in state.Stashes.OrderBy(stash => stash.Name, StringComparer.Ordinal))
        {
            Append(
                fingerprint,
                "stash",
                string.Join('\0', stash.Name, stash.Commit, stash.Message));
        }

        foreach (var configuration in relevantConfiguration.Order(StringComparer.Ordinal))
            Append(fingerprint, "config", configuration);

        var operationState = state.CurrentOperation;
        Append(
            fingerprint,
            "operation-capabilities",
            string.Join(
                '\0',
                operationState.Kind.ToString(),
                operationState.CanContinue ? "1" : "0",
                operationState.CanAbort ? "1" : "0",
                operationState.CanSkip ? "1" : "0"));
        foreach (var conflict in operationState.Conflicts.OrderBy(conflict => conflict.Path, StringComparer.Ordinal))
        {
            Append(
                fingerprint,
                "conflict",
                string.Join(
                    '\0',
                    conflict.Path,
                    conflict.Kind.ToString(),
                    conflict.IsResolved ? "1" : "0",
                    conflict.CanOpenManually ? "1" : "0",
                    conflict.CanChooseCurrentLocal ? "1" : "0",
                    conflict.CanChooseIncomingRemote ? "1" : "0",
                    conflict.CanKeepDeletion ? "1" : "0",
                    conflict.CanStage ? "1" : "0",
                    conflict.CanRunMergeTool ? "1" : "0"));
        }

        Append(fingerprint, "index-content", indexContent);
        Append(fingerprint, "working-tree-content", workingTreeContent);
        return new RepositoryRefreshFingerprint(Convert.ToHexString(fingerprint.GetHashAndReset()));
    }

    private static async Task<string> ReadIndexContentAsync(
        GitRepositoryCommandRunner runner,
        Repository repository,
        IReadOnlyList<WorkingTreeChange> changes,
        CancellationToken cancellationToken)
    {
        var paths = changes
            .Where(change => change.IsStaged)
            .Select(change => change.Path)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0) return "clean";

        var entries = new List<string>();
        for (var offset = 0; offset < paths.Length; offset += HashChunkSize)
        {
            var chunk = paths.Skip(offset).Take(HashChunkSize).ToArray();
            var arguments = new List<string>(chunk.Length + 4)
            {
                "ls-files",
                "--stage",
                "-z",
                "--"
            };
            arguments.AddRange(chunk);
            var output = await runner.RunAsync(
                repository.WorkingDirectory,
                cancellationToken,
                false,
                GitRepositoryStateService.ReadOnlyEnvironment,
                arguments.ToArray());
            entries.AddRange(output.Split('\0', StringSplitOptions.RemoveEmptyEntries));
        }

        return string.Join('\0', entries.Order(StringComparer.Ordinal));
    }

    private static async Task<string> ReadWorkingTreeContentAsync(
        GitRepositoryCommandRunner runner,
        Repository repository,
        IReadOnlyList<WorkingTreeChange> changes,
        CancellationToken cancellationToken)
    {
        var paths = changes
            .Where(change => change.IsUnstaged || change.IndexStatus == '?')
            .Select(change => change.Path)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0) return "clean";

        var result = new StringBuilder();
        var files = new List<string>();
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(repository.WorkingDirectory, path);
            if (Directory.Exists(fullPath))
            {
                result.Append(path).Append('\0').Append("<directory>").Append('\0');
                continue;
            }

            if (!File.Exists(fullPath))
            {
                result.Append(path).Append('\0').Append("<deleted>").Append('\0');
                continue;
            }

            files.Add(path);
        }

        for (var offset = 0; offset < files.Count; offset += HashChunkSize)
        {
            var chunk = files.Skip(offset).Take(HashChunkSize).ToArray();
            var arguments = new List<string>(chunk.Length + 2) { "hash-object", "--" };
            arguments.AddRange(chunk);
            var output = await runner.RunAsync(
                repository.WorkingDirectory,
                cancellationToken,
                false,
                GitRepositoryStateService.ReadOnlyEnvironment,
                arguments.ToArray());
            var hashes = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (hashes.Length != chunk.Length)
                throw new IOException("Git returned an unexpected number of working-tree content hashes.");

            for (var index = 0; index < chunk.Length; index++)
                result.Append(chunk[index]).Append('\0').Append(hashes[index]).Append('\0');
        }

        return result.ToString();
    }

    private static void Append(IncrementalHash hash, string name, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(name));
        hash.AppendData([0]);
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0x1e]);
    }
}
