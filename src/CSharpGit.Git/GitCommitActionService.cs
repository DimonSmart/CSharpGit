using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git;

internal sealed partial class GitCommitActionService : ICommitActionService
{
    private readonly GitRepositoryCommandRunner _runner;
    private readonly GitRepositoryStateService _stateService;
    private readonly IInteractiveRebaseService _interactiveRebaseService;

    internal GitCommitActionService(GitCommandExecutor executor)
        : this(
            new GitRepositoryCommandRunner(executor),
            new GitRepositoryStateService(executor),
            null)
    {
    }

    internal GitCommitActionService(
        GitRepositoryCommandRunner runner,
        GitRepositoryStateService stateService,
        IInteractiveRebaseService? interactiveRebaseService)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
        _interactiveRebaseService = interactiveRebaseService
            ?? new GitInteractiveRebaseService(runner, stateService);
    }
}
