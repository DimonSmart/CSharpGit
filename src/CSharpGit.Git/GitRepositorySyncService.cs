using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed partial class GitRepositorySyncService : IRepositorySyncService
{
    private readonly GitRepositoryCommandRunner _runner;
    private readonly GitPushExecutor _pushExecutor;

    internal GitRepositorySyncService(GitCommandExecutor executor)
        : this(new GitRepositoryCommandRunner(executor))
    {
    }

    internal GitRepositorySyncService(GitRepositoryCommandRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _pushExecutor = new GitPushExecutor(_runner);
    }

    public Task FetchAsync(
        Repository repository,
        string remote,
        CancellationToken cancellationToken = default)
    {
        GitRefValidator.Validate(remote, nameof(remote));
        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "fetch",
            remote);
    }

    public Task FetchAllAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "fetch",
            "--all");

    public async Task PullAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        var branch = await CurrentBranchAsync(
            repository,
            cancellationToken);
        var upstream = await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-parse",
            "--abbrev-ref",
            "--symbolic-full-name",
            "@{upstream}");

        if (string.IsNullOrWhiteSpace(upstream))
            throw new InvalidOperationException(
                $"Branch '{branch}' has no configured upstream. Explicitly select a remote and branch for push, then optionally set the upstream.");

        await _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "pull");
    }

    public async Task PushAsync(
        Repository repository,
        string? remote = null,
        string? branch = null,
        bool setUpstream = false,
        CancellationToken cancellationToken = default)
    {
        var current = await CurrentBranchAsync(
            repository,
            cancellationToken);
        var upstream = await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-parse",
            "--abbrev-ref",
            "--symbolic-full-name",
            "@{upstream}");

        if (!string.IsNullOrWhiteSpace(upstream)
            && remote is null
            && branch is null)
        {
            await _pushExecutor.RunAsync(
                repository,
                cancellationToken,
                "push",
                "--porcelain");
            return;
        }

        if (string.IsNullOrWhiteSpace(remote)
            || string.IsNullOrWhiteSpace(branch))
            throw new InvalidOperationException(
                "No upstream is configured. Explicitly select a remote and remote branch name; Git GUI does not select them automatically.");

        GitRefValidator.Validate(remote, nameof(remote));
        GitRefValidator.Validate(branch, nameof(branch));
        var refspec = $"{current}:refs/heads/{branch}";

        await _pushExecutor.RunAsync(
            repository,
            cancellationToken,
            setUpstream
                ? [
                    "push",
                    "--porcelain",
                    "--set-upstream",
                    remote,
                    refspec
                ]
                : [
                    "push",
                    "--porcelain",
                    remote,
                    refspec
                ]);
    }

    private async Task<string> CurrentBranchAsync(
        Repository repository,
        CancellationToken cancellationToken)
    {
        var branch = await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "symbolic-ref",
            "--quiet",
            "--short",
            "HEAD");

        if (string.IsNullOrWhiteSpace(branch))
            throw new InvalidOperationException(
                "HEAD is detached. This operation requires a current local branch.");

        return branch;
    }
}
