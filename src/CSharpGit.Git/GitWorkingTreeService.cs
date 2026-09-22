using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed partial class GitWorkingTreeService : IWorkingTreeService
{
    private readonly GitCommandRunner _commands;

    internal GitWorkingTreeService(GitCommandRunner commands)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public Task StageFileAsync(
        Repository repository,
        WorkingTreeChange change,
        CancellationToken cancellationToken = default)
    {
        GitPathValidator.ValidateChange(change);
        return _commands.RunMutationAsync(
            repository,
            cancellationToken,
            GitPathValidator.PathArguments("add", change));
    }

    public Task StageAllAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        _commands.RunMutationAsync(
            repository,
            cancellationToken,
            "add",
            "--all");

    public async Task UnstageFileAsync(
        Repository repository,
        WorkingTreeChange change,
        CancellationToken cancellationToken = default)
    {
        GitPathValidator.ValidateChange(change);
        if (await _commands.RunOptionalAsync(
                repository.WorkingDirectory,
                cancellationToken,
                "rev-parse",
                "--verify",
                "HEAD") is { Length: > 0 })
        {
            await _commands.RunMutationAsync(
                repository,
                cancellationToken,
                GitPathValidator.PathArguments(
                    "restore",
                    change,
                    "--staged"));
        }
        else
        {
            await _commands.RunMutationAsync(
                repository,
                cancellationToken,
                GitPathValidator.PathArguments(
                    "rm",
                    change,
                    "--cached",
                    "--ignore-unmatch"));
        }
    }

    public async Task DiscardFileAsync(
        Repository repository,
        WorkingTreeChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(change);
        GitPathValidator.ValidateChange(change);
        if (!change.IsUnstaged)
            throw new InvalidOperationException(
                "Only unstaged working-tree changes can be discarded.");

        cancellationToken.ThrowIfCancellationRequested();
        if (change.IndexStatus == '?')
        {
            var fullPath = GitPathValidator.ResolveSafeWorkingTreePath(
                repository,
                change.Path);
            if (File.Exists(fullPath)) File.Delete(fullPath);
            return;
        }

        if (change.WorkingTreeStatus == 'R'
            && change.OriginalPath is not null)
        {
            var renamedPath = GitPathValidator.ResolveSafeWorkingTreePath(
                repository,
                change.Path);
            if (File.Exists(renamedPath)) File.Delete(renamedPath);
            await _commands.RunMutationAsync(
                repository,
                cancellationToken,
                "restore",
                "--worktree",
                "--",
                change.OriginalPath);
            return;
        }

        await _commands.RunMutationAsync(
            repository,
            cancellationToken,
            "restore",
            "--worktree",
            "--",
            change.Path);
    }

    public async Task DiscardAllFileChangesAsync(
        Repository repository,
        WorkingTreeChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(change);
        GitPathValidator.ValidateChange(change);
        if (!change.IsStaged)
            throw new InvalidOperationException(
                "Only staged changes can be discarded from the index.");
        if (change.IsConflicted)
            throw new InvalidOperationException(
                "Conflict paths must be handled by the conflict workflow.");

        cancellationToken.ThrowIfCancellationRequested();
        if (await _commands.RunOptionalAsync(
                repository.WorkingDirectory,
                cancellationToken,
                "rev-parse",
                "--verify",
                "HEAD") is { Length: > 0 })
        {
            await _commands.RunMutationAsync(
                repository,
                cancellationToken,
                GitPathValidator.PathArguments(
                    "restore",
                    change,
                    "--source=HEAD",
                    "--staged",
                    "--worktree"));
            return;
        }

        await _commands.RunMutationAsync(
            repository,
            cancellationToken,
            GitPathValidator.PathArguments(
                "rm",
                change,
                "--force",
                "--ignore-unmatch"));
    }

    public async Task CommitAsync(
        Repository repository,
        string message,
        bool amend = false,
        bool intentionalEmpty = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException(
                "Enter a non-empty commit message.",
                nameof(message));

        var staged = await _commands.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "diff",
            "--cached",
            "--name-only",
            "-z");
        if (staged.Length == 0 && !intentionalEmpty && !amend)
            throw new InvalidOperationException(
                "The index is empty. Stage files or choose an intentional empty commit.");

        var arguments = new List<string> { "commit", "-m", message };
        if (amend) arguments.Add("--amend");
        if (intentionalEmpty) arguments.Add("--allow-empty");
        await _commands.RunMutationAsync(
            repository,
            cancellationToken,
            arguments.ToArray());
    }
}
