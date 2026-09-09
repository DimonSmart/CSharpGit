using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class WorkingTreeDiffServiceTests : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(Path.GetTempPath(), $"csharpgit-working-tree-diff-{Guid.NewGuid():N}");
    private readonly GitCliRepositoryService _service = new();

    [Fact]
    public async Task SeparatesHeadIndexAndWorkingTreeVersions()
    {
        var repository = await CreateRepositoryAsync();
        CommitFile("value.txt", "value = 1\n", "value one");

        File.WriteAllText(Path.Combine(_temporaryDirectory, "value.txt"), "value = 2\n");
        RunGit("add", "value.txt");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "value.txt"), "value = 3\n");

        var change = await ReadChangeAsync(repository, "value.txt");
        Assert.True(change.IsStaged);
        Assert.True(change.IsUnstaged);

        var staged = await _service.ReadDiffAsync(repository, change, WorkingTreeDiffKind.Staged);
        var unstaged = await _service.ReadDiffAsync(repository, change, WorkingTreeDiffKind.Unstaged);

        var stagedText = Text(staged);
        Assert.Contains("-value = 1", stagedText);
        Assert.Contains("+value = 2", stagedText);
        Assert.DoesNotContain("value = 3", stagedText);

        var unstagedText = Text(unstaged);
        Assert.Contains("-value = 2", unstagedText);
        Assert.Contains("+value = 3", unstagedText);
        Assert.DoesNotContain("value = 1", unstagedText);
    }

    [Fact]
    public async Task ReadsUntrackedTextAndTreatsNoIndexExitCodeOneAsSuccess()
    {
        var repository = await CreateRepositoryAsync();
        const string path = "new file.cs";
        File.WriteAllText(Path.Combine(_temporaryDirectory, path), "using System;\nclass NewFile {}\n");

        var change = await ReadChangeAsync(repository, path);
        var diff = await _service.ReadDiffAsync(repository, change, WorkingTreeDiffKind.Unstaged);

        Assert.False(diff.IsBinary);
        Assert.Contains("+using System;", Text(diff));
        Assert.Contains("+class NewFile {}", Text(diff));
    }

    [Fact]
    public async Task RepresentsEmptyUntrackedFileAsNewFile()
    {
        var repository = await CreateRepositoryAsync();
        const string path = "empty.txt";
        File.WriteAllText(Path.Combine(_temporaryDirectory, path), string.Empty);

        var change = await ReadChangeAsync(repository, path);
        var diff = await _service.ReadDiffAsync(repository, change, WorkingTreeDiffKind.Unstaged);

        Assert.False(diff.IsBinary);
        Assert.NotEmpty(diff.Lines);
        Assert.Contains("empty", Text(diff), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadsStagedAddedFile()
    {
        var repository = await CreateRepositoryAsync();
        const string path = "added.txt";
        File.WriteAllText(Path.Combine(_temporaryDirectory, path), "added line\n");
        RunGit("add", path);

        var change = await ReadChangeAsync(repository, path);
        var diff = await _service.ReadDiffAsync(repository, change, WorkingTreeDiffKind.Staged);

        Assert.Contains("+added line", Text(diff));
    }

    [Fact]
    public async Task ReadsUnstagedAndStagedDeletion()
    {
        var repository = await CreateRepositoryAsync();
        CommitFile("deleted.txt", "one\ntwo\n", "deletion base");
        File.Delete(Path.Combine(_temporaryDirectory, "deleted.txt"));

        var unstagedChange = await ReadChangeAsync(repository, "deleted.txt");
        var unstaged = await _service.ReadDiffAsync(repository, unstagedChange, WorkingTreeDiffKind.Unstaged);
        Assert.Contains("-one", Text(unstaged));
        Assert.Contains("-two", Text(unstaged));

        RunGit("add", "-A");
        var stagedChange = await ReadChangeAsync(repository, "deleted.txt");
        var staged = await _service.ReadDiffAsync(repository, stagedChange, WorkingTreeDiffKind.Staged);
        Assert.Contains("-one", Text(staged));
        Assert.Contains("-two", Text(staged));
    }

    [Fact]
    public async Task PreservesRenameSemanticsWithAndWithoutContentChanges()
    {
        var repository = await CreateRepositoryAsync();
        var original = string.Join('\n', Enumerable.Range(1, 30).Select(index => $"line {index}")) + "\n";
        CommitFile("old name.txt", original, "rename base");
        RunGit("mv", "old name.txt", "new name.txt");

        var rename = await ReadChangeAsync(repository, "new name.txt");
        Assert.Equal("old name.txt", rename.OriginalPath);
        var renameDiff = await _service.ReadDiffAsync(repository, rename, WorkingTreeDiffKind.Staged);
        var renameText = Text(renameDiff);
        Assert.Contains("rename from old name.txt", renameText);
        Assert.Contains("rename to new name.txt", renameText);

        var changed = original.Replace("line 15\n", "line fifteen changed\n", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "new name.txt"), changed);
        RunGit("add", "new name.txt");

        var renameWithContent = await ReadChangeAsync(repository, "new name.txt");
        var contentDiff = await _service.ReadDiffAsync(repository, renameWithContent, WorkingTreeDiffKind.Staged);
        var contentText = Text(contentDiff);
        Assert.Contains("rename from old name.txt", contentText);
        Assert.Contains("rename to new name.txt", contentText);
        Assert.Contains("-line 15", contentText);
        Assert.Contains("+line fifteen changed", contentText);
    }

    [Fact]
    public async Task DetectsBinaryStagedTrackedUnstagedAndUntrackedFiles()
    {
        var repository = await CreateRepositoryAsync();
        File.WriteAllBytes(Path.Combine(_temporaryDirectory, "tracked.bin"), [0, 1, 0, 2, 3]);
        RunGit("add", "tracked.bin");
        RunGit("commit", "-m", "binary base");

        File.WriteAllBytes(Path.Combine(_temporaryDirectory, "tracked.bin"), [0, 8, 0, 9, 3]);
        var unstagedChange = await ReadChangeAsync(repository, "tracked.bin");
        Assert.True((await _service.ReadDiffAsync(repository, unstagedChange, WorkingTreeDiffKind.Unstaged)).IsBinary);

        RunGit("add", "tracked.bin");
        var stagedChange = await ReadChangeAsync(repository, "tracked.bin");
        Assert.True((await _service.ReadDiffAsync(repository, stagedChange, WorkingTreeDiffKind.Staged)).IsBinary);

        File.WriteAllBytes(Path.Combine(_temporaryDirectory, "untracked.bin"), [0, 4, 0, 5]);
        var untrackedChange = await ReadChangeAsync(repository, "untracked.bin");
        Assert.True((await _service.ReadDiffAsync(repository, untrackedChange, WorkingTreeDiffKind.Unstaged)).IsBinary);
    }

    [Fact]
    public async Task HandlesPathsWithSpacesAndUnicode()
    {
        var repository = await CreateRepositoryAsync();
        CommitFile("space name.txt", "old space\n", "space base");
        CommitFile("данные-日本語.txt", "старое 日本語\n", "unicode base");

        File.WriteAllText(Path.Combine(_temporaryDirectory, "space name.txt"), "new space\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "данные-日本語.txt"), "новое 日本語\n");

        var space = await ReadChangeAsync(repository, "space name.txt");
        var unicode = await ReadChangeAsync(repository, "данные-日本語.txt");

        Assert.Contains("+new space", Text(await _service.ReadDiffAsync(repository, space, WorkingTreeDiffKind.Unstaged)));
        Assert.Contains("+новое 日本語", Text(await _service.ReadDiffAsync(repository, unicode, WorkingTreeDiffKind.Unstaged)));
    }

    [Fact]
    public async Task ReadsStagedDiffBeforeFirstCommit()
    {
        var repository = await CreateRepositoryAsync(createInitialCommit: false);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "first.txt"), "first commit content\n");
        RunGit("add", "first.txt");

        var change = await ReadChangeAsync(repository, "first.txt");
        var diff = await _service.ReadDiffAsync(repository, change, WorkingTreeDiffKind.Staged);

        Assert.Contains("+first commit content", Text(diff));
    }

    [Fact]
    public async Task ReportsConflictWithoutPretendingItIsTwoWayDiff()
    {
        var repository = await CreateRepositoryAsync();
        var conflict = new WorkingTreeChange("conflict.txt", 'U', 'U');

        var diff = await _service.ReadDiffAsync(repository, conflict, WorkingTreeDiffKind.Unstaged);

        Assert.False(diff.IsBinary);
        Assert.Single(diff.Lines);
        Assert.Contains("Conflict", diff.Lines[0].Text);
    }

    [Fact]
    public async Task DiscardUnstagedRestoresIndexAndPreservesStagedContent()
    {
        var repository = await CreateRepositoryAsync();
        CommitFile("value.txt", "value = 1\n", "value base");

        File.WriteAllText(Path.Combine(_temporaryDirectory, "value.txt"), "value = 2\n");
        RunGit("add", "value.txt");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "value.txt"), "value = 3\n");

        var change = await ReadChangeAsync(repository, "value.txt");
        await _service.DiscardFileAsync(repository, change);

        Assert.Equal("value = 2\n", File.ReadAllText(Path.Combine(_temporaryDirectory, "value.txt")));
        var remaining = await ReadChangeAsync(repository, "value.txt");
        Assert.True(remaining.IsStaged);
        Assert.False(remaining.IsUnstaged);

        var staged = await _service.ReadDiffAsync(repository, remaining, WorkingTreeDiffKind.Staged);
        Assert.Contains("-value = 1", Text(staged));
        Assert.Contains("+value = 2", Text(staged));
    }

    private async Task<Repository> CreateRepositoryAsync(bool createInitialCommit = true)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit("init", "-b", "main");
        RunGit("config", "user.email", "tests@example.invalid");
        RunGit("config", "user.name", "CSharpGit Tests");
        if (createInitialCommit)
            CommitFile("seed.txt", "seed\n", "seed");
        return await _service.OpenAsync(_temporaryDirectory);
    }

    private void CommitFile(string path, string contents, string message)
    {
        File.WriteAllText(Path.Combine(_temporaryDirectory, path), contents);
        RunGit("add", "--", path);
        RunGit("commit", "-m", message);
    }

    private async Task<WorkingTreeChange> ReadChangeAsync(Repository repository, string path)
    {
        var state = await _service.ReadAsync(repository);
        return Assert.Single(state.Changes.Where(change =>
            string.Equals(change.Path, path, StringComparison.Ordinal)));
    }

    private static string Text(FileDiff diff) =>
        string.Join('\n', diff.Lines.Select(line => line.Text));

    private void RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _temporaryDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {output}\n{error}");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_temporaryDirectory)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(_temporaryDirectory, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
