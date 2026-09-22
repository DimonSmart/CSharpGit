using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitPushExecutor
{
    private readonly GitRepositoryCommandRunner _runner;

    internal GitPushExecutor(GitRepositoryCommandRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    internal async Task RunAsync(
        Repository repository,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var result = await _runner.RunForResultAsync(
            repository.WorkingDirectory,
            "RepositoryCaptured",
            GitCommandKind.User,
            cancellationToken,
            null,
            arguments);

        if (result.ExitCode == 0) return;

        var detail = JoinGitOutput(result);
        throw new PushRejectedException(
            ClassifyFailure(detail),
            $"Git push failed: {(string.IsNullOrWhiteSpace(detail) ? $"exit code {result.ExitCode}" : detail)}");
    }

    internal static PushResultKind ClassifyFailure(string output)
    {
        var text = output.ToLowerInvariant();

        if (ContainsAny(
                text,
                "authentication failed",
                "could not read username",
                "permission denied",
                "publickey",
                "repository not found",
                "unable to access",
                "could not resolve host",
                "failed to connect",
                "connection timed out",
                "connection reset",
                "network is unreachable",
                "remote end hung up",
                "connection closed"))
            return PushResultKind.AuthenticationOrTransportFailure;

        if (ContainsAny(text, "stale info", "force-with-lease"))
            return PushResultKind.LeaseRejected;

        if (ContainsAny(text, "non-fast-forward", "fetch first"))
            return PushResultKind.NonFastForwardRejected;

        if (ContainsAny(
                text,
                "remote rejected",
                "[remote rejected]",
                "pre-receive hook declined",
                "hook declined",
                "protected branch",
                "deny updating"))
            return PushResultKind.RemoteRejected;

        return PushResultKind.OtherFailure;
    }

    internal static string JoinGitOutput(GitCommandResult result)
    {
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
            return result.StandardError.Trim();

        if (string.IsNullOrWhiteSpace(result.StandardError))
            return result.StandardOutput.Trim();

        return $"{result.StandardOutput.Trim()}\n{result.StandardError.Trim()}";
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.Ordinal));
}
