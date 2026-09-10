using System.Globalization;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed partial class GitCliRepositoryService
{
    public Task<ApplyCommitResult> CherryPickAsync(
        Repository repository,
        string commit,
        int? mainlineParent = null,
        CancellationToken cancellationToken = default) =>
        ApplyCommitAsync(repository, commit, "cherry-pick", mainlineParent, noEdit: false, cancellationToken);

    public Task<ApplyCommitResult> RevertAsync(
        Repository repository,
        string commit,
        int? mainlineParent = null,
        CancellationToken cancellationToken = default) =>
        ApplyCommitAsync(repository, commit, "revert", mainlineParent, noEdit: true, cancellationToken);

    public async Task ResetAsync(
        Repository repository,
        string commit,
        ResetMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(commit);

        var state = await ReadAsync(repository, cancellationToken);
        if (state.Operation != RepositoryOperation.None)
            throw new InvalidOperationException("Complete or abort the current Git operation before resetting.");
        if (state.IsDetached || string.IsNullOrWhiteSpace(state.HeadReference) ||
            !state.Refs.LocalBranches.Any(branch => branch.IsCurrent))
            throw new InvalidOperationException("Reset is available only when HEAD is attached to a local branch.");

        var flag = mode switch
        {
            ResetMode.Soft => "--soft",
            ResetMode.Mixed => "--mixed",
            ResetMode.Hard => "--hard",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        await RunGitForMutationAsync(repository, cancellationToken, "reset", flag, commit);
    }

    private async Task<ApplyCommitResult> ApplyCommitAsync(
        Repository repository,
        string commit,
        string command,
        int? mainlineParent,
        bool noEdit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(commit);
        if (mainlineParent is <= 0)
            throw new ArgumentOutOfRangeException(nameof(mainlineParent), "Mainline parent numbers start at 1.");
        if (DetectOperation(repository) != RepositoryOperation.None)
            throw new InvalidOperationException("Complete or abort the current Git operation first.");

        var arguments = new List<string> { command };
        if (noEdit) arguments.Add("--no-edit");
        if (mainlineParent is { } parent)
        {
            arguments.Add("-m");
            arguments.Add(parent.ToString(CultureInfo.InvariantCulture));
        }
        arguments.Add(commit);

        try
        {
            await RunGitForMutationAsync(repository, cancellationToken, arguments.ToArray());
            var head = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken, "rev-parse", "--verify", "HEAD");
            return new ApplyCommitResult(ApplyCommitResultKind.Completed, $"{command} completed successfully.", head);
        }
        catch (RepositoryOpenException exception)
        {
            var state = await ReadAsync(repository, cancellationToken);
            var expectedOperation = command == "cherry-pick"
                ? RepositoryOperation.CherryPick
                : RepositoryOperation.Revert;
            var conflicts = state.Operation == expectedOperation &&
                            state.CurrentOperation.Conflicts.Any(conflict => !conflict.IsResolved);
            return new ApplyCommitResult(
                conflicts ? ApplyCommitResultKind.Conflicts : ApplyCommitResultKind.Failed,
                exception.Message,
                state.HeadCommit);
        }
    }
}
