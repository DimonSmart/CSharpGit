using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git.Tests;

public sealed class GitArchitectureGuardrailTests
{
    [Fact]
    public void OnlyGitCommandExecutorMayLaunchGitCli()
    {
        var gitProject = Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Git");
        var files = Directory.GetFiles(gitProject, "*.cs", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(gitProject, file).Replace('\\', '/');
            var source = File.ReadAllText(file);

            Assert.DoesNotContain("GitProcessRunner", source, StringComparison.Ordinal);

            if (relative == "GitCommandExecutor.cs") continue;

            Assert.DoesNotContain("_gitExecutable", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RedirectStandardOutput = true", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RedirectStandardError = true", source, StringComparison.Ordinal);

            if (relative == "GitServiceCollectionExtensions.cs") continue;

            Assert.DoesNotContain("new GitCommandExecutor", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new GitCommitHistoryReader", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new GitCliRepositoryService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new GitHistoryService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new GitRepositoryFileVersionService", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TagAndReferenceCapabilitiesRemainIndependent()
    {
        Assert.False(typeof(ITagService).IsAssignableFrom(typeof(IReferenceService)));
        Assert.False(typeof(ITagService).IsAssignableFrom(typeof(GitReferenceService)));
    }

    [Fact]
    public void GitTagServiceIsTheOnlyProductionTagImplementationPath()
    {
        var gitProject = Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Git");
        Assert.False(File.Exists(Path.Combine(gitProject, "GitCliRepositoryService.Tags.cs")));

        Assert.Empty(Directory.GetFiles(gitProject, "GitCliRepositoryService*.cs", SearchOption.TopDirectoryOnly));

        var referenceService = File.ReadAllText(Path.Combine(gitProject, "GitReferenceService.cs"));
        Assert.DoesNotContain("ITagService", referenceService, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadTagsAsync", referenceService, StringComparison.Ordinal);

        var registrations = File.ReadAllText(Path.Combine(gitProject, "GitServiceCollectionExtensions.cs"));
        Assert.Contains("AddSingleton<ITagService>", registrations, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<GitTagService>()", registrations, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryCapabilitiesHaveDistinctGitImplementations()
    {
        Assert.True(typeof(IRepositoryService).IsAssignableFrom(typeof(GitRepositoryService)));
        Assert.True(typeof(IRepositoryCreationService).IsAssignableFrom(typeof(GitRepositoryCreationService)));
        Assert.True(typeof(IRepositoryStateService).IsAssignableFrom(typeof(GitRepositoryStateService)));
        Assert.True(typeof(IWorkingTreeService).IsAssignableFrom(typeof(GitWorkingTreeService)));
        Assert.True(typeof(IWorkingTreeDiffService).IsAssignableFrom(typeof(GitWorkingTreeDiffService)));
        Assert.True(typeof(IReferenceService).IsAssignableFrom(typeof(GitReferenceService)));
        Assert.True(typeof(IRepositorySyncService).IsAssignableFrom(typeof(GitRepositorySyncService)));
        Assert.True(typeof(IStashMutationService).IsAssignableFrom(typeof(GitStashMutationService)));
        Assert.True(typeof(IMergeService).IsAssignableFrom(typeof(GitMergeService)));
        Assert.True(typeof(IConflictResolutionService).IsAssignableFrom(typeof(GitConflictResolutionService)));
        Assert.True(typeof(IInteractiveRebaseService).IsAssignableFrom(typeof(GitInteractiveRebaseService)));
        Assert.True(typeof(IRepositoryOperationService).IsAssignableFrom(typeof(GitRepositoryOperationService)));
        Assert.True(typeof(ICommitAuthorDateReader).IsAssignableFrom(typeof(GitCommitAuthorDateReader)));
        Assert.True(typeof(ICommitActionService).IsAssignableFrom(typeof(GitCommitActionService)));

        var implementations = new[]
        {
            typeof(GitRepositoryService),
            typeof(GitRepositoryCreationService),
            typeof(GitRepositoryStateService),
            typeof(GitWorkingTreeService),
            typeof(GitWorkingTreeDiffService),
            typeof(GitReferenceService),
            typeof(GitRepositorySyncService),
            typeof(GitStashMutationService),
            typeof(GitMergeService),
            typeof(GitConflictResolutionService),
            typeof(GitInteractiveRebaseService),
            typeof(GitRepositoryOperationService),
            typeof(GitCommitAuthorDateReader),
            typeof(GitCommitActionService)
        };

        Assert.Equal(implementations.Length, implementations.Distinct().Count());
    }

    [Fact]
    public void LegacyRepositoryWorkflowTypesAreRemoved()
    {
        Assert.Null(typeof(IRepositoryService).Assembly.GetType(
            "CSharpGit.Application.Abstractions.IRepositoryWorkflowService"));
        Assert.Null(typeof(GitRepositoryService).Assembly.GetType(
            "CSharpGit.Git.GitRepositoryWorkflowService"));
    }

    [Fact]
    public void WorkflowCapabilitiesRemainIndependent()
    {
        Assert.False(typeof(IInteractiveRebaseService).IsAssignableFrom(typeof(GitMergeService)));
        Assert.False(typeof(IMergeService).IsAssignableFrom(typeof(GitInteractiveRebaseService)));
        Assert.False(typeof(IInteractiveRebaseService).IsAssignableFrom(typeof(GitConflictResolutionService)));
        Assert.False(typeof(IMergeService).IsAssignableFrom(typeof(GitConflictResolutionService)));
        Assert.False(typeof(IInteractiveRebaseService).IsAssignableFrom(typeof(GitRepositoryOperationService)));
        Assert.False(typeof(IMergeService).IsAssignableFrom(typeof(GitRepositoryOperationService)));
        Assert.False(typeof(IConflictResolutionService).IsAssignableFrom(typeof(GitRepositoryOperationService)));
    }

    [Fact]
    public void ReferenceSyncAndCommitMethodsAreNotMixedAcrossCapabilityServices()
    {
        Assert.DoesNotContain(nameof(IRepositorySyncService.FetchAsync), typeof(GitReferenceService).GetMethods().Select(method => method.Name));
        Assert.DoesNotContain(nameof(IRepositorySyncService.DeleteRemoteBranchAsync), typeof(GitReferenceService).GetMethods().Select(method => method.Name));
        Assert.Contains(nameof(IRepositorySyncService.DeleteRemoteBranchAsync), typeof(GitRepositorySyncService).GetMethods().Select(method => method.Name));
        Assert.DoesNotContain(nameof(ICommitActionService.CherryPickAsync), typeof(GitReferenceService).GetMethods().Select(method => method.Name));
        Assert.DoesNotContain(nameof(IReferenceService.SwitchBranchAsync), typeof(GitRepositorySyncService).GetMethods().Select(method => method.Name));
        Assert.DoesNotContain(nameof(ICommitActionService.ResetAsync), typeof(GitRepositorySyncService).GetMethods().Select(method => method.Name));
        Assert.DoesNotContain(nameof(IRepositorySyncService.PushAsync), typeof(GitCommitActionService).GetMethods().Select(method => method.Name));
        Assert.DoesNotContain(nameof(IReferenceService.CreateBranchAsync), typeof(GitCommitActionService).GetMethods().Select(method => method.Name));
    }

    [Fact]
    public void RepositoryStateAndSyncCapabilitiesDoNotUseLegacyWrappers()
    {
        var gitProject = Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Git");

        Assert.False(File.Exists(Path.Combine(gitProject, "DefaultBranchRepositoryStateService.cs")));
        Assert.False(File.Exists(Path.Combine(gitProject, "DefaultBranchRepositorySyncService.cs")));
    }

    [Fact]
    public void RepositoryStateSessionLayerIsNotRestored()
    {
        var applicationProject = Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Application");

        Assert.False(File.Exists(Path.Combine(
            applicationProject,
            "Abstractions",
            "IRepositoryStateSession.cs")));
        Assert.False(File.Exists(Path.Combine(
            applicationProject,
            "RepositoryStateSession.cs")));
    }

    [Fact]
    public void HistoryCapabilityHasSingleProductionImplementation()
    {
        Assert.True(typeof(IHistoryService).IsAssignableFrom(typeof(GitHistoryService)));
        Assert.False(typeof(IHistoryService).IsAssignableFrom(typeof(GitCommitHistoryReader)));
        Assert.Null(typeof(IHistoryService).Assembly.GetType(
            "CSharpGit.Application.Abstractions.IReferenceHistoryService"));
        Assert.Null(typeof(GitHistoryService).Assembly.GetType(
            "CSharpGit.Git.GitReferenceHistoryService"));
        Assert.Null(typeof(GitHistoryService).Assembly.GetType(
            "CSharpGit.Git.GitFileAwareHistoryService"));
    }

    [Fact]
    public void GitServicesRequireExplicitExecutorConstruction()
    {
        var serviceTypes = new[]
        {
            typeof(GitRepositoryService),
            typeof(GitRepositoryCreationService),
            typeof(GitRepositoryStateService),
            typeof(GitWorkingTreeService),
            typeof(GitWorkingTreeDiffService),
            typeof(GitReferenceService),
            typeof(GitRepositorySyncService),
            typeof(GitStashMutationService),
            typeof(GitMergeService),
            typeof(GitConflictResolutionService),
            typeof(GitInteractiveRebaseService),
            typeof(GitRepositoryOperationService),
            typeof(GitCommitAuthorDateReader),
            typeof(GitCommitActionService),
            typeof(GitCommitHistoryReader),
            typeof(GitHistoryService),
            typeof(GitTagService),
            typeof(GitRepositoryFileVersionService),
            typeof(GitRepositorySnapshotService),
            typeof(GitRepositoryHistoryRewriteService),
            typeof(GitRepositoryMaintenanceService),
            typeof(GitToolConfigurationService),
            typeof(ExternalGitToolService)
        };

        foreach (var serviceType in serviceTypes)
        {
            var parameterless = serviceType
                .GetConstructors(System.Reflection.BindingFlags.Instance |
                                 System.Reflection.BindingFlags.Public |
                                 System.Reflection.BindingFlags.NonPublic)
                .Where(constructor => constructor.GetParameters().Length == 0)
                .ToArray();
            Assert.Empty(parameterless);
        }

        var root = FindRepositoryRoot();
        var gitProject = Path.Combine(root, "src", "CSharpGit.Git");
        var executor = File.ReadAllText(Path.Combine(gitProject, "GitCommandExecutor.cs"));
        var activity = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "GitCommandActivityHistory.cs"));

        Assert.DoesNotContain("static GitCommandExecutor Default", executor, StringComparison.Ordinal);
        Assert.DoesNotContain("GitCommandActivitySession", activity, StringComparison.Ordinal);
    }

    [Fact]
    public void GitToolsConfigurationAndExecutionCapabilitiesRemainSeparated()
    {
        Assert.Null(typeof(IGitToolConfigurationService).Assembly.GetType(
            "CSharpGit.Application.Abstractions.IGitToolsService"));
        Assert.Null(typeof(GitToolConfigurationService).Assembly.GetType(
            "CSharpGit.Git.GitToolsService"));

        var root = FindRepositoryRoot();
        var gitProject = Path.Combine(root, "src", "CSharpGit.Git");
        Assert.False(File.Exists(Path.Combine(gitProject, "GitToolsService.cs")));

        var configuration = File.ReadAllText(Path.Combine(gitProject, "GitToolConfigurationService.cs"));
        Assert.DoesNotContain("RunShellCommandAsync", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("IRepositoryFileVersionService", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("IRepositoryPathService", configuration, StringComparison.Ordinal);

        var execution = File.ReadAllText(Path.Combine(gitProject, "ExternalGitToolService.cs"));
        Assert.Contains("IGitToolConfigurationService _configurationService", execution, StringComparison.Ordinal);
        Assert.DoesNotContain("GitConfigService", execution, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CSharpGit.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
