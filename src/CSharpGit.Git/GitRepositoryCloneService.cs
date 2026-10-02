using System.ComponentModel;
using System.Security;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;

namespace CSharpGit.Git;

internal sealed class GitRepositoryCloneService : IRepositoryCloneService
{
    private readonly GitCommandExecutor _executor;

    internal GitRepositoryCloneService(GitCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public async Task CloneAsync(
        string repositoryUrl,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryUrl);

        var source = repositoryUrl.Trim();
        var target = NormalizeTargetPath(targetPath);

        try
        {
            if (File.Exists(target))
            {
                throw new RepositoryCloneException(
                    RepositoryCloneFailureKind.TargetIsFile,
                    "A file already exists at the selected path.");
            }

            if (Directory.Exists(target)
                && Directory.EnumerateFileSystemEntries(target).Any())
            {
                throw new RepositoryCloneException(
                    RepositoryCloneFailureKind.TargetNotEmpty,
                    "The selected directory is not empty.");
            }

            var parent = Path.GetDirectoryName(target);
            if (string.IsNullOrWhiteSpace(parent))
            {
                throw new RepositoryCloneException(
                    RepositoryCloneFailureKind.TargetCannotBeCreated,
                    "The selected repository directory cannot be created.");
            }

            Directory.CreateDirectory(parent);

            var result = await _executor.ExecuteForResultAsync(
                parent,
                "CloneRepository",
                GitCommandKind.User,
                cancellationToken,
                environment: null,
                ["clone", "--", source, target]);

            if (result.ExitCode != 0)
                throw CreateGitFailure(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RepositoryCloneException)
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

            throw new RepositoryCloneException(
                RepositoryCloneFailureKind.GitUnavailable,
                $"Git executable '{_executor.ExecutablePath}' was not found or could not be started.",
                innerException: exception);
        }
        catch (IOException exception)
        {
            throw new RepositoryCloneException(
                RepositoryCloneFailureKind.TargetCannotBeCreated,
                "The selected repository directory cannot be created.",
                innerException: exception);
        }
    }

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

    private static RepositoryCloneException InvalidPath(Exception? innerException = null) =>
        new(
            RepositoryCloneFailureKind.InvalidTargetPath,
            "Enter a valid absolute local directory path.",
            innerException: innerException);

    private static RepositoryCloneException CreateGitFailure(GitCommandResult result)
    {
        var diagnostic = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();

        return new RepositoryCloneException(
            RepositoryCloneFailureKind.GitFailed,
            "Could not clone repository.",
            result.ExitCode,
            string.IsNullOrWhiteSpace(diagnostic) ? null : diagnostic);
    }

    private static RepositoryCloneException CreateAccessDenied(Exception innerException) =>
        new(
            RepositoryCloneFailureKind.AccessDenied,
            "Access to the selected directory was denied.",
            innerException: innerException);
}
