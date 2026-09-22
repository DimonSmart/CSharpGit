using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed partial class GitReferenceService : IReferenceService
{
    private readonly GitCommandRunner _commands;
    private readonly GitPushRunner _push;

    internal GitReferenceService(
        GitCommandRunner commands,
        GitPushRunner push)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _push = push ?? throw new ArgumentNullException(nameof(push));
    }

    public Task SwitchBranchAsync(
        Repository repository,
        string branch,
        CancellationToken cancellationToken = default)
    {
        GitReferenceValidator.ValidateRefName(branch, nameof(branch));
        return _commands.RunMutationAsync(
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
        GitReferenceValidator.ValidateRefName(reference, nameof(reference));
        return _commands.RunMutationAsync(
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
        GitReferenceValidator.ValidateRefName(branch, nameof(branch));
        if (startPoint is not null)
            GitReferenceValidator.ValidateRefName(
                startPoint,
                nameof(startPoint));

        if (!switchToBranch)
        {
            return _commands.RunMutationAsync(
                repository,
                cancellationToken,
                "branch",
                branch,
                startPoint ?? "HEAD");
        }

        var arguments = new List<string> { "switch", "-c", branch };
        if (startPoint is not null) arguments.Add(startPoint);
        return _commands.RunMutationAsync(
            repository,
            cancellationToken,
            arguments.ToArray());
    }

    public Task DeleteBranchAsync(
        Repository repository,
        string branch,
        CancellationToken cancellationToken = default)
    {
        GitReferenceValidator.ValidateRefName(branch, nameof(branch));
        return _commands.RunMutationAsync(
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
        GitReferenceValidator.ValidateRefName(
            remoteBranch,
            nameof(remoteBranch));
        GitReferenceValidator.ValidateRefName(
            localBranch,
            nameof(localBranch));
        return _commands.RunMutationAsync(
            repository,
            cancellationToken,
            "switch",
            "--create",
            localBranch,
            "--track",
            remoteBranch);
    }
}
