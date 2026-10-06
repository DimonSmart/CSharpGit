using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class StashViewerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-stash-viewer-{Guid.NewGuid():N}");

    public StashViewerTests()
    {
        Directory.CreateDirectory(_root);
        Git("init", "-b", "main");
        Git("config", "user.email", "tests@example.invalid");
        Git("config", "user.name", "CSharpGit Tests");
        File.WriteAllText(Path.Combine(_root, "file-a.txt"), "A\n");
        File.WriteAllText(Path.Combine(_root, "file-b.txt"), "A\n");
        File.WriteAllText(Path.Combine(_root, "file-c.txt"), "A\n");
        File.WriteAllText(Path.Combine(_root, "unchanged.txt"), "unchanged\n");
        Git("add", ".");
        Git("commit", "-m", "Initial");
    }

    [Fact]
    public async Task ViewerClassifiesStagedUnstagedAndCombinedTrackedChanges()
    {
        File.WriteAllText(Path.Combine(_root, "file-a.txt"), "staged\n");
        Git("add", "file-a.txt");
        File.WriteAllText(Path.Combine(_root, "file-b.txt"), "unstaged\n");
        File.WriteAllText(Path.Combine(_root, "file-c.txt"), "index\n");
        Git("add", "file-c.txt");
        File.WriteAllText(Path.Combine(_root, "file-c.txt"), "working\n");

        var (repository, mutations, reader, _) = await CreateServicesAsync();
        await mutations.CreateStashAsync(repository);
        var details = await reader.ReadAsync(repository, CurrentStash());

        var byPath = details.Changes.ToDictionary(change => change.File.Path, StringComparer.Ordinal);
        Assert.Equal(StashChangeState.Staged, byPath["file-a.txt"].State);
        Assert.Equal(StashChangeState.Unstaged, byPath["file-b.txt"].State);
        Assert.Equal(StashChangeState.StagedAndUnstaged, byPath["file-c.txt"].State);
        Assert.True(byPath["file-a.txt"].HasCombinedDiff);
        Assert.True(byPath["file-b.txt"].HasCombinedDiff);
        Assert.True(byPath["file-c.txt"].HasCombinedDiff);
    }

    [Fact]
    public async Task NetZeroTrackedFileRemainsVisible()
    {
        File.WriteAllText(Path.Combine(_root, "file-a.txt"), "B\n");
        Git("add", "file-a.txt");
        File.WriteAllText(Path.Combine(_root, "file-a.txt"), "A\n");

        var (repository, mutations, reader, _) = await CreateServicesAsync();
        await mutations.CreateStashAsync(repository);
        var details = await reader.ReadAsync(repository, CurrentStash());

        var change = Assert.Single(details.Changes, item =>
            item.File.Path == "file-a.txt");
        Assert.Equal(StashChangeState.StagedAndUnstaged, change.State);
        Assert.False(change.HasCombinedDiff);
        Assert.Equal(0, change.File.AddedLines);
        Assert.Equal(0, change.File.RemovedLines);
    }

    [Fact]
    public async Task UntrackedFilesComeFromAdditionalStashParent()
    {
        File.WriteAllText(Path.Combine(_root, "file-a.txt"), "changed\n");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "notes\n");

        var (repository, mutations, reader, history) = await CreateServicesAsync();
        await mutations.CreateStashAsync(
            repository,
            new CreateStashRequest(
                null,
                StashScope.AllTrackedChanges,
                IncludeUntracked: true));
        var details = await reader.ReadAsync(repository, CurrentStash());

        Assert.NotNull(details.UntrackedCommit);
        var change = Assert.Single(details.Changes, item =>
            item.File.Path == "notes.txt");
        Assert.Equal(StashChangeState.Untracked, change.State);

        var diff = await history.ReadDiffAsync(
            repository,
            details.UntrackedCommit!,
            parentHash: null,
            change.File);
        Assert.False(diff.IsBinary);
        Assert.Contains(diff.Lines, line =>
            line.Kind == DiffLineKind.Added && line.Text.Contains("notes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BinaryUntrackedFileUsesBinaryMetadata()
    {
        File.WriteAllText(Path.Combine(_root, "file-a.txt"), "changed\n");
        File.WriteAllBytes(Path.Combine(_root, "image.bin"), [0, 1, 2, 0, 3, 4]);

        var (repository, mutations, reader, _) = await CreateServicesAsync();
        await mutations.CreateStashAsync(
            repository,
            new CreateStashRequest(
                null,
                StashScope.AllTrackedChanges,
                IncludeUntracked: true));
        var details = await reader.ReadAsync(repository, CurrentStash());

        var binary = Assert.Single(details.Changes, item =>
            item.File.Path == "image.bin");
        Assert.Equal(StashChangeState.Untracked, binary.State);
        Assert.True(binary.File.IsBinary);
    }

    [Fact]
    public async Task TrackedSnapshotUsesWTreeAndDoesNotContainSavedUntrackedFiles()
    {
        File.WriteAllText(Path.Combine(_root, "file-a.txt"), "changed\n");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "notes\n");

        var (repository, mutations, reader, _) = await CreateServicesAsync();
        await mutations.CreateStashAsync(
            repository,
            new CreateStashRequest(
                null,
                StashScope.AllTrackedChanges,
                IncludeUntracked: true));
        var stash = CurrentStash();
        var details = await reader.ReadAsync(repository, stash);

        var executor = GitTestServices.CreateExecutor();
        var snapshotService = new GitRepositorySnapshotService(executor);
        var snapshot = await snapshotService.ReadTreeAsync(repository, details.Stash.Commit);
        var paths = snapshot.Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("unchanged.txt", paths);
        Assert.Contains("file-a.txt", paths);
        Assert.DoesNotContain("notes.txt", paths);
    }

    [Fact]
    public async Task RenameIsKeptAsOneRenameAwareChange()
    {
        Git("mv", "file-a.txt", "renamed-a.txt");

        var (repository, mutations, reader, _) = await CreateServicesAsync();
        await mutations.CreateStashAsync(repository);
        var details = await reader.ReadAsync(repository, CurrentStash());

        var renamed = Assert.Single(details.Changes, change =>
            change.File.Path == "renamed-a.txt");
        Assert.Equal("file-a.txt", renamed.File.OriginalPath);
        Assert.Equal("R", renamed.File.Status);
        Assert.Equal(StashChangeState.Staged, renamed.State);
    }

    private async Task<(Repository Repository, GitStashMutationService Mutations, IStashService Reader, IHistoryService History)> CreateServicesAsync()
    {
        var executor = GitTestServices.CreateExecutor();
        var repository = await new GitRepositoryService(executor).OpenAsync(_root);
        var history = new GitHistoryService(
            new GitCommitHistoryReader(executor),
            executor);
        return (
            repository,
            GitTestServices.CreateStashMutationService(executor),
            new GitStashService(history),
            history);
    }

    private GitStash CurrentStash()
    {
        var fields = GitOut("stash", "list", "-1", "--format=%gd%x00%H%x00%gs")
            .Split('\0', 3);
        Assert.Equal(3, fields.Length);
        return new GitStash(fields[0], fields[1], fields[2]);
    }

    private void Git(params string[] arguments)
    {
        var result = RunGit(arguments);
        Assert.True(result.Success, $"git {string.Join(' ', arguments)} failed: {result.Error}");
    }

    private string GitOut(params string[] arguments)
    {
        var result = RunGit(arguments);
        Assert.True(result.Success, $"git {string.Join(' ', arguments)} failed: {result.Error}");
        return result.Output.Trim();
    }

    private (bool Success, string Output, string Error) RunGit(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git did not start.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode == 0, output, error);
    }

    public void Dispose() => TestDirectory.Delete(_root);
}
