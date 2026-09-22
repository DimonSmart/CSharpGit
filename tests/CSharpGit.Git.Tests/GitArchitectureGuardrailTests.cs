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
            Assert.DoesNotContain("new GitReferenceHistoryService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new GitCliRepositoryService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new GitFileAwareHistoryService", source, StringComparison.Ordinal);
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
        Assert.True(typeof(IRepositoryStateService).IsAssignableFrom(typeof(GitRepositoryStateService)));
        Assert.True(typeof(IWorkingTreeService).IsAssignableFrom(typeof(GitWorkingTreeService)));
        Assert.True(typeof(IWorkingTreeDiffService).IsAssignableFrom(typeof(GitWorkingTreeDiffService)));
        Assert.True(typeof(IReferenceService).IsAssignableFrom(typeof(GitReferenceService)));
        Assert.True(typeof(IRepositorySyncService).IsAssignableFrom(typeof(GitRepositorySyncService)));
        Assert.True(typeof(IRepositoryWorkflowService).IsAssignableFrom(typeof(GitRepositoryWorkflowService)));
        Assert.True(typeof(ICommitActionService).IsAssignableFrom(typeof(GitCommitActionService)));

        var implementations = new[]
        {
            typeof(GitRepositoryService),
            typeof(GitRepositoryStateService),
            typeof(GitWorkingTreeService),
            typeof(GitWorkingTreeDiffService),
            typeof(GitReferenceService),
            typeof(GitRepositorySyncService),
            typeof(GitRepositoryWorkflowService),
            typeof(GitCommitActionService)
        };

        Assert.Equal(implementations.Length, implementations.Distinct().Count());
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
    public void GitServicesRequireExplicitExecutorConstruction()
    {
        var serviceTypes = new[]
        {
            typeof(GitRepositoryService),
            typeof(GitRepositoryStateService),
            typeof(GitWorkingTreeService),
            typeof(GitWorkingTreeDiffService),
            typeof(GitReferenceService),
            typeof(GitRepositorySyncService),
            typeof(GitRepositoryWorkflowService),
            typeof(GitCommitActionService),
            typeof(GitReferenceHistoryService),
            typeof(GitFileAwareHistoryService),
            typeof(GitTagService),
            typeof(GitRepositoryFileVersionService),
            typeof(GitRepositorySnapshotService),
            typeof(GitRepositoryHistoryRewriteService),
            typeof(GitRepositoryMaintenanceService)
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
