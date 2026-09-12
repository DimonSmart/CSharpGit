using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class RepositoryFileVersionServiceTests : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(Path.GetTempPath(), $"csharpgit-file-versions-{Guid.NewGuid():N}");
    private readonly GitCliRepositoryService _repositoryService = new();
    private readonly GitRepositoryFileVersionService _versionService = new();

    [Fact]
    public async Task CommitModifiedMaterializesExactOriginalAndChangedBytes()
    {
        var repository = await CreateRepositoryAsync();
        var original = new byte[] { 0, 1, 2, 0, 255, 10 };
        var changed = new byte[] { 0, 1, 3, 0, 254, 10 };
        CommitBytes("asset.bin", original, "binary base");
        CommitBytes("asset.bin", changed, "binary changed");
        var commit = RunGitOutput("rev-parse", "HEAD");

        var pair = await _versionService.ResolveCommitAsync(repository, commit, "asset.bin");
        var originalSnapshot = await _versionService.MaterializeAsync(repository, pair.Original, DiffFileSide.Original);
        var changedSnapshot = await _versionService.MaterializeAsync(repository, pair.Changed, DiffFileSide.Changed);

        Assert.Equal(original, await File.ReadAllBytesAsync(originalSnapshot.Path));
        Assert.Equal(changed, await File.ReadAllBytesAsync(changedSnapshot.Path));
        Assert.Equal(changed, await File.ReadAllBytesAsync(Path.Combine(_temporaryDirectory, "asset.bin")));
    }

    [Fact]
    public async Task RootAddedAndDeletedExposeOnlyExistingDiffSide()
    {
        var repository = await CreateRepositoryAsync();
        CommitText("root.txt", "root\n", "root commit");
        var rootCommit = RunGitOutput("rev-parse", "HEAD");

        var root = await _versionService.ResolveCommitAsync(repository, rootCommit, "root.txt");
        Assert.False(root.Original.CanOpen);
        Assert.True(root.Changed.CanOpen);

        File.WriteAllText(Path.Combine(_temporaryDirectory, "added.txt"), "added\n");
        RunGit("add", "added.txt");
        RunGit("commit", "-m", "add file");
        var addedCommit = RunGitOutput("rev-parse", "HEAD");
        var added = await _versionService.ResolveCommitAsync(repository, addedCommit, "added.txt");
        Assert.False(added.Original.CanOpen);
        Assert.True(added.Changed.CanOpen);

        File.Delete(Path.Combine(_temporaryDirectory, "added.txt"));
        RunGit("add", "-A");
        RunGit("commit", "-m", "delete file");
        var deletedCommit = RunGitOutput("rev-parse", "HEAD");
        var deleted = await _versionService.ResolveCommitAsync(repository, deletedCommit, "added.txt");
        Assert.True(deleted.Original.CanOpen);
        Assert.False(deleted.Changed.CanOpen);
    }

    [Fact]
    public async Task RenameKeepsOldAndNewPathsAndHistoryMetadata()
    {
        var repository = await CreateRepositoryAsync();
        var contents = string.Join('\n', Enumerable.Range(1, 40).Select(index => $"line {index}")) + "\n";
        CommitText("old name.txt", contents, "rename base");
        RunGit("mv", "old name.txt", "new name.txt");
        RunGit("commit", "-m", "rename");
        var commit = RunGitOutput("rev-parse", "HEAD");

        var pair = await _versionService.ResolveCommitAsync(repository, commit, "new name.txt");
        Assert.Equal("old name.txt", pair.Original.GitPath);
        Assert.Equal("new name.txt", pair.Changed.GitPath);
        Assert.Equal("R", pair.Status);

        var history = new GitFileAwareHistoryService(new GitReferenceHistoryService());
        var details = await history.ReadCommitAsync(repository, commit);
        var file = Assert.Single(details.Files);
        Assert.Equal("R", file.Status);
        Assert.Equal("old name.txt", file.OriginalPath);
        Assert.Equal("new name.txt", file.Path);

        var diff = await history.ReadDiffAsync(repository, commit, file.Path);
        var text = string.Join('\n', diff.Lines.Select(line => line.Text));
        Assert.Contains("rename from old name.txt", text);
        Assert.Contains("rename to new name.txt", text);
    }

    [Fact]
    public async Task CopyUsesGitResolvedSourceAndDestinationPaths()
    {
        var repository = await CreateRepositoryAsync();
        var contents = string.Join('\n', Enumerable.Range(1, 40).Select(index => $"line {index}")) + "\n";
        CommitText("source.txt", contents, "copy base");
        File.Copy(Path.Combine(_temporaryDirectory, "source.txt"), Path.Combine(_temporaryDirectory, "copy.txt"));
        File.AppendAllText(Path.Combine(_temporaryDirectory, "source.txt"), "source changed\n");
        RunGit("add", "source.txt", "copy.txt");
        RunGit("commit", "-m", "copy file");
        var commit = RunGitOutput("rev-parse", "HEAD");

        var pair = await _versionService.ResolveCommitAsync(repository, commit, "copy.txt");

        Assert.Equal("source.txt", pair.Original.GitPath);
        Assert.Equal("copy.txt", pair.Changed.GitPath);
        Assert.Equal("C", pair.Status);
    }

    [Fact]
    public async Task StagedAndUnstagedVersionsMatchDisplayedDiffSides()
    {
        var repository = await CreateRepositoryAsync();
        CommitText("value.txt", "value = 1\n", "base");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "value.txt"), "value = 2\n");
        RunGit("add", "value.txt");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "value.txt"), "value = 3\n");

        var state = await _repositoryService.ReadAsync(repository);
        var change = Assert.Single(state.Changes, item => item.Path == "value.txt");

        var staged = await _versionService.ResolveWorkingTreeAsync(repository, change, WorkingTreeDiffKind.Staged);
        var stagedOriginal = await _versionService.MaterializeAsync(repository, staged.Original, DiffFileSide.Original);
        var stagedChanged = await _versionService.MaterializeAsync(repository, staged.Changed, DiffFileSide.Changed);
        Assert.Equal("value = 1\n", await File.ReadAllTextAsync(stagedOriginal.Path));
        Assert.Equal("value = 2\n", await File.ReadAllTextAsync(stagedChanged.Path));

        var unstaged = await _versionService.ResolveWorkingTreeAsync(repository, change, WorkingTreeDiffKind.Unstaged);
        var unstagedOriginal = await _versionService.MaterializeAsync(repository, unstaged.Original, DiffFileSide.Original);
        Assert.Equal("value = 2\n", await File.ReadAllTextAsync(unstagedOriginal.Path));
        Assert.Equal(DiffFileVersionLocation.WorkingCopy, unstaged.Changed.Location);
        Assert.Equal("value = 3\n", await File.ReadAllTextAsync(Path.Combine(_temporaryDirectory, unstaged.Changed.GitPath)));
    }

    [Fact]
    public async Task UntrackedAndWorkingTreeDeletedExposeOnlyExistingSide()
    {
        var repository = await CreateRepositoryAsync();
        CommitText("deleted.txt", "tracked\n", "base");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "new file.txt"), "new\n");
        File.Delete(Path.Combine(_temporaryDirectory, "deleted.txt"));

        var state = await _repositoryService.ReadAsync(repository);
        var untracked = Assert.Single(state.Changes, item => item.Path == "new file.txt");
        var deleted = Assert.Single(state.Changes, item => item.Path == "deleted.txt");

        var untrackedPair = await _versionService.ResolveWorkingTreeAsync(repository, untracked, WorkingTreeDiffKind.Unstaged);
        Assert.False(untrackedPair.Original.CanOpen);
        Assert.True(untrackedPair.Changed.CanOpen);
        Assert.Equal(DiffFileVersionLocation.WorkingCopy, untrackedPair.Changed.Location);

        var deletedPair = await _versionService.ResolveWorkingTreeAsync(repository, deleted, WorkingTreeDiffKind.Unstaged);
        Assert.True(deletedPair.Original.CanOpen);
        Assert.False(deletedPair.Changed.CanOpen);
    }

    [Fact]
    public async Task HandlesUnicodeAndSpacesWithoutPathParsingAmbiguity()
    {
        var repository = await CreateRepositoryAsync();
        const string path = "folder name/данные 日本語.txt";
        Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "folder name"));
        CommitText(path, "старое\n", "unicode base");
        CommitText(path, "новое\n", "unicode changed");
        var commit = RunGitOutput("rev-parse", "HEAD");

        var pair = await _versionService.ResolveCommitAsync(repository, commit, path);
        var original = await _versionService.MaterializeAsync(repository, pair.Original, DiffFileSide.Original);
        var changed = await _versionService.MaterializeAsync(repository, pair.Changed, DiffFileSide.Changed);

        Assert.Equal("старое\n", await File.ReadAllTextAsync(original.Path));
        Assert.Equal("новое\n", await File.ReadAllTextAsync(changed.Path));
    }

    private async Task<Repository> CreateRepositoryAsync()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit("init", "-b", "main");
        RunGit("config", "user.email", "tests@example.invalid");
        RunGit("config", "user.name", "CSharpGit Tests");
        return await _repositoryService.OpenAsync(_temporaryDirectory);
    }

    private void CommitText(string path, string contents, string message)
    {
        var fullPath = Path.Combine(_temporaryDirectory, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
        RunGit("add", "--", path);
        RunGit("commit", "-m", message);
    }

    private void CommitBytes(string path, byte[] contents, string message)
    {
        File.WriteAllBytes(Path.Combine(_temporaryDirectory, path), contents);
        RunGit("add", "--", path);
        RunGit("commit", "-m", message);
    }

    private string RunGitOutput(params string[] arguments)
    {
        var (output, _) = RunGitCore(arguments);
        return output.Trim();
    }

    private void RunGit(params string[] arguments) => RunGitCore(arguments);

    private (string Output, string Error) RunGitCore(params string[] arguments)
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
        return (output, error);
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
