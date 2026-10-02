using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitWorkingTreeStatusReader(GitRepositoryCommandRunner runner) : IWorkingTreeStatusReader
{
    private readonly GitRepositoryCommandRunner _runner =
        runner ?? throw new ArgumentNullException(nameof(runner));

    public async Task<WorkingTreeStatusSnapshot> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        await _runner.EnsureGitAvailableAsync(cancellationToken);

        var result = await _runner.RunForResultAsync(
            repository.WorkingDirectory,
            "WorkingTreeChangeProbe",
            GitCommandKind.Internal,
            cancellationToken,
            GitRepositoryStateService.ReadOnlyEnvironment,
            ["status", "--porcelain=v2", "-z", "--untracked-files=all"]);

        if (result.ExitCode != 0)
            throw GitRepositoryCommandRunner.CreateCommandFailure(result);

        return GitRepositoryStateService.ParseStatusV2(result.StandardOutput).WorkingTreeStatus;
    }
}
