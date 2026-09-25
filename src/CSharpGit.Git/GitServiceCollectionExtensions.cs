using CSharpGit.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CSharpGit.Git;

public static class GitServiceCollectionExtensions
{
    public static IServiceCollection AddCSharpGitGit(
        this IServiceCollection services,
        GitCliOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(options ?? new GitCliOptions());
        services.AddSingleton(provider =>
            new GitCommandExecutor(
                provider.GetRequiredService<GitCliOptions>(),
                provider.GetRequiredService<IGitCommandActivitySink>()));

        services.AddSingleton(provider =>
            new GitRepositoryCommandRunner(
                provider.GetRequiredService<GitCommandExecutor>()));

        services.AddSingleton(provider =>
            new GitRepositoryService(
                provider.GetRequiredService<GitRepositoryCommandRunner>()));
        services.AddSingleton<IRepositoryService>(provider =>
            provider.GetRequiredService<GitRepositoryService>());

        services.AddSingleton(provider =>
            new GitRepositoryCreationService(
                provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton<IRepositoryCreationService>(provider =>
            provider.GetRequiredService<GitRepositoryCreationService>());

        services.AddSingleton(provider =>
            new GitRepositoryStateService(
                provider.GetRequiredService<GitRepositoryCommandRunner>()));
        services.AddSingleton(provider =>
            new DefaultBranchRepositoryStateService(
                provider.GetRequiredService<GitRepositoryStateService>(),
                provider.GetRequiredService<DefaultBranchResolver>(),
                provider.GetRequiredService<ITagService>()));
        services.AddSingleton<IRepositoryStateService>(provider =>
            provider.GetRequiredService<DefaultBranchRepositoryStateService>());

        services.AddSingleton(provider =>
            new GitWorkingTreeService(
                provider.GetRequiredService<GitRepositoryCommandRunner>()));
        services.AddSingleton<IWorkingTreeService>(provider =>
            provider.GetRequiredService<GitWorkingTreeService>());

        services.AddSingleton(provider =>
            new GitWorkingTreeDiffService(
                provider.GetRequiredService<GitRepositoryCommandRunner>()));
        services.AddSingleton<IWorkingTreeDiffService>(provider =>
            provider.GetRequiredService<GitWorkingTreeDiffService>());

        services.AddSingleton(provider =>
            new GitReferenceService(
                provider.GetRequiredService<GitRepositoryCommandRunner>()));
        services.AddSingleton<IReferenceService>(provider =>
            provider.GetRequiredService<GitReferenceService>());

        services.AddSingleton(provider =>
            new GitRepositorySyncService(
                provider.GetRequiredService<GitRepositoryCommandRunner>()));
        services.AddSingleton<IRepositorySyncService>(provider =>
            new DefaultBranchRepositorySyncService(
                provider.GetRequiredService<GitRepositorySyncService>(),
                provider.GetRequiredService<DefaultBranchResolver>()));

        services.AddSingleton(provider =>
            new GitRepositoryWorkflowService(
                provider.GetRequiredService<GitRepositoryCommandRunner>(),
                provider.GetRequiredService<GitRepositoryStateService>()));
        services.AddSingleton<IRepositoryWorkflowService>(provider =>
            provider.GetRequiredService<GitRepositoryWorkflowService>());

        services.AddSingleton(provider =>
            new GitCommitActionService(
                provider.GetRequiredService<GitRepositoryCommandRunner>(),
                provider.GetRequiredService<GitRepositoryStateService>(),
                provider.GetRequiredService<GitRepositoryWorkflowService>()));
        services.AddSingleton<ICommitActionService>(provider =>
            provider.GetRequiredService<GitCommitActionService>());

        services.AddSingleton(provider =>
            new GitTagService(
                provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton<ITagService>(provider =>
            provider.GetRequiredService<GitTagService>());

        services.AddSingleton(provider =>
            new DefaultBranchResolver(
                provider.GetRequiredService<GitCommandExecutor>()));

        services.AddSingleton<IRepositoryRefreshProbe>(provider =>
            new GitRepositoryRefreshProbe(
                provider.GetRequiredService<IRepositoryStateService>()));
        services.AddSingleton<IWorktreeService>(provider =>
            new GitWorktreeService(
                provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton<IRepositoryFileVersionService>(provider =>
            new GitRepositoryFileVersionService(
                provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton<IRepositorySnapshotService>(provider =>
            new GitRepositorySnapshotService(
                provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton<IRepositoryHistoryRewriteService>(provider =>
            new GitRepositoryHistoryRewriteService(
                provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton<IRepositoryMaintenanceService>(provider =>
            new GitRepositoryMaintenanceService(
                provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton<IGitToolsService>(provider =>
            new GitToolsService(
                provider.GetRequiredService<GitCommandExecutor>(),
                provider.GetRequiredService<IRepositoryFileVersionService>(),
                provider.GetRequiredService<IRepositoryPathService>(),
                provider.GetRequiredService<IExternalToolProcessService>()));

        services.AddSingleton(provider =>
            new GitReferenceHistoryService(
                provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton(provider =>
            new GitFileAwareHistoryService(
                provider.GetRequiredService<GitReferenceHistoryService>(),
                provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton<IHistoryService>(provider =>
            provider.GetRequiredService<GitFileAwareHistoryService>());
        services.AddSingleton<IReferenceHistoryService>(provider =>
            provider.GetRequiredService<GitFileAwareHistoryService>());

        return services;
    }
}
