using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed partial class GitCliRepositoryService
{
    public async Task<ForcePushWithLeaseSnapshot> PrepareForcePushWithLeaseAsync(
        Repository repository,
        string? remote = null,
        string? remoteBranch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (DetectOperation(repository) != RepositoryOperation.None)
            throw new InvalidOperationException("Complete or abort the current Git operation before force pushing.");
        if ((remote is null) != (remoteBranch is null))
            throw new ArgumentException("Explicit force-push target requires both remote and remote branch.");

        var localBranch = await CurrentBranchAsync(repository, cancellationToken);
        ValidateRefName(localBranch, nameof(localBranch));
        var localRef = $"refs/heads/{localBranch}";
        var localCommit = await RunGitAsync(repository.WorkingDirectory, cancellationToken, true,
            "rev-parse", "--verify", localRef);
        ValidateObjectName(localCommit);

        if (remote is null)
        {
            remote = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken,
                "config", "--get", $"branch.{localBranch}.remote");
            var mergeRef = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken,
                "config", "--get", $"branch.{localBranch}.merge");
            if (string.IsNullOrWhiteSpace(remote) ||
                string.IsNullOrWhiteSpace(mergeRef) ||
                !mergeRef.StartsWith("refs/heads/", StringComparison.Ordinal) ||
                mergeRef.Length <= "refs/heads/".Length)
            {
                throw new ForcePushWithLeasePreparationException(
                    ForcePushPreparationFailure.MissingUpstream,
                    $"Branch '{localBranch}' has no unambiguous configured upstream. Explicitly select a remote and remote branch.");
            }
            remoteBranch = mergeRef["refs/heads/".Length..];
        }

        remote = remote.Trim();
        remoteBranch = remoteBranch!.Trim();
        ValidateRefName(remote, nameof(remote));
        ValidateRefName(remoteBranch, nameof(remoteBranch));
        await EnsureRemoteExistsAsync(repository, remote, cancellationToken);
        var pushDestination = await ReadSinglePushDestinationAsync(repository, remote, cancellationToken);
        var remoteRef = $"refs/heads/{remoteBranch}";
        var remoteQuery = await RunGitCapturedAsync(repository, cancellationToken,
            "ls-remote", "--exit-code", "--refs", pushDestination, remoteRef);
        if (remoteQuery.ExitCode == 2)
        {
            throw new ForcePushWithLeasePreparationException(
                ForcePushPreparationFailure.RemoteBranchDoesNotExist,
                $"{remote}/{remoteBranch} does not currently exist. Use normal Push to create a new remote branch.");
        }
        if (remoteQuery.ExitCode != 0)
            throw CreateGitFailure("Could not query the force-push destination", remoteQuery);

        var matchingLines = remoteQuery.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length == 2 && string.Equals(fields[1], remoteRef, StringComparison.Ordinal))
            .ToList();
        if (matchingLines.Count != 1)
            throw new RepositoryOpenException("Git ls-remote did not return exactly one matching remote branch ref.");
        var expectedRemoteCommit = matchingLines[0][0];
        ValidateObjectName(expectedRemoteCommit);

        var localSubject = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false,
            "show", "-s", "--format=%s", localCommit);
        string? remoteSubject = null;
        var objectCheck = await RunGitCapturedAsync(repository, cancellationToken,
            "cat-file", "-e", $"{expectedRemoteCommit}^{{commit}}");
        if (objectCheck.ExitCode == 0)
        {
            var subject = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken,
                "show", "-s", "--format=%s", expectedRemoteCommit);
            remoteSubject = string.IsNullOrWhiteSpace(subject) ? null : subject;
        }

        return new ForcePushWithLeaseSnapshot(
            localBranch,
            localCommit,
            remote,
            pushDestination,
            remoteBranch,
            expectedRemoteCommit,
            string.IsNullOrWhiteSpace(localSubject) ? null : localSubject,
            remoteSubject);
    }

    public async Task ForcePushWithLeaseAsync(
        Repository repository,
        ForcePushWithLeaseSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);
        if (DetectOperation(repository) != RepositoryOperation.None)
            throw new ForcePushWithLeaseCancelledException("A Git operation started after confirmation was prepared. Start the force-push workflow again.");

        var currentBranch = await CurrentBranchAsync(repository, cancellationToken);
        if (!string.Equals(currentBranch, snapshot.LocalBranch, StringComparison.Ordinal))
            throw new ForcePushWithLeaseCancelledException("The current branch changed after confirmation was prepared. Start the operation again.");

        var currentLocalCommit = await RunGitAsync(repository.WorkingDirectory, cancellationToken, true,
            "rev-parse", "--verify", $"refs/heads/{snapshot.LocalBranch}");
        if (!string.Equals(currentLocalCommit, snapshot.LocalCommit, StringComparison.Ordinal))
            throw new ForcePushWithLeaseCancelledException($"{snapshot.LocalBranch} changed after confirmation was prepared. Review the new state and start the operation again.");

        string currentPushDestination;
        try
        {
            currentPushDestination = await ReadSinglePushDestinationAsync(repository, snapshot.Remote, cancellationToken);
        }
        catch (ForcePushWithLeasePreparationException exception)
        {
            throw new ForcePushWithLeaseCancelledException($"The push destination changed after confirmation was prepared: {exception.Message}");
        }
        if (!string.Equals(currentPushDestination, snapshot.RemotePushDestination, StringComparison.Ordinal))
            throw new ForcePushWithLeaseCancelledException("The remote push destination changed after confirmation was prepared. Start the operation again.");

        // Do not query the remote OID here. The exact OID in the confirmed immutable
        // snapshot is the lease and Git itself performs the compare-and-swap check.
        await RunPushAsync(repository, cancellationToken, BuildForcePushArguments(snapshot));
    }

    internal static string[] BuildForcePushArguments(ForcePushWithLeaseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);
        return
        [
            "push",
            "--porcelain",
            $"--force-with-lease=refs/heads/{snapshot.RemoteBranch}:{snapshot.ExpectedRemoteCommit}",
            snapshot.Remote,
            $"refs/heads/{snapshot.LocalBranch}:refs/heads/{snapshot.RemoteBranch}"
        ];
    }

    private static void ValidateSnapshot(ForcePushWithLeaseSnapshot snapshot)
    {
        ValidateRefName(snapshot.LocalBranch, nameof(snapshot.LocalBranch));
        ValidateRefName(snapshot.Remote, nameof(snapshot.Remote));
        ValidateRefName(snapshot.RemoteBranch, nameof(snapshot.RemoteBranch));
        ValidateObjectName(snapshot.LocalCommit);
        ValidateObjectName(snapshot.ExpectedRemoteCommit);
        if (string.IsNullOrWhiteSpace(snapshot.RemotePushDestination))
            throw new ArgumentException("The force-push snapshot has no push destination.", nameof(snapshot));
    }

    private async Task EnsureRemoteExistsAsync(Repository repository, string remote, CancellationToken cancellationToken)
    {
        var remotes = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "remote");
        if (!remotes.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(remote, StringComparer.Ordinal))
            throw new ArgumentException($"Remote '{remote}' is not configured.", nameof(remote));
    }

    private async Task<string> ReadSinglePushDestinationAsync(Repository repository, string remote, CancellationToken cancellationToken)
    {
        ValidateRefName(remote, nameof(remote));
        await EnsureRemoteExistsAsync(repository, remote, cancellationToken);
        var result = await RunGitCapturedAsync(repository, cancellationToken,
            "remote", "get-url", "--push", "--all", remote);
        if (result.ExitCode != 0)
            throw CreateGitFailure($"Could not resolve push URL for remote '{remote}'", result);
        var destinations = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (destinations.Length == 0)
            throw new RepositoryOpenException($"Remote '{remote}' has no push destination.");
        if (destinations.Length != 1)
            throw new ForcePushWithLeasePreparationException(
                ForcePushPreparationFailure.MultiplePushDestinations,
                "Force push with lease is not available for remotes with multiple push destinations.");
        return destinations[0];
    }

    private async Task RunPushAsync(Repository repository, CancellationToken cancellationToken, params string[] arguments)
    {
        var result = await RunGitCapturedAsync(repository, cancellationToken, GitCommandKind.User, arguments);
        if (result.ExitCode == 0) return;
        var detail = JoinGitOutput(result);
        throw new PushRejectedException(
            ClassifyPushFailure(detail),
            $"Git push failed: {(string.IsNullOrWhiteSpace(detail) ? $"exit code {result.ExitCode}" : detail)}");
    }

    internal static PushResultKind ClassifyPushFailure(string output)
    {
        var text = output.ToLowerInvariant();

        // Authentication/transport signals have priority. Git commonly prefixes
        // diagnostics with "remote:", which must not turn auth failures into a
        // generic remote-policy rejection.
        if (ContainsAny(text,
                "authentication failed", "could not read username", "permission denied",
                "publickey", "repository not found", "unable to access", "could not resolve host",
                "failed to connect", "connection timed out", "connection reset", "network is unreachable",
                "remote end hung up", "connection closed"))
            return PushResultKind.AuthenticationOrTransportFailure;

        if (ContainsAny(text, "stale info", "force-with-lease"))
            return PushResultKind.LeaseRejected;

        if (ContainsAny(text, "non-fast-forward", "fetch first"))
            return PushResultKind.NonFastForwardRejected;

        if (ContainsAny(text,
                "remote rejected", "[remote rejected]", "pre-receive hook declined",
                "hook declined", "protected branch", "deny updating"))
            return PushResultKind.RemoteRejected;

        return PushResultKind.OtherFailure;
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.Ordinal));

    private static RepositoryOpenException CreateGitFailure(string prefix, GitCommandResult result)
    {
        var detail = JoinGitOutput(result);
        return new RepositoryOpenException(
            $"{prefix}: {(string.IsNullOrWhiteSpace(detail) ? $"Git exited with code {result.ExitCode}." : detail)}");
    }

    private static string JoinGitOutput(GitCommandResult result)
    {
        if (string.IsNullOrWhiteSpace(result.StandardOutput)) return result.StandardError.Trim();
        if (string.IsNullOrWhiteSpace(result.StandardError)) return result.StandardOutput.Trim();
        return $"{result.StandardOutput.Trim()}\n{result.StandardError.Trim()}";
    }

    private Task<GitCommandResult> RunGitCapturedAsync(
        Repository repository,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        RunGitCapturedAsync(repository, cancellationToken, GitCommandKind.Internal, arguments);

    private async Task<GitCommandResult> RunGitCapturedAsync(
        Repository repository,
        CancellationToken cancellationToken,
        GitCommandKind commandKind,
        params string[] arguments)
    {
        try
        {
            var result = await SharedProcessRunner.RunForResultAsync(
                repository.WorkingDirectory,
                "RepositoryCaptured",
                commandKind,
                cancellationToken,
                null,
                arguments);
            return new GitCommandResult(result.ExitCode, result.StandardOutput, result.StandardError);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RepositoryOpenException($"Git executable '{_gitExecutable}' could not be started.", exception);
        }
    }

    private sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
}
