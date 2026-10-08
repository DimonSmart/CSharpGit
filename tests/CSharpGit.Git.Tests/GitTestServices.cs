using CSharpGit.Application;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git.Tests;

internal static class GitTestServices
{
    internal static GitCommandExecutor CreateExecutor(
        GitCliOptions? options = null,
        IGitCommandActivitySink? activitySink = null,
        Action<int>? processStarted = null) =>
        new(
            options ?? new GitCliOptions(),
            ProfileIfEnabled(activitySink ?? new GitCommandActivityHistory()),
            processStarted);

    private static IGitCommandActivitySink ProfileIfEnabled(IGitCommandActivitySink sink) =>
        Environment.GetEnvironmentVariable("CSHARPGIT_TEST_METRICS") == "1"
            ? new ProfilingGitCommandActivitySink(sink)
            : sink;

    internal static GitRepositoryService CreateRepositoryService() =>
        new(CreateExecutor());

    internal static GitRepositoryCreationService CreateRepositoryCreationService() =>
        new(CreateExecutor());

    internal static GitRepositoryCloneService CreateRepositoryCloneService() =>
        new(CreateExecutor());

    internal static GitRepositoryStateService CreateRepositoryStateService() =>
        new(CreateExecutor());

    internal static GitWorkingTreeStatusReader CreateWorkingTreeStatusReader() =>
        new(new GitRepositoryCommandRunner(CreateExecutor()));

    internal static GitWorkingTreeService CreateWorkingTreeService() =>
        new(CreateExecutor());

    internal static GitReferenceService CreateReferenceService() =>
        new(CreateExecutor());

    internal static GitRepositorySyncService CreateRepositorySyncService() =>
        new(CreateExecutor());

    internal static GitStashMutationService CreateStashMutationService(
        GitCommandExecutor? executor = null)
    {
        executor ??= CreateExecutor();
        return new GitStashMutationService(
            new GitRepositoryCommandRunner(executor));
    }

    internal static GitMergeService CreateMergeService(
        GitCommandExecutor? executor = null)
    {
        executor ??= CreateExecutor();
        return new GitMergeService(
            new GitRepositoryCommandRunner(executor),
            new GitRepositoryStateService(executor));
    }

    internal static GitConflictResolutionService CreateConflictResolutionService(
        GitCommandExecutor? executor = null)
    {
        executor ??= CreateExecutor();
        return new GitConflictResolutionService(
            new GitRepositoryCommandRunner(executor));
    }

    internal static GitInteractiveRebaseService CreateInteractiveRebaseService(
        GitCommandExecutor? executor = null)
    {
        executor ??= CreateExecutor();
        return new GitInteractiveRebaseService(
            new GitRepositoryCommandRunner(executor),
            new GitRepositoryStateService(executor));
    }

    internal static GitRepositoryOperationService CreateRepositoryOperationService(
        GitCommandExecutor? executor = null)
    {
        executor ??= CreateExecutor();
        var runner = new GitRepositoryCommandRunner(executor);
        var stateService = new GitRepositoryStateService(executor);
        var rebaseService = new GitInteractiveRebaseService(runner, stateService);
        return new GitRepositoryOperationService(
            runner,
            stateService,
            rebaseService);
    }

    internal static GitCommitAuthorDateReader CreateCommitAuthorDateReader(
        GitCommandExecutor? executor = null)
    {
        executor ??= CreateExecutor();
        return new GitCommitAuthorDateReader(
            new GitRepositoryCommandRunner(executor));
    }

    internal static GitCommitActionService CreateCommitActionService() =>
        new(CreateExecutor());

    internal static GitCommitHistoryReader CreateCommitHistoryReader(
        Action<int>? processStarted = null) =>
        new(CreateExecutor(processStarted: processStarted));

    internal static GitHistoryService CreateHistoryService(
        Action<int>? processStarted = null)
    {
        var executor = CreateExecutor(processStarted: processStarted);
        return new GitHistoryService(
            new GitCommitHistoryReader(executor),
            executor);
    }

    internal static GitWorkingTreeDiffService CreateWorkingTreeDiffService() =>
        new(CreateExecutor());

    internal static GitTagService CreateTagService() =>
        new(CreateExecutor());

    internal static GitRepositoryMaintenanceService CreateRepositoryMaintenanceService() =>
        new(CreateExecutor());

    internal static GitRepositoryFileVersionService CreateRepositoryFileVersionService() =>
        new(CreateExecutor());

    internal static GitRepositorySnapshotService CreateRepositorySnapshotService() =>
        new(CreateExecutor());

    internal static GitRepositoryHistoryRewriteService CreateRepositoryHistoryRewriteService() =>
        new(CreateExecutor());
}
