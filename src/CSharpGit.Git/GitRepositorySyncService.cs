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

    public async Task DeleteRemoteBranchAsync(
        Repository repository,
        string remote,
        string branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        GitRefValidator.Validate(remote, nameof(remote));
        GitRefValidator.Validate(branch, nameof(branch));
        await _pushExecutor.RunAsync(
            repository,
            cancellationToken,
            "push",
            "--porcelain",
            remote,
            "--delete",
            branch);
    }

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
                $"Branch '{branch}' has no configured upstream. Publish the branch or configure an upstream before pulling.");

        await _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "pull");
    }

    public Task PushAsync(
        Repository repository,
        PushOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return _pushExecutor.RunAsync(
            repository,
            cancellationToken,
            options?.AutoSetupRemote == true
                ? [
                    "-c",
                    "push.autoSetupRemote=true",
                    "push",
                    "--porcelain"
                ]
                : [
                    "push",
                    "--porcelain"
                ]);
    }

    public async Task<PublishBranchPreparation> PreparePublishBranchAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var localBranch = await CurrentBranchAsync(repository, cancellationToken);
        var remotes = await ReadRemoteNamesAsync(repository, cancellationToken);

        var candidates = new[]
        {
            await ReadConfigAsync(repository, $"branch.{localBranch}.pushRemote", cancellationToken),
            await ReadConfigAsync(repository, "remote.pushDefault", cancellationToken),
            await ReadConfigAsync(repository, $"branch.{localBranch}.remote", cancellationToken)
        };

        var suggested = candidates.FirstOrDefault(candidate =>
            candidate is not null && remotes.Contains(candidate, StringComparer.Ordinal));

        if (suggested is null && remotes.Contains("origin", StringComparer.Ordinal))
            suggested = "origin";
        if (suggested is null && remotes.Count == 1)
            suggested = remotes[0];

        return new PublishBranchPreparation(localBranch, suggested);
    }

    public async Task PublishBranchAsync(
        Repository repository,
        PublishBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);

        var localBranch = await CurrentBranchAsync(repository, cancellationToken);
        var remote = request.Remote.Trim();
        var remoteBranch = request.RemoteBranch.Trim();
        GitRefValidator.Validate(remote, nameof(request.Remote));
        GitRefValidator.Validate(remoteBranch, nameof(request.RemoteBranch));
        await EnsureRemoteExistsAsync(repository, remote, cancellationToken);

        var refspec = $"refs/heads/{localBranch}:refs/heads/{remoteBranch}";
        await _pushExecutor.RunAsync(
            repository,
            cancellationToken,
            request.SetUpstream
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

    private async Task<IReadOnlyList<string>> ReadRemoteNamesAsync(
        Repository repository,
        CancellationToken cancellationToken)
    {
        var output = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "remote");

        return output.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<string?> ReadConfigAsync(
        Repository repository,
        string key,
        CancellationToken cancellationToken)
    {
        var value = await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "config",
            "--get",
            key);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

        return branch.Trim();
    }
}
