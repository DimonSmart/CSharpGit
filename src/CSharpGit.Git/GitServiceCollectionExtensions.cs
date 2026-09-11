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
        services.AddSingleton<IRepositoryService>(provider => provider.GetRequiredService<GitCliRepositoryService>());
        services.AddSingleton<IRepositoryStateService>(provider => provider.GetRequiredService<GitCliRepositoryService>());
        services.AddSingleton<IWorkingTreeService>(provider => provider.GetRequiredService<GitCliRepositoryService>());
        services.AddSingleton<IWorkingTreeDiffService>(provider => provider.GetRequiredService<GitCliRepositoryService>());
        services.AddSingleton<IReferenceService>(provider => provider.GetRequiredService<GitCliRepositoryService>());

        services.AddSingleton<IRepositoryFileVersionService>(provider =>
            new GitRepositoryFileVersionService(provider.GetRequiredService<GitCommandExecutor>()));

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
