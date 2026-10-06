using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CSharpGit.Git.Tests;

public sealed class GitDependencyInjectionTests
{
    [Fact]
    public void AddCSharpGitGitResolvesExecutorAndCoreServicesWithExplicitActivitySink()
    {
        var services = new ServiceCollection();
        var options = new GitCliOptions();
        var activity = new GitCommandActivityHistory();
        services.AddSingleton<IGitCommandActivitySink>(activity);

        services.AddCSharpGitGit(options);

        using var provider = services.BuildServiceProvider();
        Assert.Same(options, provider.GetRequiredService<GitCliOptions>());
        Assert.NotNull(provider.GetRequiredService<GitCommandExecutor>());
        Assert.IsType<GitRepositoryStateService>(
            provider.GetRequiredService<IRepositoryStateService>());
        Assert.IsType<GitRepositorySyncService>(
            provider.GetRequiredService<IRepositorySyncService>());

        Assert.All(
            new object[]
            {
                provider.GetRequiredService<IRepositoryService>(),
                provider.GetRequiredService<IRepositoryCreationService>(),
                provider.GetRequiredService<IRepositoryStateService>(),
                provider.GetRequiredService<IWorkingTreeService>(),
                provider.GetRequiredService<IWorkingTreeDiffService>(),
                provider.GetRequiredService<IReferenceService>(),
                provider.GetRequiredService<IRepositorySyncService>(),
                provider.GetRequiredService<IStashMutationService>(),
                provider.GetRequiredService<IMergeService>(),
                provider.GetRequiredService<IConflictResolutionService>(),
                provider.GetRequiredService<IInteractiveRebaseService>(),
                provider.GetRequiredService<IRepositoryOperationService>(),
                provider.GetRequiredService<ICommitAuthorDateReader>(),
                provider.GetRequiredService<ICommitActionService>(),
                provider.GetRequiredService<ITagService>(),
                provider.GetRequiredService<IWorkingTreeStatusReader>(),
                provider.GetRequiredService<IWorktreeService>(),
                provider.GetRequiredService<IRepositoryFileVersionService>(),
                provider.GetRequiredService<IRepositorySnapshotService>(),
                provider.GetRequiredService<IRepositoryHistoryRewriteService>(),
                provider.GetRequiredService<IRepositoryMaintenanceService>(),
                provider.GetRequiredService<IRepositoryIdentityService>(),
                provider.GetRequiredService<IInteractiveRebaseAuthorChangeService>(),
                provider.GetRequiredService<IStashService>(),
                provider.GetRequiredService<IHistoryService>(),
                provider.GetRequiredService<IReferenceHistoryService>()
            },
            service => Assert.NotNull(service));
    }

    [Fact]
    public void ActivitySinkAndSourceCanBeRegisteredAsOneHistoryInstance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<GitCommandActivityHistory>();
        services.AddSingleton<IGitCommandActivitySink>(
            provider => provider.GetRequiredService<GitCommandActivityHistory>());
        services.AddSingleton<IGitCommandActivitySource>(
            provider => provider.GetRequiredService<GitCommandActivityHistory>());

        using var provider = services.BuildServiceProvider();
        var history = provider.GetRequiredService<GitCommandActivityHistory>();

        Assert.Same(history, provider.GetRequiredService<IGitCommandActivitySink>());
        Assert.Same(history, provider.GetRequiredService<IGitCommandActivitySource>());
    }
}
