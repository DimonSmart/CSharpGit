using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git;

internal sealed partial class GitCommitActionService : ICommitActionService
{
private readonly GitRepositoryCommandRunner _runner;
    private readonly GitRepositoryStateService _stateService;
    private readonly GitRepositoryWorkflowService _workflowService;

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
        GitRepositoryWorkflowService? workflowService)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
        _workflowService = workflowService
            ?? new GitRepositoryWorkflowService(runner, stateService);
    }
}
