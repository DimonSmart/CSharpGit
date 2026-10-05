using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitConflictResolutionService : IConflictResolutionService
{
    private readonly GitRepositoryCommandRunner _runner;

    internal GitConflictResolutionService(GitRepositoryCommandRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public Task ChooseConflictSideAsync(
        Repository repository,
        ConflictFile conflict,
        ConflictResolutionSide side,
        CancellationToken cancellationToken = default)
    {
        ValidateConflictAction(
            conflict,
            side == ConflictResolutionSide.CurrentLocal
                ? conflict.CanChooseCurrentLocal
                : conflict.CanChooseIncomingRemote);

        var operation = GitOperationDetector.Detect(repository);
        var gitSide = side == ConflictResolutionSide.CurrentLocal
            ? operation == RepositoryOperation.Rebase
                ? "--theirs"
                : "--ours"
            : operation == RepositoryOperation.Rebase
                ? "--ours"
                : "--theirs";

        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "checkout",
            gitSide,
            "--",
            conflict.Path);
    }

    public Task KeepConflictDeletionAsync(
        Repository repository,
        ConflictFile conflict,
        CancellationToken cancellationToken = default)
    {
        ValidateConflictAction(conflict, conflict.CanKeepDeletion);
        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "rm",
            "--",
            conflict.Path);
    }

    public Task StageResolvedConflictAsync(
        Repository repository,
        ConflictFile conflict,
        CancellationToken cancellationToken = default)
    {
        ValidateConflictAction(conflict, conflict.CanStage);
        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "add",
            "--",
            conflict.Path);
    }

    private static void ValidateConflictAction(
        ConflictFile conflict,
        bool allowed)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        GitPathValidator.ValidateRepositoryRelative(conflict.Path);

        if (!allowed)
            throw new InvalidOperationException(
                "This action is unavailable for the selected conflict type or state.");
    }
}
