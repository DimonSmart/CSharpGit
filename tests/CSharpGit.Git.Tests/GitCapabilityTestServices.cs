namespace CSharpGit.Git.Tests;

internal sealed class GitCapabilityTestServices
{
    internal GitRepositoryService Repositories { get; }
    internal GitRepositoryStateService State { get; }
    internal GitWorkingTreeService WorkingTree { get; }
    internal GitWorkingTreeDiffService WorkingTreeDiff { get; }
    internal GitReferenceService References { get; }
    internal GitRepositorySyncService Sync { get; }
    internal GitRepositoryWorkflowService Workflow { get; }
    internal GitCommitActionService CommitActions { get; }

    internal GitCapabilityTestServices(GitCommandExecutor? executor = null)
    {
        executor ??= GitCommandExecutor.Default;
        var commands = new GitCommandRunner(executor);
        var operations = new GitOperationDetector();
        var push = new GitPushRunner(commands);

        Repositories = new GitRepositoryService(commands);
        State = new GitRepositoryStateService(commands, operations);
        WorkingTree = new GitWorkingTreeService(commands);
        WorkingTreeDiff = new GitWorkingTreeDiffService(commands);
        References = new GitReferenceService(commands, push);
        Sync = new GitRepositorySyncService(commands, operations);
        Workflow = new GitRepositoryWorkflowService(commands, operations, State);
        CommitActions = new GitCommitActionService(
            commands,
            operations,
            State,
            Workflow);
    }
}
