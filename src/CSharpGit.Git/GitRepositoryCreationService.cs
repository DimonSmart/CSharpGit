using System.ComponentModel;
using System.Security;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;

namespace CSharpGit.Git;

internal sealed class GitRepositoryCreationService : IRepositoryCreationService
{
    private readonly GitCommandExecutor _executor;

    internal GitRepositoryCreationService(GitCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public async Task CreateAsync(
        string path,
        RepositoryCreationKind kind,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = NormalizeTargetPath(path);

        try
        {
            if (File.Exists(target))
            {
                throw new RepositoryCreationException(
                    RepositoryCreationFailureKind.TargetIsFile,
                    "A file already exists at the selected path.");
            }

            var targetExists = Directory.Exists(target);
            if (targetExists)
            {
                if (await IsRepositoryTargetAsync(target, cancellationToken))
                {
                    throw new RepositoryCreationException(
                        RepositoryCreationFailureKind.RepositoryAlreadyExists,
                        "A Git repository already exists in this directory.");
                }

                if (kind == RepositoryCreationKind.BareShared
                    && Directory.EnumerateFileSystemEntries(target).Any())
                {
                    throw new RepositoryCreationException(
                        RepositoryCreationFailureKind.BareTargetNotEmpty,
                        "A central repository must be created in an empty directory.");
                }
            }

            var workingDirectory = FindNearestExistingDirectory(target);
            if (workingDirectory is null)
            {
                throw new RepositoryCreationException(
                    RepositoryCreationFailureKind.TargetCannotBeCreated,
                    "The selected repository directory cannot be created.");
            }

            var arguments = kind switch
            {
                RepositoryCreationKind.WorkingTree =>
                    new[] { "init", target },
                RepositoryCreationKind.BareShared =>
                    new[] { "init", "--bare", "--shared=all", target },
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };

            var result = await _executor.ExecuteForResultAsync(
                workingDirectory,
                "CreateRepository",
                GitCommandKind.User,
                cancellationToken,
                environment: null,
                arguments);

            if (result.ExitCode != 0)
                throw CreateGitFailure(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RepositoryCreationException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw CreateAccessDenied(exception);
        }
        catch (SecurityException exception)
        {
            throw CreateAccessDenied(exception);
        }
        catch (Win32Exception exception)
        {
            if (exception.NativeErrorCode is 5 or 13)
                throw CreateAccessDenied(exception);

            throw new RepositoryCreationException(
                RepositoryCreationFailureKind.GitUnavailable,
                $"Git executable '{_executor.ExecutablePath}' was not found or could not be started.",
                innerException: exception);
        }
        catch (IOException exception)
        {
            throw new RepositoryCreationException(
                RepositoryCreationFailureKind.TargetCannotBeCreated,
                "The selected repository directory cannot be created.",
                innerException: exception);
        }
    }

    private async Task<bool> IsRepositoryTargetAsync(
        string target,
        CancellationToken cancellationToken)
    {
        var kind = await RunProbeAsync(
            target,
            cancellationToken,
            "rev-parse",
            "--is-bare-repository");
        if (kind.ExitCode != 0)
            return false;

        if (string.Equals(
                kind.StandardOutput.Trim(),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            var gitDirectory = await RunProbeAsync(
                target,
                cancellationToken,
                "rev-parse",
                "--absolute-git-dir");
            return gitDirectory.ExitCode == 0
                   && PathsEqual(gitDirectory.StandardOutput.Trim(), target);
        }

        var repositoryRoot = await RunProbeAsync(
            target,
            cancellationToken,
            "rev-parse",
            "--show-toplevel");
        return repositoryRoot.ExitCode == 0
               && PathsEqual(repositoryRoot.StandardOutput.Trim(), target);
    }

    private Task<GitCommandResult> RunProbeAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        _executor.ExecuteForResultAsync(
            workingDirectory,
            "CreateRepositoryProbe",
            GitCommandKind.Internal,
            cancellationToken,
            environment: null,
            arguments);

    private static string NormalizeTargetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw InvalidPath();

        var trimmed = path.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
            throw InvalidPath();

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            throw InvalidPath(exception);
        }
    }

    private static RepositoryCreationException InvalidPath(
        Exception? innerException = null) =>
        new(
            RepositoryCreationFailureKind.InvalidPath,
            "Enter a valid absolute directory path.",
            innerException: innerException);

    private static string? FindNearestExistingDirectory(string target)
    {
        if (Directory.Exists(target))
            return target;

        var current = Path.GetDirectoryName(target);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(current))
                return current;

            var parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.Ordinal))
                break;
            current = parent;
        }

        return null;
    }

    private static RepositoryCreationException CreateGitFailure(
        GitCommandResult result)
    {
        var diagnostic = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();
        var accessDenied = diagnostic.Contains(
                               "permission denied",
                               StringComparison.OrdinalIgnoreCase)
                           || diagnostic.Contains(
                               "access is denied",
                               StringComparison.OrdinalIgnoreCase);

        return new RepositoryCreationException(
            accessDenied
                ? RepositoryCreationFailureKind.AccessDenied
                : RepositoryCreationFailureKind.GitFailed,
            accessDenied
                ? "Access to the selected directory was denied."
                : "Could not create repository.",
            result.ExitCode,
            string.IsNullOrWhiteSpace(diagnostic) ? null : diagnostic);
    }

    private static RepositoryCreationException CreateAccessDenied(
        Exception innerException) =>
        new(
            RepositoryCreationFailureKind.AccessDenied,
            "Access to the selected directory was denied.",
            innerException: innerException);

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }
}
