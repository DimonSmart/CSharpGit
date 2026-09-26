using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed partial class GitCommitActionService
{
    private const string DirtyEditCommitMessage =
        "Commit message cannot be edited while the working tree contains changes.\n\n" +
        "Commit, stash, or discard the changes first.";

    public async Task<EditCommitMessageResult> EditCommitMessageAsync(
        Repository repository,
        string commit,
        string newMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        GitRefValidator.ValidateObjectId(commit);
        if (string.IsNullOrWhiteSpace(newMessage))
            throw new ArgumentException("Enter a non-empty commit message.", nameof(newMessage));

        var resolvedCommit = (await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-parse",
            "--verify",
            $"{commit}^{{commit}}")).Trim();

        if (resolvedCommit.Length == 0)
            return Failed("The selected commit no longer exists.", commit);

        var state = await _stateService.ReadAsync(repository, cancellationToken);
        if (ValidateEditCommitMessageState(state, resolvedCommit) is { } stateFailure)
            return stateFailure;

        var oldHead = state.HeadCommit!;
        var oldHeadReference = state.HeadReference!;

        if (string.Equals(resolvedCommit, oldHead, StringComparison.Ordinal))
            return await EditHeadCommitMessageAsync(
                repository,
                resolvedCommit,
                oldHeadReference,
                newMessage,
                cancellationToken);

        var parentsOutput = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "show",
            "-s",
            "--format=%P",
            resolvedCommit);
        var parents = parentsOutput.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parents.Length == 0)
            return Failed(
                "Editing the root commit message is not supported by the simple editor.",
                resolvedCommit);

        if (parents.Length != 1)
            return Failed(
                "Editing merge commit messages is not supported by the simple editor.\n\n" +
                "Use Interactive Rebase for advanced history editing.",
                resolvedCommit);

        var ancestorCheck = await _runner.RunForResultAsync(
            repository.WorkingDirectory,
            "EditCommitMessageAncestorCheck",
            GitCommandKind.Internal,
            cancellationToken,
            null,
            ["merge-base", "--is-ancestor", resolvedCommit, oldHead]);

        if (ancestorCheck.ExitCode == 1)
            return Failed(
                "This commit is not part of the current branch history.\n\n" +
                "Switch to the branch containing this commit before editing it.",
                resolvedCommit);
        if (ancestorCheck.ExitCode != 0)
            throw GitRepositoryCommandRunner.CreateCommandFailure(ancestorCheck);

        var parent = parents[0];
        var mergeCommits = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "rev-list",
            "--min-parents=2",
            $"{parent}..{oldHead}");
        if (!string.IsNullOrWhiteSpace(mergeCommits))
            return Failed(
                "This commit cannot be edited with the simple editor because the history " +
                "between this commit and HEAD contains merges.\n\n" +
                "Use Interactive Rebase for advanced history editing.",
                resolvedCommit);

        var existingPlan = await _workflowService.ReadInteractiveRebasePlanAsync(repository, parent, cancellationToken);
        var targetIndex = -1;
        for (var index = 0; index < existingPlan.Items.Count; index++)
        {
            if (!string.Equals(existingPlan.Items[index].Commit, resolvedCommit, StringComparison.Ordinal))
                continue;
            targetIndex = index;
            break;
        }

        if (targetIndex < 0)
            return Failed("The selected commit is no longer part of the rebase range.", resolvedCommit);

        var items = existingPlan.Items
            .Select(item => string.Equals(item.Commit, resolvedCommit, StringComparison.Ordinal)
                ? item with { Action = RebaseAction.Reword, NewMessage = newMessage }
                : item with { Action = RebaseAction.Pick, NewMessage = null })
            .ToArray();

        var beforeMutation = await _stateService.ReadAsync(repository, cancellationToken);
        if (ValidateEditCommitMessageState(
                beforeMutation,
                resolvedCommit,
                oldHead,
                oldHeadReference) is { } mutationFailure)
            return mutationFailure;

        var rebaseResult = await _workflowService.StartInteractiveRebaseAsync(
            repository,
            existingPlan with { Items = items },
            cancellationToken);

        var newHead = NullIfEmpty(await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD"));

        if (rebaseResult.Kind == RebaseResultKind.Completed)
        {
            var commitsAfterTarget = items.Length - targetIndex - 1;
            var rewrittenTarget = NullIfEmpty(await _runner.RunOptionalAsync(
                repository.WorkingDirectory,
                cancellationToken,
                "rev-parse",
                "--verify",
                commitsAfterTarget == 0 ? "HEAD" : $"HEAD~{commitsAfterTarget}"));

            return new EditCommitMessageResult(
                EditCommitMessageResultKind.Completed,
                "Commit message changed successfully.",
                resolvedCommit,
                rewrittenTarget,
                newHead);
        }

        if (rebaseResult.Kind == RebaseResultKind.Conflicts)
            return new EditCommitMessageResult(
                EditCommitMessageResultKind.Conflicts,
                rebaseResult.Message,
                resolvedCommit,
                ReadRewordMarker(repository, 0),
                newHead);

        return new EditCommitMessageResult(
            EditCommitMessageResultKind.Failed,
            rebaseResult.Message,
            resolvedCommit,
            null,
            newHead);
    }

    private async Task<EditCommitMessageResult> EditHeadCommitMessageAsync(
        Repository repository,
        string oldCommit,
        string oldHeadReference,
        string newMessage,
        CancellationToken cancellationToken)
    {
        var beforeMutation = await _stateService.ReadAsync(repository, cancellationToken);
        if (ValidateEditCommitMessageState(
                beforeMutation,
                oldCommit,
                oldCommit,
                oldHeadReference) is { } failure)
            return failure;

        var messagePath = Path.Combine(
            repository.GitDirectory,
            $"csharpgit-edit-message-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(messagePath, newMessage, cancellationToken);
            try
            {
                await _runner.RunMutationAsync(
                    repository,
                    cancellationToken,
                    "commit",
                    "--amend",
                    "--only",
                    "--no-verify",
                    "--no-gpg-sign",
                    "-F",
                    messagePath);
            }
            catch (CSharpGit.Application.Exceptions.RepositoryOpenException exception)
            {
                return new EditCommitMessageResult(
                    EditCommitMessageResultKind.Failed,
                    exception.Message,
                    oldCommit,
                    null,
                    NullIfEmpty(await _runner.RunOptionalAsync(
                        repository.WorkingDirectory,
                        cancellationToken,
                        "rev-parse",
                        "--verify",
                        "HEAD")));
            }

            var newHead = (await _runner.RunAsync(
                repository.WorkingDirectory,
                cancellationToken,
                true,
                "rev-parse",
                "--verify",
                "HEAD")).Trim();

            return new EditCommitMessageResult(
                EditCommitMessageResultKind.Completed,
                "Commit message changed successfully.",
                oldCommit,
                newHead,
                newHead);
        }
        finally
        {
            try { File.Delete(messagePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static EditCommitMessageResult? ValidateEditCommitMessageState(
        RepositoryState state,
        string oldCommit,
        string? expectedHead = null,
        string? expectedHeadReference = null)
    {
        if (state.Operation != RepositoryOperation.None)
            return Failed(
                "Complete or abort the current Git operation before editing a commit message.",
                oldCommit);

        if (state.IsDetached ||
            string.IsNullOrWhiteSpace(state.HeadReference) ||
            !state.Refs.LocalBranches.Any(branch => branch.IsCurrent))
            return Failed(
                "Commit messages can be edited only when HEAD is attached to a local branch.",
                oldCommit);

        if (state.Changes.Count != 0)
            return Failed(DirtyEditCommitMessage, oldCommit);

        if (string.IsNullOrWhiteSpace(state.HeadCommit))
            return Failed("The repository does not have a current HEAD commit.", oldCommit);

        if (expectedHead is not null &&
            !string.Equals(state.HeadCommit, expectedHead, StringComparison.Ordinal))
            return Failed(
                "Repository history changed while the operation was being prepared. Try again.",
                oldCommit);

        if (expectedHeadReference is not null &&
            !string.Equals(state.HeadReference, expectedHeadReference, StringComparison.Ordinal))
            return Failed(
                "The current branch changed while the operation was being prepared. Try again.",
                oldCommit);

        return null;
    }

    private static EditCommitMessageResult Failed(string message, string oldCommit) =>
        new(EditCommitMessageResultKind.Failed, message, oldCommit);

    private static string? NullIfEmpty(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? ReadRewordMarker(Repository repository, int index)
    {
        var path = Path.Combine(
            repository.GitDirectory,
            "csharpgit-rebase",
            $"reword-{index}.sha");
        if (!File.Exists(path)) return null;
        var value = File.ReadAllText(path).Trim();
        return value.Length == 0 ? null : value;
    }

    private static void CleanupRebaseSupportDirectory(Repository repository)
    {
        var path = Path.Combine(repository.GitDirectory, "csharpgit-rebase");
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
