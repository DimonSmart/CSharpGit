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
            Assert.DoesNotContain("_gitExecutable", source, StringComparison.Ordinal);

            if (relative == "GitCommandExecutor.cs") continue;

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
