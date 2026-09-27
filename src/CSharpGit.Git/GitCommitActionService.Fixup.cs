using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed partial class GitCommitActionService
{
    public async Task<RebaseResult> FixupIntoPreviousCommitAsync(
        Repository repository,
        string commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        GitRefValidator.ValidateObjectId(commit);

        if (GitOperationDetector.Detect(repository) != RepositoryOperation.None)
            throw new InvalidOperationException(
                "Complete or abort the current Git operation before rewriting history.");

        var resolvedCommit = (await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-parse",
            "--verify",
            $"{commit}^{{commit}}")).Trim();
        if (resolvedCommit.Length == 0)
            return FailedFixup("The selected commit no longer exists.");

        var state = await _stateService.ReadAsync(repository, cancellationToken);
        if (state.IsDetached ||
            string.IsNullOrWhiteSpace(state.HeadReference) ||
            !state.Refs.LocalBranches.Any(branch => branch.IsCurrent))
            return FailedFixup(
                "Fixup is available only when HEAD is attached to a local branch.");
        if (string.IsNullOrWhiteSpace(state.HeadCommit))
            return FailedFixup("The repository does not have a current HEAD commit.");

        var ancestorCheck = await _runner.RunForResultAsync(
            repository.WorkingDirectory,
            "FixupCommitAncestorCheck",
            GitCommandKind.Internal,
            cancellationToken,
            null,
            ["merge-base", "--is-ancestor", resolvedCommit, state.HeadCommit]);
        if (ancestorCheck.ExitCode == 1)
            return FailedFixup(
                "The selected commit is not part of the current branch history.");
        if (ancestorCheck.ExitCode != 0)
            throw GitRepositoryCommandRunner.CreateCommandFailure(ancestorCheck);

        var parents = await ReadParentsAsync(
            repository,
            resolvedCommit,
            cancellationToken);
        if (parents.Length == 0)
            return FailedFixup(
                "The root commit has no previous commit to fix up into.");
        if (parents.Length != 1)
            return FailedFixup(
                "Fixup into previous commit supports linear history only. The selected commit is a merge commit.");

        var previousCommit = parents[0];
        var previousParents = await ReadParentsAsync(
            repository,
            previousCommit,
            cancellationToken);
        if (previousParents.Length == 0)
            return FailedFixup(
                "Fixup into the root commit is not supported yet.");
        if (previousParents.Length != 1)
            return FailedFixup(
                "Fixup into previous commit supports linear history only. The previous commit is a merge commit.");

        var plan = await _workflowService.ReadInteractiveRebasePlanFromCommitAsync(
            repository,
            previousCommit,
            cancellationToken);

        var targetIndex = -1;
        for (var index = 0; index < plan.Items.Count; index++)
        {
            if (!string.Equals(
                    plan.Items[index].Commit,
                    resolvedCommit,
                    StringComparison.Ordinal))
                continue;

            targetIndex = index;
            break;
        }

        if (targetIndex <= 0 ||
            !string.Equals(
                plan.Items[targetIndex - 1].Commit,
                previousCommit,
                StringComparison.Ordinal))
            return FailedFixup(
                "The selected commit is no longer immediately after its expected previous commit.");

        var items = plan.Items
            .Select(item => item with
            {
                Action = string.Equals(
                    item.Commit,
                    resolvedCommit,
                    StringComparison.Ordinal)
                    ? RebaseAction.Fixup
                    : RebaseAction.Pick,
                NewMessage = null
            })
            .ToArray();

        return await _workflowService.StartInteractiveRebaseAsync(
            repository,
            plan with { Items = items },
            cancellationToken);
    }

    private async Task<string[]> ReadParentsAsync(
        Repository repository,
        string commit,
        CancellationToken cancellationToken)
    {
        var output = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "show",
            "-s",
            "--format=%P",
            commit);
        return output.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static RebaseResult FailedFixup(string message) =>
        new(RebaseResultKind.Failed, message);
}
