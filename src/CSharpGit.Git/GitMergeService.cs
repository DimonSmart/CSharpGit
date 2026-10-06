using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitMergeService : IMergeService
{
    private readonly GitRepositoryCommandRunner _runner;
    private readonly GitRepositoryStateService _stateService;

    internal GitMergeService(
        GitRepositoryCommandRunner runner,
        GitRepositoryStateService stateService)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
    }

    public async Task<MergeResult> MergeAsync(
        Repository repository,
        string branch,
        CancellationToken cancellationToken = default)
    {
        GitRefValidator.Validate(branch, nameof(branch));

        var before = await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD");

        try
        {
            await _runner.RunMutationAsync(
                repository,
                cancellationToken,
                "merge",
                "--no-edit",
                branch);
        }
        catch (RepositoryOpenException exception)
        {
            var state = await _stateService.ReadAsync(
                repository,
                cancellationToken);
            var conflicts =
                state.Operation == RepositoryOperation.Merge
                || state.Changes.Any(change => change.IsConflicted);

            return new MergeResult(
                conflicts
                    ? MergeResultKind.Conflicts
                    : MergeResultKind.Refused,
                exception.Message);
        }

        var after = await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD");

        if (string.Equals(before, after, StringComparison.Ordinal))
            return new MergeResult(
                MergeResultKind.UpToDate,
                "Git: already up to date.");

        var parents = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            true,
            "show",
            "-s",
            "--format=%P",
            "HEAD");

        return parents.Split(
                   ' ',
                   StringSplitOptions.RemoveEmptyEntries).Length > 1
            ? new MergeResult(
                MergeResultKind.MergeCommit,
                "Git created a merge commit.")
            : new MergeResult(
                MergeResultKind.FastForward,
                "Git completed a fast-forward merge.");
    }
}
