using System.Diagnostics;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class BatchStagingTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-batch-{Guid.NewGuid():N}");

    [Fact]
    public async Task StageFilesStagesMixedSelectedPathsIncludingRenameSpacesAndUnicode()
    {
        InitializeRepository(withCommit: true);
        CommitFile("file with spaces.cs", "base\n", "add spaced file");
        CommitFile("deleted.cs", "delete me\n", "add deleted file");
        CommitFile("old name.cs", "rename me\n", "add renamed file");
        CommitFile("leave.txt", "base\n", "add unselected file");

        File.WriteAllText(Path.Combine(_temporaryDirectory, "file with spaces.cs"), "modified\n");
        File.Delete(Path.Combine(_temporaryDirectory, "deleted.cs"));
        File.Move(Path.Combine(_temporaryDirectory, "old name.cs"), Path.Combine(_temporaryDirectory, "new name.cs"));
        File.WriteAllText(Path.Combine(_temporaryDirectory, "тест.cs"), "unicode\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "leave.txt"), "leave unstaged\n");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        var changes = (await service.ReadAsync(repository)).Changes;
        var selected = new WorkingTreeChange[]
        {
            changes.Single(change => change.Path == "file with spaces.cs"),
            changes.Single(change => change.Path == "deleted.cs"),
            changes.Single(change => change.Path == "тест.cs"),
            new("new name.cs", ' ', 'R', "old name.cs")
        };

        await service.StageFilesAsync(repository, selected);

        var refreshed = (await service.ReadAsync(repository)).Changes;
        Assert.Contains(refreshed, change => change.Path == "file with spaces.cs" && change.IsStaged && !change.IsUnstaged);
        Assert.Contains(refreshed, change => change.Path == "deleted.cs" && change.IsStaged && !change.IsUnstaged);
        Assert.Contains(refreshed, change => change.Path == "тест.cs" && change.IsStaged && !change.IsUnstaged);
        Assert.Contains(refreshed, change => change.Path == "new name.cs" && change.OriginalPath == "old name.cs" && change.IsStaged);
        Assert.Contains(refreshed, change => change.Path == "leave.txt" && !change.IsStaged && change.IsUnstaged);
    }

    [Fact]
    public async Task UnstageFilesOnlyResetsSelectedIndexEntriesAndPreservesWorkingTreeBytes()
    {
        InitializeRepository(withCommit: true);
        CommitFile("one.cs", "one A\n", "add one");
        CommitFile("two.cs", "two A\n", "add two");

        File.WriteAllText(Path.Combine(_temporaryDirectory, "one.cs"), "one B\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "two.cs"), "two B\n");
        RunGit(_temporaryDirectory, "add", "--", "one.cs", "two.cs");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "one.cs"), "one C\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "two.cs"), "two C\n");
        var oneBytes = File.ReadAllBytes(Path.Combine(_temporaryDirectory, "one.cs"));
        var twoBytes = File.ReadAllBytes(Path.Combine(_temporaryDirectory, "two.cs"));

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        var before = await service.ReadAsync(repository);
        var one = before.Changes.Single(change => change.Path == "one.cs");
        Assert.True(one.IsStaged && one.IsUnstaged);

        await service.UnstageFilesAsync(repository, [one]);

        Assert.Equal(oneBytes, File.ReadAllBytes(Path.Combine(_temporaryDirectory, "one.cs")));
        Assert.Equal(twoBytes, File.ReadAllBytes(Path.Combine(_temporaryDirectory, "two.cs")));
        var after = (await service.ReadAsync(repository)).Changes;
        Assert.Contains(after, change => change.Path == "one.cs" && !change.IsStaged && change.IsUnstaged);
        Assert.Contains(after, change => change.Path == "two.cs" && change.IsStaged && change.IsUnstaged);
    }

    [Fact]
    public async Task SameFileCanMoveBetweenStagedAndUnstagedWithoutLosingWorkingTreeContent()
    {
        InitializeRepository(withCommit: true);
        CommitFile("Foo.cs", "A\n", "base");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "Foo.cs"), "B\n");
        RunGit(_temporaryDirectory, "add", "--", "Foo.cs");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "Foo.cs"), "C\n");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        var both = Assert.Single((await service.ReadAsync(repository)).Changes);
        Assert.True(both.IsStaged && both.IsUnstaged);

        await service.UnstageFilesAsync(repository, [both]);

        var unstaged = Assert.Single((await service.ReadAsync(repository)).Changes);
        Assert.False(unstaged.IsStaged);
        Assert.True(unstaged.IsUnstaged);
        Assert.Equal("C\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "Foo.cs")));
        Assert.Equal("A", RunGitOutput(_temporaryDirectory, "show", "HEAD:Foo.cs").Trim());

        await service.StageFilesAsync(repository, [unstaged]);

        var staged = Assert.Single((await service.ReadAsync(repository)).Changes);
        Assert.True(staged.IsStaged);
        Assert.False(staged.IsUnstaged);
        Assert.Equal("C", RunGitOutput(_temporaryDirectory, "show", ":Foo.cs").Trim());
    }

    [Fact]
    public async Task UnstageFilesInUnbornRepositoryKeepsModifiedWorkingTreeVersion()
    {
        InitializeRepository(withCommit: false);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "Foo.cs"), "A\n");
        RunGit(_temporaryDirectory, "add", "--", "Foo.cs");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "Foo.cs"), "B\n");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        var change = Assert.Single((await service.ReadAsync(repository)).Changes);

        await service.UnstageFilesAsync(repository, [change]);

        Assert.Equal("B\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "Foo.cs")));
        Assert.Equal(string.Empty, RunGitOutput(_temporaryDirectory, "ls-files"));
        Assert.Contains("?? Foo.cs", RunGitOutput(_temporaryDirectory, "status", "--porcelain", "--untracked-files=all"));
    }

    [Fact]
    public async Task UnstageAllInUnbornRepositoryEmptiesIndexAndPreservesFiles()
    {
        InitializeRepository(withCommit: false);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "A.cs"), "A\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "B.cs"), "B\n");
        RunGit(_temporaryDirectory, "add", "--", "A.cs", "B.cs");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        await service.UnstageAllAsync(repository);

        Assert.Equal(string.Empty, RunGitOutput(_temporaryDirectory, "ls-files"));
        Assert.Equal("A\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "A.cs")));
        Assert.Equal("B\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "B.cs")));
        var status = RunGitOutput(_temporaryDirectory, "status", "--porcelain", "--untracked-files=all");
        Assert.Contains("?? A.cs", status);
        Assert.Contains("?? B.cs", status);
    }

    [Fact]
    public async Task UnstageAllResetsIndexToHeadWithoutChangingWorkingTree()
    {
        InitializeRepository(withCommit: true);
        CommitFile("one.cs", "A\n", "add one");
        CommitFile("two.cs", "A\n", "add two");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "one.cs"), "B\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "two.cs"), "B\n");
        RunGit(_temporaryDirectory, "add", "--all");
        File.AppendAllText(Path.Combine(_temporaryDirectory, "one.cs"), "C\n");
        var oneBytes = File.ReadAllBytes(Path.Combine(_temporaryDirectory, "one.cs"));
        var twoBytes = File.ReadAllBytes(Path.Combine(_temporaryDirectory, "two.cs"));

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);
        await service.UnstageAllAsync(repository);

        Assert.Equal(oneBytes, File.ReadAllBytes(Path.Combine(_temporaryDirectory, "one.cs")));
        Assert.Equal(twoBytes, File.ReadAllBytes(Path.Combine(_temporaryDirectory, "two.cs")));
        Assert.DoesNotContain((await service.ReadAsync(repository)).Changes, change => change.IsStaged);
    }

    [Fact]
    public async Task BatchApiRejectsNullEmptyAndInvalidPaths()
    {
        InitializeRepository(withCommit: false);
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_temporaryDirectory);

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.StageFilesAsync(repository, null!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.StageFilesAsync(repository, []));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UnstageFilesAsync(repository, [new WorkingTreeChange(Path.GetFullPath("absolute.cs"), ' ', 'M')]));
    }

    private void InitializeRepository(bool withCommit)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit(_temporaryDirectory, "init", "-b", "main");
        RunGit(_temporaryDirectory, "config", "user.email", "tests@example.invalid");
        RunGit(_temporaryDirectory, "config", "user.name", "CSharpGit Tests");
        if (withCommit) CommitFile("root.txt", "root\n", "initial");
    }

    private void CommitFile(string name, string contents, string message)
    {
        File.WriteAllText(Path.Combine(_temporaryDirectory, name), contents);
        RunGit(_temporaryDirectory, "add", "--", name);
        RunGit(_temporaryDirectory, "commit", "-m", message);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_temporaryDirectory)) return;
        foreach (var file in Directory.EnumerateFiles(_temporaryDirectory, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        try { Directory.Delete(_temporaryDirectory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void RunGit(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = directory };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static string RunGitOutput(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return output;
    }
}
