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

        if (options is null)
        {
            services.AddSingleton(GitCommandExecutor.Default);
        }
        else
        {
            services.AddSingleton(options);
            services.AddSingleton(provider =>
                new GitCommandExecutor(provider.GetRequiredService<GitCliOptions>()));
        }

        services.AddSingleton(provider =>
            new GitCliRepositoryService(provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton(provider =>
            new DefaultBranchResolver(provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton<IRepositoryService>(provider => provider.GetRequiredService<GitCliRepositoryService>());
        services.AddSingleton<IRepositoryStateService>(provider =>
            new DefaultBranchRepositoryStateService(
                provider.GetRequiredService<GitCliRepositoryService>(),
                provider.GetRequiredService<DefaultBranchResolver>()));
        services.AddSingleton<IWorkingTreeService>(provider => provider.GetRequiredService<GitCliRepositoryService>());
        services.AddSingleton<IWorkingTreeDiffService>(provider => provider.GetRequiredService<GitCliRepositoryService>());
        services.AddSingleton<IWorktreeService>(provider =>
            new GitWorktreeService(provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton<IReferenceService>(provider =>
            new DefaultBranchReferenceService(
                provider.GetRequiredService<GitCliRepositoryService>(),
                provider.GetRequiredService<DefaultBranchResolver>()));

        services.AddSingleton<IRepositoryFileVersionService>(provider =>
            new GitRepositoryFileVersionService(provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton<IGitToolsService>(provider =>
            new GitToolsService(
                provider.GetRequiredService<GitCommandExecutor>(),
                provider.GetRequiredService<IRepositoryFileVersionService>(),
                provider.GetRequiredService<IRepositoryPathService>(),
                provider.GetRequiredService<IExternalToolProcessService>()));

        services.AddSingleton(provider =>
            new GitReferenceHistoryService(provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton(provider =>
            new GitFileAwareHistoryService(
                provider.GetRequiredService<GitReferenceHistoryService>(),
                provider.GetRequiredService<GitCommandExecutor>()));
        services.AddSingleton<IHistoryService>(provider => provider.GetRequiredService<GitFileAwareHistoryService>());
        services.AddSingleton<IReferenceHistoryService>(provider => provider.GetRequiredService<GitFileAwareHistoryService>());

        return services;
    }
}
