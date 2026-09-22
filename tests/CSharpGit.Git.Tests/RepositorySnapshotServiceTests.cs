using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class RepositorySnapshotServiceTests : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(Path.GetTempPath(), $"csharpgit-snapshot-{Guid.NewGuid():N}");
    private readonly GitRepositoryService _repositoryService = new();
    private readonly GitRepositorySnapshotService _snapshotService = new();
    private readonly GitRepositoryFileVersionService _fileVersionService = new();

    [Fact]
    public async Task ReadTreeReturnsExactTrackedSnapshotAndEntryKinds()
    {
        var repository = await CreateRepositoryAsync();
        Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "src"));
        File.WriteAllText(Path.Combine(_temporaryDirectory, "README.md"), "root\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "src", "First.cs"), "A\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "src", "Second.cs"), "B\n");
        RunGit("add", ".");
        RunGit("commit", "-m", "snapshot");
        var commit = RunGitOutput("rev-parse", "HEAD");

        File.WriteAllText(Path.Combine(_temporaryDirectory, "untracked.txt"), "not tracked\n");
        File.Delete(Path.Combine(_temporaryDirectory, "README.md"));

        var entries = await _snapshotService.ReadTreeAsync(repository, commit);

        Assert.Contains(entries, entry => entry.Path == "README.md" && entry.Kind == RepositorySnapshotEntryKind.File);
        Assert.Contains(entries, entry => entry.Path == "src/First.cs");
        Assert.Contains(entries, entry => entry.Path == "src/Second.cs");
        Assert.DoesNotContain(entries, entry => entry.Path == "untracked.txt");
        Assert.Equal(entries.Count, entries.Select(entry => entry.Path).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TreeParserIsNulSafeAndMapsGitModes()
    {
        var output = string.Join('\0',
            "100644 blob aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\tfolder name/данные 日本語.txt",
            "100755 blob bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\texecutable",
            "120000 blob cccccccccccccccccccccccccccccccccccccccc\tlink:name",
            "160000 commit dddddddddddddddddddddddddddddddddddddddd\tsubmodule",
            "100600 blob eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\tunsupported") + '\0';

        var entries = GitRepositorySnapshotService.ParseTree(output);

        Assert.Equal(5, entries.Count);
        Assert.Equal(RepositorySnapshotEntryKind.File, entries[0].Kind);
        Assert.Equal(RepositorySnapshotEntryKind.File, entries[1].Kind);
        Assert.Equal(RepositorySnapshotEntryKind.Symlink, entries[2].Kind);
        Assert.Equal(RepositorySnapshotEntryKind.Submodule, entries[3].Kind);
        Assert.Equal(RepositorySnapshotEntryKind.Unsupported, entries[4].Kind);
        Assert.Equal("folder name/данные 日本語.txt", entries[0].Path);
    }

    [Fact]
    public void ContentSearchParserPreservesNewlinesAndColonsInGitPaths()
    {
        var commit = new string('a', 40);
        var output = $"{commit}:folder\npart:name.txt\0" + "12\0matched text\n";

        var match = Assert.Single(GitRepositorySnapshotService.ParseContentSearch(output, commit));

        Assert.Equal("folder\npart:name.txt", match.Path);
        Assert.Equal(12, match.LineNumber);
        Assert.Equal("matched text", match.Snippet);
    }

    [Fact]
    public async Task ContentSearchIsLiteralCaseInsensitiveAndSkipsBinaryFiles()
    {
        var repository = await CreateRepositoryAsync();
        Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "folder-name"));
        File.WriteAllText(Path.Combine(_temporaryDirectory, "folder-name", "text file.txt"), "Prefix [Needle.*] suffix\n");
        File.WriteAllBytes(Path.Combine(_temporaryDirectory, "binary.bin"), [0, 1, 2, 0, 91, 78, 101, 101, 100, 108, 101, 46, 42, 93]);
        RunGit("add", ".");
        RunGit("commit", "-m", "search data");
        var commit = RunGitOutput("rev-parse", "HEAD");

        var matches = await _snapshotService.SearchContentAsync(repository, commit, "[needle.*]");
        var match = Assert.Single(matches);
        Assert.Equal("folder-name/text file.txt", match.Path);
        Assert.Equal(1, match.LineNumber);
        Assert.Contains("[Needle.*]", match.Snippet);

        Assert.Empty(await _snapshotService.SearchContentAsync(repository, commit, "does-not-exist"));
    }

    [Fact]
    public async Task ResolveUnchangedFileMaterializesSelectedCommitWithoutUsingWorkingTree()
    {
        var repository = await CreateRepositoryAsync();
        CommitText("stable.txt", "historical\n", "base");
        var selectedCommit = RunGitOutput("rev-parse", "HEAD");
        CommitText("other.txt", "later\n", "later commit");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "stable.txt"), "working tree changed\n");

        var version = await _snapshotService.ResolveFileVersionAsync(repository, selectedCommit, "stable.txt");
        var materialized = await _fileVersionService.MaterializeAsync(repository, version, DiffFileSide.Changed);

        Assert.True(version.CanOpen);
        Assert.Equal(selectedCommit, version.RevisionIdentity);
        Assert.Equal("historical\n", await File.ReadAllTextAsync(materialized.Path));
        Assert.Equal("working tree changed\n", await File.ReadAllTextAsync(Path.Combine(_temporaryDirectory, "stable.txt")));
    }

    [Fact]
    public async Task SnapshotReadSearchAndResolveDoNotMutateRepositoryState()
    {
        var repository = await CreateRepositoryAsync();
        CommitText("tracked.txt", "needle\n", "base");
        var commit = RunGitOutput("rev-parse", "HEAD");
        File.AppendAllText(Path.Combine(_temporaryDirectory, "tracked.txt"), "unstaged\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "untracked.txt"), "keep\n");
        var headBefore = RunGitOutput("rev-parse", "HEAD");
        var statusBefore = RunGitOutput("status", "--porcelain=v1", "-uall");

        _ = await _snapshotService.ReadTreeAsync(repository, commit);
        _ = await _snapshotService.SearchContentAsync(repository, commit, "needle");
        _ = await _snapshotService.ResolveFileVersionAsync(repository, commit, "tracked.txt");

        Assert.Equal(headBefore, RunGitOutput("rev-parse", "HEAD"));
        Assert.Equal(statusBefore, RunGitOutput("status", "--porcelain=v1", "-uall"));
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

    private string RunGitOutput(params string[] arguments)
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
        return output.Trim();
    }

    private void RunGit(params string[] arguments) => _ = RunGitOutput(arguments);

    public void Dispose() => TestDirectory.Delete(_temporaryDirectory);
}