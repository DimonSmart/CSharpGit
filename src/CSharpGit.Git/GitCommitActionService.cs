using System.Globalization;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed partial class GitCommitActionService : ICommitActionService
{
    private readonly GitCommandRunner _commands;
    private readonly GitOperationDetector _operationDetector;
    private readonly GitRepositoryStateService _stateService;
    private readonly GitRepositoryWorkflowService _workflowService;

    internal GitCommitActionService(
        GitCommandRunner commands,
        GitOperationDetector operationDetector,
        GitRepositoryStateService stateService,
        GitRepositoryWorkflowService workflowService)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _operationDetector = operationDetector ?? throw new ArgumentNullException(nameof(operationDetector));
        _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
    }

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
        GitReferenceValidator.ValidateObjectName(commit);

        var state = await _stateService.ReadAsync(repository, cancellationToken);
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

        await _commands.RunMutationAsync(repository, cancellationToken, "reset", flag, commit);
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
        GitReferenceValidator.ValidateObjectName(commit);
        if (mainlineParent is <= 0)
            throw new ArgumentOutOfRangeException(nameof(mainlineParent), "Mainline parent numbers start at 1.");
        if (_operationDetector.Detect(repository) != RepositoryOperation.None)
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
            await _commands.RunMutationAsync(repository, cancellationToken, arguments.ToArray());
            var head = await _commands.RunOptionalAsync(repository.WorkingDirectory, cancellationToken, "rev-parse", "--verify", "HEAD");
            return new ApplyCommitResult(ApplyCommitResultKind.Completed, $"{command} completed successfully.", head);
        }
        catch (RepositoryOpenException exception)
        {
            var state = await _stateService.ReadAsync(repository, cancellationToken);
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
