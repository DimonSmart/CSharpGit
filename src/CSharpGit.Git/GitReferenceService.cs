using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed partial class GitReferenceService : IReferenceService
{
    private readonly GitRepositoryCommandRunner _runner;
    private readonly GitPushExecutor _pushExecutor;

    internal GitReferenceService(GitCommandExecutor executor)
        : this(new GitRepositoryCommandRunner(executor))
    {
    }

    internal GitReferenceService(GitRepositoryCommandRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _pushExecutor = new GitPushExecutor(_runner);
    }

    public Task SwitchBranchAsync(
        Repository repository,
        string branch,
        CancellationToken cancellationToken = default)
    {
        GitRefValidator.Validate(branch, nameof(branch));
        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "switch",
            branch);
    }

    public Task CheckoutAsync(
        Repository repository,
        string reference,
        CancellationToken cancellationToken = default)
    {
        GitRefValidator.Validate(reference, nameof(reference));
        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "checkout",
            "--detach",
            reference);
    }

    public Task CreateBranchAsync(
        Repository repository,
        string branch,
        string? startPoint = null,
        bool switchToBranch = true,
        CancellationToken cancellationToken = default)
    {
        GitRefValidator.Validate(branch, nameof(branch));
        if (startPoint is not null)
            GitRefValidator.Validate(startPoint, nameof(startPoint));

        var arguments = new List<string>
        {
            "switch",
            switchToBranch ? "-c" : "--no-track"
        };

        if (!switchToBranch)
        {
            return _runner.RunMutationAsync(
                repository,
                cancellationToken,
                "branch",
                branch,
                startPoint ?? "HEAD");
        }

        arguments.Add(branch);
        if (startPoint is not null) arguments.Add(startPoint);

        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            arguments.ToArray());
    }

    public Task DeleteBranchAsync(
        Repository repository,
        string branch,
        CancellationToken cancellationToken = default)
    {
        GitRefValidator.Validate(branch, nameof(branch));
        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "branch",
            "--delete",
            branch);
    }

    public Task CheckoutRemoteBranchAsync(
        Repository repository,
        string remoteBranch,
        string localBranch,
        CancellationToken cancellationToken = default)
    {
        GitRefValidator.Validate(remoteBranch, nameof(remoteBranch));
        GitRefValidator.Validate(localBranch, nameof(localBranch));

        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "switch",
            "--create",
            localBranch,
            "--track",
            remoteBranch);
    }
}
