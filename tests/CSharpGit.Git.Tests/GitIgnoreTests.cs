using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class GitIgnoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "csharpgit-ignore-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FileRuleIgnoresOnlyTheSelectedRepositoryRelativePath()
    {
        var repository = await InitializeRepositoryAsync();
        WriteFile("logs/cache.tmp", "first");
        WriteFile("other/cache.tmp", "second");

        await GitTestServices.CreateWorkingTreeService().AddFileToGitIgnoreAsync(
            repository, Untracked("logs/cache.tmp"));

        Assert.Equal("/logs/cache.tmp\n", File.ReadAllText(Path.Combine(_root, ".gitignore")));
        TestGitRunner.Run(_root, "check-ignore", "--quiet", "--", "logs/cache.tmp");
        Assert.Contains("other/cache.tmp", TestGitRunner.Run(
            _root, "ls-files", "--others", "--exclude-standard"), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_root, "logs", "cache.tmp")));
    }

    [Fact]
    public async Task FolderRuleIgnoresUntrackedFilesWithoutRemovingTrackedFilesFromIndex()
    {
        var repository = await InitializeRepositoryAsync();
        WriteFile("generated/tracked.txt", "committed");
        TestGitRunner.Run(_root, "add", "--", "generated/tracked.txt");
        TestGitRunner.Run(_root, "commit", "-m", "Tracked file");
        WriteFile("generated/cache.tmp", "untracked");

        await GitTestServices.CreateWorkingTreeService().AddFolderToGitIgnoreAsync(
            repository, "generated");

        Assert.Equal("/generated/\n", File.ReadAllText(Path.Combine(_root, ".gitignore")));
        TestGitRunner.Run(_root, "check-ignore", "--quiet", "--", "generated/cache.tmp");
        Assert.Contains("generated/tracked.txt", TestGitRunner.Run(
            _root, "ls-files", "--", "generated/tracked.txt"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpensSubdirectoryButAppendsToRepositoryRootGitIgnore()
    {
        await InitializeRepositoryAsync();
        WriteFile("sub/temp.tmp", "content");
        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(
            Path.Combine(_root, "sub"));

        await GitTestServices.CreateWorkingTreeService().AddFileToGitIgnoreAsync(
            repository, Untracked("sub/temp.tmp"));

        Assert.Equal("/sub/temp.tmp\n", File.ReadAllText(Path.Combine(_root, ".gitignore")));
        Assert.False(File.Exists(Path.Combine(_root, "sub", ".gitignore")));
    }

    [Fact]
    public async Task RepeatedRuleDoesNotDuplicateAndPreservesExistingCrLf()
    {
        var repository = await InitializeRepositoryAsync();
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "old-rule\r\nlast-rule");
        var service = GitTestServices.CreateWorkingTreeService();
        var change = Untracked("cache.tmp");

        await service.AddFileToGitIgnoreAsync(repository, change);
        await service.AddFileToGitIgnoreAsync(repository, change);

        Assert.Equal("old-rule\r\nlast-rule\r\n/cache.tmp\r\n",
            File.ReadAllText(Path.Combine(_root, ".gitignore")));
    }

    [Fact]
    public async Task RejectsTrackedFilesAndUnsafePaths()
    {
        var repository = await InitializeRepositoryAsync();
        var service = GitTestServices.CreateWorkingTreeService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddFileToGitIgnoreAsync(repository, new WorkingTreeChange("tracked.txt", ' ', 'M')));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddFolderToGitIgnoreAsync(repository, "."));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddFolderToGitIgnoreAsync(repository, "../outside"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddFolderToGitIgnoreAsync(repository, ".git/objects"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddFileToGitIgnoreAsync(repository, Untracked(".gitignore")));

        Assert.False(File.Exists(Path.Combine(_root, ".gitignore")));
    }

    [Fact]
    public async Task EscapesLiteralGitPatternCharacters()
    {
        if (OperatingSystem.IsWindows()) return;

        var repository = await InitializeRepositoryAsync();
        WriteFile("some folder/a[b]*?.txt", "content");

        await GitTestServices.CreateWorkingTreeService().AddFileToGitIgnoreAsync(
            repository, Untracked("some folder/a[b]*?.txt"));

        Assert.Equal("/some folder/a\\[b\\]\\*\\?.txt\n",
            File.ReadAllText(Path.Combine(_root, ".gitignore")));
        TestGitRunner.Run(_root, "check-ignore", "--quiet", "--", "some folder/a[b]*?.txt");
    }

    private async Task<Repository> InitializeRepositoryAsync()
    {
        Directory.CreateDirectory(_root);
        TestGitRunner.Run(_root, "init", "-b", "main");
        TestGitRunner.Run(_root, "config", "user.email", "tests@example.invalid");
        TestGitRunner.Run(_root, "config", "user.name", "Git Ignore Tests");
        return await GitTestServices.CreateRepositoryService().OpenAsync(_root);
    }

    private void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static WorkingTreeChange Untracked(string path) => new(path, '?', '?');

    public void Dispose() => TestDirectory.Delete(_root);
}
