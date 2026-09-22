using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git;

internal sealed partial class GitCommitActionService : ICommitActionService
{
    internal GitCommitActionService()
        : this(GitCommandExecutor.Default)
    {
    }


    private readonly GitRepositoryCommandRunner _runner;
    private readonly GitRepositoryStateService _stateService;
    private readonly GitRepositoryWorkflowService _workflowService;

    internal GitCommitActionService(GitCommandExecutor executor)
        : this(new GitRepositoryCommandRunner(executor))
    {
    }

    internal GitCommitActionService(GitRepositoryCommandRunner runner)
        : this(
            runner,
            new GitRepositoryStateService(runner),
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
