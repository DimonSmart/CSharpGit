namespace CSharpGit.Git.Tests;

/// <summary>
/// A fixture owns an immutable template; tests only mutate independent byte-for-byte copies.
/// Never hardlink Git data or copy a template with live remote URLs pointing at shared state.
/// </summary>
internal static class GitRepositoryFixtureFactory
{
    public static string CreateTemplate(string name, Action<string> build)
    {
        var root = NewPath(name + "-template");
        Directory.CreateDirectory(root);
        try
        {
            return GitTestMeasurements.Measure("fixture-template", "fixture/setup", () =>
            {
                using var phase = GitTestMeasurements.BeginPhase("fixture/setup");
                TestGitRunner.Run(root, "init", "-b", "main");
                TestGitRunner.Run(root, "config", "user.email", "tests@example.invalid");
                TestGitRunner.Run(root, "config", "user.name", "CSharpGit Tests");
                TestGitRunner.Run(root, "config", "core.autocrlf", "false");
                TestGitRunner.Run(root, "config", "commit.gpgsign", "false");
                build(root);
                return root;
            });
        }
        catch
        {
            TestDirectory.Delete(root);
            throw;
        }
    }

    public static string CopyTemplate(string template, string name)
    {
        var destination = NewPath(name);
        try
        {
            return GitTestMeasurements.Measure("fixture-copy", "fixture/setup", () =>
            {
                CopyDirectory(template, destination);
                return destination;
            });
        }
        catch
        {
            TestDirectory.Delete(destination);
            throw;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            File.Copy(file, target);
        }
    }

    private static string NewPath(string name) =>
        Path.Combine(Path.GetTempPath(), $"csharpgit-{name}-{Guid.NewGuid():N}");
}

public sealed class RebaseHistoryFixture : IDisposable
{
    private readonly string _template;

    internal string[] Commits { get; }

    public RebaseHistoryFixture()
    {
        _template = GitRepositoryFixtureFactory.CreateTemplate("rebase", directory =>
        {
            for (var i = 0; i < 5; i++)
            {
                var subject = ((char)('A' + i)).ToString();
                var file = $"commit-{i + 1}.txt";
                File.WriteAllText(Path.Combine(directory, file), $"{subject}\n");
                TestGitRunner.Run(directory, "add", file);
                TestGitRunner.Run(directory, "commit", "-m", subject);
            }
        });
        Commits = TestGitRunner.Run(_template, "rev-list", "--reverse", "HEAD")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (Commits.Length != 5) throw new InvalidOperationException("Rebase template must have five commits");
    }

    internal string CreateCopy() => GitRepositoryFixtureFactory.CopyTemplate(_template, "interactive-rebase");

    public void Dispose() => TestDirectory.Delete(_template);
}

public sealed class StashHistoryFixture : IDisposable
{
    private readonly string _template = GitRepositoryFixtureFactory.CreateTemplate("stash", directory =>
    {
        File.WriteAllText(Path.Combine(directory, "tracked.txt"), "initial\n");
        TestGitRunner.Run(directory, "add", "tracked.txt");
        TestGitRunner.Run(directory, "commit", "-m", "Initial");
    });

    internal string CreateCopy() => GitRepositoryFixtureFactory.CopyTemplate(_template, "stash");

    public void Dispose() => TestDirectory.Delete(_template);
}

public sealed class PublishHistoryFixture : IDisposable
{
    private readonly string _template = GitRepositoryFixtureFactory.CreateTemplate("publish", directory =>
    {
        File.WriteAllText(Path.Combine(directory, "initial.txt"), "initial\n");
        TestGitRunner.Run(directory, "add", "initial.txt");
        TestGitRunner.Run(directory, "commit", "-m", "Initial");
    });

    internal string CreateCopy() => GitRepositoryFixtureFactory.CopyTemplate(_template, "publish");

    public void Dispose() => TestDirectory.Delete(_template);
}

public sealed class ForcePushHistoryFixture : IDisposable
{
    private readonly string _template = GitRepositoryFixtureFactory.CreateTemplate("force", directory =>
    {
        File.WriteAllText(Path.Combine(directory, "history.txt"), "A\n");
        TestGitRunner.Run(directory, "add", "history.txt");
        TestGitRunner.Run(directory, "commit", "-m", "A");
    });

    internal string CreateCopy() => GitRepositoryFixtureFactory.CopyTemplate(_template, "force");

    public void Dispose() => TestDirectory.Delete(_template);
}

public sealed class GitToolsHistoryFixture : IDisposable
{
    // Left and right branches are committed, but each test creates its own real
    // unmerged index/working tree via git merge on an isolated repository copy.
    private readonly string _template = GitRepositoryFixtureFactory.CreateTemplate("git-tools", directory =>
    {
        File.WriteAllText(Path.Combine(directory, "conflict.txt"), "base\n");
        TestGitRunner.Run(directory, "add", "conflict.txt");
        TestGitRunner.Run(directory, "commit", "-m", "base");
        TestGitRunner.Run(directory, "checkout", "-b", "left");
        File.WriteAllText(Path.Combine(directory, "conflict.txt"), "left\n");
        TestGitRunner.Run(directory, "add", "conflict.txt");
        TestGitRunner.Run(directory, "commit", "-m", "left");
        TestGitRunner.Run(directory, "checkout", "-b", "right", "HEAD~1");
        File.WriteAllText(Path.Combine(directory, "conflict.txt"), "right\n");
        TestGitRunner.Run(directory, "add", "conflict.txt");
        TestGitRunner.Run(directory, "commit", "-m", "right");
        TestGitRunner.Run(directory, "checkout", "left");
    });

    internal string CreateCopy() => GitRepositoryFixtureFactory.CopyTemplate(_template, "git-tools");

    public void Dispose() => TestDirectory.Delete(_template);
}
