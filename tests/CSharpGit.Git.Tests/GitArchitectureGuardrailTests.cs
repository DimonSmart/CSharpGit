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
        Assert.False(typeof(ITagService).IsAssignableFrom(typeof(GitCliRepositoryService)));
    }

    [Fact]
    public void GitTagServiceIsTheOnlyProductionTagImplementationPath()
    {
        var gitProject = Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Git");
        Assert.False(File.Exists(Path.Combine(gitProject, "GitCliRepositoryService.Tags.cs")));

        var repositoryServiceSources = Directory
            .GetFiles(gitProject, "GitCliRepositoryService*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText);
        Assert.DoesNotContain(repositoryServiceSources, source => source.Contains("new GitTagService", StringComparison.Ordinal));

        var referenceDecorator = File.ReadAllText(Path.Combine(gitProject, "DefaultBranchReferenceService.cs"));
        Assert.DoesNotContain("ITagService", referenceDecorator, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadTagsAsync", referenceDecorator, StringComparison.Ordinal);

        var registrations = File.ReadAllText(Path.Combine(gitProject, "GitServiceCollectionExtensions.cs"));
        Assert.Contains("AddSingleton<ITagService>", registrations, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<GitTagService>()", registrations, StringComparison.Ordinal);
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
