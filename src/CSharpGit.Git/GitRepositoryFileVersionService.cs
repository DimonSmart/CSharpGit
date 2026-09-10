using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed class GitRepositoryFileVersionService : IRepositoryFileVersionService
{
    private readonly string _gitExecutable;
    private readonly string _cacheRoot;

    public GitRepositoryFileVersionService() : this(new GitCliOptions()) { }

    public GitRepositoryFileVersionService(GitCliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _gitExecutable = string.IsNullOrWhiteSpace(options.ExecutablePath) ? "git" : options.ExecutablePath;
        _cacheRoot = Path.Combine(Path.GetTempPath(), "CSharpGit", "file-versions");
        TryCleanupCache();
    }

    public async Task<DiffFileVersionPair> ResolveCommitAsync(
        Repository repository,
        string commitHash,
        string selectedPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(commitHash);
        ValidateGitPath(selectedPath);

        var parentOutput = await RunGitAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "show", "-s", "--format=%P", commitHash);
        var firstParent = parentOutput
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        string raw;
        if (firstParent is null)
        {
            raw = await RunGitAsync(
                repository.WorkingDirectory,
                cancellationToken,
                "diff-tree", "--root", "--no-commit-id", "--raw", "-r", "-z",
                "--find-renames", "--find-copies", commitHash);
        }
        else
        {
            raw = await RunGitAsync(
                repository.WorkingDirectory,
                cancellationToken,
                "diff", "--raw", "-z", "--no-ext-diff", "--find-renames", "--find-copies",
                firstParent, commitHash);
        }

        var entry = ParseRawDiff(raw)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.NewPath, selectedPath, StringComparison.Ordinal) ||
                string.Equals(candidate.OldPath, selectedPath, StringComparison.Ordinal));
        if (entry is null)
            throw new InvalidOperationException("The selected file is no longer part of this commit diff.");

        var original = BuildSnapshotVersion(
            entry.OldPath,
            firstParent ?? "<empty-tree>",
            entry.OldBlob,
            entry.OldMode,
            entry.Status == 'A' ? "This file did not exist before this commit." : "The original version is not available.");
        var changed = BuildSnapshotVersion(
            entry.NewPath,
            commitHash,
            entry.NewBlob,
            entry.NewMode,
            entry.Status == 'D' ? "This file was deleted by this commit." : "The changed version is not available.");

        return new DiffFileVersionPair(original, changed, entry.NewPath, entry.Status.ToString());
    }

    public async Task<DiffFileVersionPair> ResolveWorkingTreeAsync(
        Repository repository,
        WorkingTreeChange change,
        WorkingTreeDiffKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(change);
        ValidateGitPath(change.Path);
        if (change.OriginalPath is not null) ValidateGitPath(change.OriginalPath);

        if (change.IsConflicted)
        {
            var unavailable = Unavailable(change.Path, "Use the conflict workflow for this file.");
            return new DiffFileVersionPair(unavailable, unavailable, change.Path, "U");
        }

        return kind switch
        {
            WorkingTreeDiffKind.Unstaged => await ResolveUnstagedAsync(repository, change, cancellationToken),
            WorkingTreeDiffKind.Staged => await ResolveStagedAsync(repository, change, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    public async Task<MaterializedFileVersion> MaterializeAsync(
        Repository repository,
        DiffFileVersion version,
        DiffFileSide side,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(version);
        cancellationToken.ThrowIfCancellationRequested();

        if (version.Location != DiffFileVersionLocation.GitSnapshot ||
            string.IsNullOrWhiteSpace(version.BlobId) ||
            string.IsNullOrWhiteSpace(version.RevisionIdentity))
            throw new InvalidOperationException(version.UnavailableReason ?? "This file version cannot be materialized.");
        if (version.EntryKind != GitEntryKind.RegularFile)
            throw new NotSupportedException(UnsupportedEntryMessage(version.EntryKind));

        ValidateObjectName(version.BlobId);
        ValidateGitPath(version.GitPath);

        var repositoryIdentity = Hash(Path.GetFullPath(repository.GitDirectory));
        var identity = Hash(string.Join('\n',
            repositoryIdentity,
            version.RevisionIdentity,
            version.BlobId,
            version.GitPath,
            side.ToString()));
        var directory = Path.Combine(_cacheRoot, repositoryIdentity, identity);
        Directory.CreateDirectory(directory);
        var finalPath = Path.Combine(directory, SafeFileName(version.GitPath));

        if (File.Exists(finalPath)) return new MaterializedFileVersion(finalPath);

        var temporaryPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await WriteBlobAsync(repository.WorkingDirectory, version.BlobId, temporaryPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                File.SetAttributes(temporaryPath, File.GetAttributes(temporaryPath) | FileAttributes.ReadOnly);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Read-only is best effort. Isolation from the repository is the real safety boundary.
            }

            try
            {
                File.Move(temporaryPath, finalPath);
            }
            catch (IOException) when (File.Exists(finalPath))
            {
                TryDelete(temporaryPath);
            }

            if (!File.Exists(finalPath))
                throw new IOException("The historical file snapshot could not be finalized.");
            return new MaterializedFileVersion(finalPath);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private async Task<DiffFileVersionPair> ResolveUnstagedAsync(
        Repository repository,
        WorkingTreeChange change,
        CancellationToken cancellationToken)
    {
        if (!change.IsUnstaged)
            throw new InvalidOperationException("The selected file no longer has unstaged changes.");

        var originalPath = change.WorkingTreeStatus is 'R' or 'C' && change.OriginalPath is not null
            ? change.OriginalPath
            : change.Path;

        DiffFileVersion original;
        if (change.IndexStatus == '?')
        {
            original = Unavailable(originalPath, "This file did not exist before the working-tree change.");
        }
        else
        {
            original = await ResolveIndexVersionAsync(repository, originalPath, cancellationToken)
                ?? Unavailable(originalPath, "The original index version is not available.");
        }

        var changed = change.WorkingTreeStatus == 'D'
            ? Unavailable(change.Path, "This file was deleted from the working tree.")
            : new DiffFileVersion(DiffFileVersionLocation.WorkingCopy, change.Path);

        return new DiffFileVersionPair(original, changed, change.Path, change.WorkingTreeStatus.ToString());
    }

    private async Task<DiffFileVersionPair> ResolveStagedAsync(
        Repository repository,
        WorkingTreeChange change,
        CancellationToken cancellationToken)
    {
        if (!change.IsStaged)
            throw new InvalidOperationException("The selected file is no longer staged.");

        var originalPath = change.IndexStatus is 'R' or 'C' && change.OriginalPath is not null
            ? change.OriginalPath
            : change.Path;
        var head = await RunOptionalGitAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-parse", "--verify", "HEAD");

        DiffFileVersion original;
        if (change.IndexStatus == 'A' || string.IsNullOrWhiteSpace(head))
        {
            original = Unavailable(originalPath, "This file did not exist in HEAD.");
        }
        else
        {
            original = await ResolveTreeVersionAsync(repository, head, originalPath, cancellationToken)
                ?? Unavailable(originalPath, "The HEAD version is not available.");
        }

        var changed = change.IndexStatus == 'D'
            ? Unavailable(change.Path, "This file was deleted from the index.")
            : await ResolveIndexVersionAsync(repository, change.Path, cancellationToken)
                ?? Unavailable(change.Path, "The staged version is not available.");

        return new DiffFileVersionPair(original, changed, change.Path, change.IndexStatus.ToString());
    }

    private async Task<DiffFileVersion?> ResolveTreeVersionAsync(
        Repository repository,
        string revision,
        string path,
        CancellationToken cancellationToken)
    {
        var output = await RunGitAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "ls-tree", "-z", revision, "--", path);
        if (output.Length == 0) return null;

        var record = output.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (record is null) return null;
        var tab = record.IndexOf('\t');
        if (tab < 0) return null;
        var metadata = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (metadata.Length < 3) return null;
        return BuildSnapshotVersion(path, revision, metadata[2], metadata[0], "The requested Git version is not available.");
    }

    private async Task<DiffFileVersion?> ResolveIndexVersionAsync(
        Repository repository,
        string path,
        CancellationToken cancellationToken)
    {
        var output = await RunGitAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "ls-files", "--stage", "-z", "--", path);
        if (output.Length == 0) return null;

        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab < 0) continue;
            var metadata = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length != 3 || metadata[2] != "0") continue;
            var actualPath = record[(tab + 1)..];
            return BuildSnapshotVersion(actualPath, $"index:{metadata[1]}", metadata[1], metadata[0], "The index version is not available.");
        }
        return null;
    }

    private async Task WriteBlobAsync(
        string workingDirectory,
        string blobId,
        string destination,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(workingDirectory, "cat-file", "blob", blobId);
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
        {
            await process.StandardOutput.BaseStream.CopyToAsync(file, cancellationToken);
            await file.FlushAsync(cancellationToken);
        }
        await process.WaitForExitAsync(cancellationToken);
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "The requested Git blob is no longer available."
                : $"Git could not read the requested blob: {error}");
    }

    private async Task<string> RunGitAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var startInfo = CreateStartInfo(workingDirectory, arguments);
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"Git exited with code {process.ExitCode}."
                : $"Git exited with code {process.ExitCode}: {error}");
        return output.TrimEnd('\r', '\n');
    }

    private async Task<string> RunOptionalGitAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        try
        {
            return await RunGitAsync(workingDirectory, cancellationToken, arguments);
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private ProcessStartInfo CreateStartInfo(string workingDirectory, params string[] arguments)
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
        return startInfo;
    }

    private static IEnumerable<RawDiffEntry> ParseRawDiff(string output)
    {
        var records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < records.Length; index++)
        {
            var metadataText = records[index].TrimStart('\r', '\n');
            if (!metadataText.StartsWith(':')) continue;
            var metadata = metadataText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length < 5 || index + 1 >= records.Length) continue;

            var oldMode = metadata[0][1..];
            var newMode = metadata[1];
            var oldBlob = metadata[2];
            var newBlob = metadata[3];
            var statusText = metadata[4];
            if (statusText.Length == 0) continue;
            var status = statusText[0];
            var firstPath = records[++index];
            var oldPath = firstPath;
            var newPath = firstPath;
            if (status is 'R' or 'C' && index + 1 < records.Length)
                newPath = records[++index];

            yield return new RawDiffEntry(oldMode, newMode, oldBlob, newBlob, status, oldPath, newPath);
        }
    }

    private static DiffFileVersion BuildSnapshotVersion(
        string path,
        string revision,
        string blob,
        string mode,
        string unavailableReason)
    {
        if (IsZeroObject(blob) || mode == "000000") return Unavailable(path, unavailableReason);
        var kind = EntryKind(mode);
        return new DiffFileVersion(
            DiffFileVersionLocation.GitSnapshot,
            path,
            revision,
            blob,
            kind,
            kind == GitEntryKind.RegularFile ? null : UnsupportedEntryMessage(kind));
    }

    private static DiffFileVersion Unavailable(string path, string reason) =>
        new(DiffFileVersionLocation.Unavailable, path, EntryKind: GitEntryKind.Missing, UnavailableReason: reason);

    private static GitEntryKind EntryKind(string mode) => mode switch
    {
        "100644" or "100755" => GitEntryKind.RegularFile,
        "120000" => GitEntryKind.SymbolicLink,
        "160000" => GitEntryKind.GitLink,
        "000000" => GitEntryKind.Missing,
        _ => GitEntryKind.Unsupported
    };

    private static string UnsupportedEntryMessage(GitEntryKind kind) => kind switch
    {
        GitEntryKind.SymbolicLink => "Opening symbolic-link snapshots is not supported.",
        GitEntryKind.GitLink => "Opening submodule entries is not supported.",
        _ => "Opening this Git entry type is not supported."
    };

    private static bool IsZeroObject(string value) =>
        value.Length > 0 && value.All(character => character == '0');

    private static void ValidateObjectName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Invalid Git object id.", nameof(value));
    }

    private static void ValidateGitPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0') || Path.IsPathRooted(path))
            throw new ArgumentException("Invalid Git file path.", nameof(path));
        if (path.Replace('\\', '/').Split('/').Any(part => part is ".." or "."))
            throw new ArgumentException("Git file path traversal is not allowed.", nameof(path));
    }

    private static string SafeFileName(string gitPath)
    {
        var name = gitPath.Replace('\\', '/').Split('/').LastOrDefault() ?? "snapshot";
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
            builder.Append(invalid.Contains(character) || char.IsControl(character) ? '_' : character);
        var safe = builder.ToString().TrimEnd(' ', '.');
        if (safe.Length == 0) safe = "snapshot";

        if (OperatingSystem.IsWindows())
        {
            var stem = Path.GetFileNameWithoutExtension(safe);
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }
                .Contains(stem, StringComparer.OrdinalIgnoreCase))
                safe = "_" + safe;
        }
        return safe;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private void TryCleanupCache()
    {
        try
        {
            if (!Directory.Exists(_cacheRoot)) return;
            var threshold = DateTime.UtcNow.AddDays(-14);
            foreach (var file in Directory.EnumerateFiles(_cacheRoot, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) >= threshold) continue;
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record RawDiffEntry(
        string OldMode,
        string NewMode,
        string OldBlob,
        string NewBlob,
        char Status,
        string OldPath,
        string NewPath);
}
