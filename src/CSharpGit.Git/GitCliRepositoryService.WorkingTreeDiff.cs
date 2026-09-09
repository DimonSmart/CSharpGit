using System.Diagnostics;
using System.Text;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed partial class GitCliRepositoryService : IWorkingTreeDiffService
{
    public async Task<FileDiff> ReadDiffAsync(
        Repository repository,
        WorkingTreeChange change,
        WorkingTreeDiffKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(change);
        ValidateChange(change);

        if (change.IsConflicted)
        {
            return new FileDiff(
                change.Path,
                false,
                [new DiffLine("Conflict — use the conflict workflow to resolve this file.", DiffLineKind.Header)]);
        }

        var kindMatchesStatus = kind switch
        {
            WorkingTreeDiffKind.Unstaged => change.IsUnstaged,
            WorkingTreeDiffKind.Staged => change.IsStaged,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (!kindMatchesStatus)
            return new FileDiff(change.Path, false, []);

        if (kind == WorkingTreeDiffKind.Unstaged && change.IndexStatus == '?')
            return await ReadUntrackedDiffAsync(repository, change, cancellationToken);

        var arguments = new List<string> { "diff" };
        if (kind == WorkingTreeDiffKind.Staged) arguments.Add("--cached");
        arguments.Add("--no-ext-diff");
        arguments.Add("--find-renames");
        arguments.Add("--");
        arguments.Add(change.Path);
        if (change.OriginalPath is not null) arguments.Add(change.OriginalPath);

        var output = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, arguments.ToArray());
        return BuildWorkingTreeDiff(change.Path, output);
    }

    private async Task<FileDiff> ReadUntrackedDiffAsync(
        Repository repository,
        WorkingTreeChange change,
        CancellationToken cancellationToken)
    {
        var fullPath = ResolveSafeWorkingTreePath(repository, change.Path);
        if (!File.Exists(fullPath))
            return new FileDiff(change.Path, false, []);

        var emptyPath = Path.Combine(Path.GetTempPath(), $"csharpgit-empty-{Guid.NewGuid():N}");
        await File.WriteAllBytesAsync(emptyPath, [], cancellationToken);
        try
        {
            var output = await RunGitNoIndexAsync(
                repository.WorkingDirectory,
                cancellationToken,
                "diff", "--no-index", "--no-ext-diff", "--", emptyPath, fullPath);

            if (output.Length == 0)
            {
                return new FileDiff(
                    change.Path,
                    false,
                    [new DiffLine("new empty file", DiffLineKind.Header)]);
            }

            return BuildWorkingTreeDiff(change.Path, output);
        }
        finally
        {
            try { File.Delete(emptyPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static FileDiff BuildWorkingTreeDiff(string path, string output)
    {
        var binary = output.Contains("Binary files ", StringComparison.Ordinal) ||
                     output.Contains("GIT binary patch", StringComparison.Ordinal);
        return new FileDiff(
            path,
            binary,
            binary || output.Length == 0 ? [] : ParseDiffLines(output));
    }

    private async Task<string> RunGitNoIndexAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
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
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = (await standardOutput).TrimEnd('\r', '\n');
        var error = (await standardError).Trim();
        if (process.ExitCode is not 0 and not 1)
        {
            var detail = string.IsNullOrWhiteSpace(error)
                ? "Git returned no diagnostic message."
                : error;
            throw new RepositoryOpenException($"Git command exited with code {process.ExitCode}: {detail}");
        }

        return output;
    }

    private static string ResolveSafeWorkingTreePath(Repository repository, string path)
    {
        ValidatePath(path);
        var root = Path.GetFullPath(repository.WorkingDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootedPrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootedPrefix, comparison))
            throw new ArgumentException("The file path is outside the repository.", nameof(path));
        return fullPath;
    }
}
