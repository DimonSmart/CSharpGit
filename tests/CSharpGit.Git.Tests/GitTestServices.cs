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
            activitySink ?? new GitCommandActivityHistory(),
            processStarted);

    internal static GitRepositoryService CreateRepositoryService() =>
        new(CreateExecutor());

    internal static GitRepositoryCreationService CreateRepositoryCreationService() =>
        new(CreateExecutor());

    internal static GitRepositoryStateService CreateRepositoryStateService() =>
        new(CreateExecutor());

    internal static GitRepositoryRefreshProbe CreateRepositoryRefreshProbe() =>
        new(CreateExecutor());

    internal static GitWorkingTreeService CreateWorkingTreeService() =>
        new(CreateExecutor());

    internal static GitReferenceService CreateReferenceService() =>
        new(CreateExecutor());

    internal static GitRepositorySyncService CreateRepositorySyncService() =>
        new(CreateExecutor());

    internal static GitRepositoryWorkflowService CreateRepositoryWorkflowService() =>
        new(CreateExecutor());

    internal static GitCommitActionService CreateCommitActionService() =>
        new(CreateExecutor());

    internal static GitReferenceHistoryService CreateReferenceHistoryService() =>
        new(CreateExecutor());

    internal static GitFileAwareHistoryService CreateFileAwareHistoryService()
    {
        var executor = CreateExecutor();
        return new GitFileAwareHistoryService(
            new GitReferenceHistoryService(executor),
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
