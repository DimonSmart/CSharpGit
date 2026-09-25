using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitRepositoryCommandRunner
{
    private readonly GitCommandExecutor _executor;
    private readonly SemaphoreSlim _availabilityGate = new(1, 1);
    private int _gitAvailabilityValidated;

    internal GitRepositoryCommandRunner(GitCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    internal string ExecutablePath => _executor.ExecutablePath;

    internal async Task EnsureGitAvailableAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _gitAvailabilityValidated) != 0)
            return;

        await _availabilityGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _gitAvailabilityValidated) != 0)
                return;

            try
            {
                _ = await RunAsync(Environment.CurrentDirectory, cancellationToken, true, "--version");
                Volatile.Write(ref _gitAvailabilityValidated, 1);
            }
            catch (RepositoryOpenException exception)
            {
                throw new RepositoryOpenException(
                    "Git is installed but could not start correctly. Check the Git executable configuration.",
                    exception);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new RepositoryOpenException(
                    $"Git executable '{_executor.ExecutablePath}' was not found or could not be started.",
                    exception);
            }
        }
        finally
        {
            _availabilityGate.Release();
        }
    }

    internal Task<string> RunAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        bool requireOutput,
        params string[] arguments) =>
        RunAsync(
            workingDirectory,
            cancellationToken,
            requireOutput,
            null,
            GitCommandKind.Internal,
            arguments);

    internal Task<string> RunAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        bool requireOutput,
        IReadOnlyDictionary<string, string?>? environment,
        params string[] arguments) =>
        RunAsync(
            workingDirectory,
            cancellationToken,
            requireOutput,
            environment,
            GitCommandKind.Internal,
            arguments);

    internal async Task<string> RunAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        bool requireOutput,
        IReadOnlyDictionary<string, string?>? environment,
        GitCommandKind commandKind,
        params string[] arguments)
    {
        var result = await RunForResultAsync(
            workingDirectory,
            "Repository",
            commandKind,
            cancellationToken,
            environment,
            arguments);

        if (result.ExitCode != 0)
            throw CreateCommandFailure(result);

        if (requireOutput && string.IsNullOrWhiteSpace(result.StandardOutput))
            throw new RepositoryOpenException("Git command returned no output.");

        return result.StandardOutput;
    }

    internal async Task<string> RunOptionalAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var result = await RunForResultAsync(
            workingDirectory,
            "RepositoryOptional",
            GitCommandKind.Internal,
            cancellationToken,
            null,
            arguments);

        if (result.ExitCode == 0) return result.StandardOutput;
        if (IsExpectedOptionalExitCode(arguments, result.ExitCode)) return string.Empty;
        throw CreateCommandFailure(result);
    }

    internal async Task RunMutationAsync(
        Repository repository,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        _ = await RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            null,
            GitCommandKind.User,
            arguments);

    internal async Task<GitCommandResult> RunForResultAsync(
        string workingDirectory,
        string operation,
        GitCommandKind commandKind,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environment,
        IReadOnlyList<string> arguments)
    {
        try
        {
            return await _executor.ExecuteForResultAsync(
                workingDirectory,
                operation,
                commandKind,
                cancellationToken,
                environment,
                arguments);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RepositoryOpenException(
                $"Git executable '{_executor.ExecutablePath}' could not be started.",
                exception);
        }
    }

    internal static RepositoryOpenException CreateCommandFailure(GitCommandResult result)
    {
        var error = result.StandardError.Trim();
        var detail = string.IsNullOrWhiteSpace(error)
            ? "Git returned no diagnostic message."
            : error;
        return new RepositoryOpenException(
            $"Git command exited with code {result.ExitCode}: {detail}");
    }

    private static bool IsExpectedOptionalExitCode(
        IReadOnlyList<string> arguments,
        int exitCode)
    {
        if (arguments.Count == 0) return false;

        return arguments[0] switch
        {
            "symbolic-ref" => exitCode == 1,
            "rev-parse" => exitCode is 1 or 128,
            "config" => exitCode == 1,
            "show" => exitCode == 128,
            _ => false
        };
    }
}
