using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class LiteralGitPathspecIntegrationTests : IDisposable
{
    private const string Selected = "file[1].txt";
    private const string Neighbor = "file1.txt";
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"csharpgit-literal-paths-{Guid.NewGuid():N}");
    private readonly GitRepositoryService _repositories = GitTestServices.CreateRepositoryService();
    private readonly GitRepositoryStateService _state = GitTestServices.CreateRepositoryStateService();

    [Theory]
    [InlineData("file[1].txt")]
    [InlineData("file?.txt")]
    [InlineData("file*.txt")]
    [InlineData(":(glob)example.txt")]
    public void EscapesAllGitPathspecMagicWithoutModifyingFilename(string path)
    {
        Assert.Equal($":(literal){path}", GitPathValidator.LiteralPathspec(path));
    }

    [Fact]
    public async Task SingleAndBatchStagingAndUnstagingAffectOnlySelectedFile()
    {
        var repository = await InitializeAsync();
        Write(Selected, "selected base\n");
        Write(Neighbor, "neighbor base\n");
        CommitAll("base");
        Write(Selected, "selected changed\n");
        Write(Neighbor, "neighbor changed\n");

        var service = GitTestServices.CreateWorkingTreeService();
        await service.StageFileAsync(repository, await ReadChangeAsync(repository, Selected));
        Assert.Equal(Selected + "\0", RunGit("diff", "--cached", "--name-only", "-z"));

        await service.UnstageFileAsync(repository, await ReadChangeAsync(repository, Selected));
        Assert.Equal(string.Empty, RunGit("diff", "--cached", "--name-only", "-z"));

        await service.StageFilesAsync(repository, [await ReadChangeAsync(repository, Selected)]);
        Assert.Equal(Selected + "\0", RunGit("diff", "--cached", "--name-only", "-z"));

        await service.UnstageFilesAsync(repository, [await ReadChangeAsync(repository, Selected)]);
        Assert.Equal(string.Empty, RunGit("diff", "--cached", "--name-only", "-z"));

        await service.StageFilesAsync(repository, [await ReadChangeAsync(repository, Selected)]);
        await service.DiscardAllFileChangesAsync(repository, await ReadChangeAsync(repository, Selected));
        Assert.Equal(string.Empty, RunGit("diff", "--cached", "--name-only", "-z"));
        Assert.Equal("selected base\n", File.ReadAllText(Path.Combine(_directory, Selected)));
        Assert.Equal("neighbor changed\n", File.ReadAllText(Path.Combine(_directory, Neighbor)));
    }

    [Fact]
    public async Task StagedAndUnstagedDiffReadOnlyLiteralFilename()
    {
        var repository = await InitializeAsync();
        Write(Selected, "selected base\n");
        Write(Neighbor, "neighbor base\n");
        CommitAll("base");
        Write(Selected, "selected staged\n");
        Write(Neighbor, "neighbor staged\n");
        RunGit("add", "--all");

        var service = GitTestServices.CreateWorkingTreeDiffService();
        var selected = await ReadChangeAsync(repository, Selected);
        var staged = await service.ReadDiffAsync(repository, selected, WorkingTreeDiffKind.Staged);
        var stagedText = string.Join('\n', staged.Lines.Select(line => line.Text));
        Assert.Contains("+selected staged", stagedText);
        Assert.DoesNotContain("neighbor staged", stagedText);

        Write(Selected, "selected unstaged\n");
        Write(Neighbor, "neighbor unstaged\n");
        selected = await ReadChangeAsync(repository, Selected);
        var unstaged = await service.ReadDiffAsync(repository, selected, WorkingTreeDiffKind.Unstaged);
        var unstagedText = string.Join('\n', unstaged.Lines.Select(line => line.Text));
        Assert.Contains("+selected unstaged", unstagedText);
        Assert.DoesNotContain("neighbor unstaged", unstagedText);
    }

    [Fact]
    public async Task VersionsSnapshotsAndHistoricalDiffResolveExactFile()
    {
        var repository = await InitializeAsync();
        Write(Selected, "selected before\n");
        Write(Neighbor, "neighbor before\n");
        CommitAll("base");
        var parent = RunGit("rev-parse", "HEAD").Trim();

        var snapshot = GitTestServices.CreateRepositorySnapshotService();
        var versions = GitTestServices.CreateRepositoryFileVersionService();
        var historical = await snapshot.ResolveFileVersionAsync(repository, parent, Selected);
        var historicalFile = await versions.MaterializeAsync(repository, historical, DiffFileSide.Changed);
        Assert.Equal("selected before\n", File.ReadAllText(historicalFile.Path));

        Write(Selected, "selected after\n");
        Write(Neighbor, "neighbor after\n");
        RunGit("add", "--all");
        var staged = await versions.ResolveWorkingTreeAsync(
            repository, await ReadChangeAsync(repository, Selected), WorkingTreeDiffKind.Staged);
        var oldFile = await versions.MaterializeAsync(repository, staged.Original, DiffFileSide.Original);
        var newFile = await versions.MaterializeAsync(repository, staged.Changed, DiffFileSide.Changed);
        Assert.Equal("selected before\n", File.ReadAllText(oldFile.Path));
        Assert.Equal("selected after\n", File.ReadAllText(newFile.Path));

        RunGit("commit", "-m", "update both");
        var commit = RunGit("rev-parse", "HEAD").Trim();
        var history = GitTestServices.CreateHistoryService();
        var change = Assert.Single(
            await history.ReadChangedFilesAsync(repository, commit, parent),
            file => file.Path == Selected);
        var diff = await history.ReadDiffAsync(repository, commit, parent, change);
        var text = string.Join('\n', diff.Lines.Select(line => line.Text));
        Assert.Contains("+selected after", text);
        Assert.DoesNotContain("neighbor after", text);
    }

    [Fact]
    public async Task ConflictResolutionLeavesOtherUnmergedFileUntouched()
    {
        var repository = await InitializeAsync();
        Write(Selected, "selected base\n");
        Write(Neighbor, "neighbor base\n");
        CommitAll("base");
        RunGit("checkout", "-b", "other");
        Write(Selected, "selected theirs\n");
        Write(Neighbor, "neighbor theirs\n");
        CommitAll("other side");
        RunGit("checkout", "main");
        Write(Selected, "selected ours\n");
        Write(Neighbor, "neighbor ours\n");
        CommitAll("our side");
        Assert.NotEqual(0, RunGitExitCode("merge", "--no-edit", "other"));

        var state = await _state.ReadAsync(repository);
        var conflict = Assert.Single(state.CurrentOperation.Conflicts, item => item.Path == Selected);
        Assert.True(conflict.CanChooseCurrentLocal);
        Assert.True(conflict.CanStage);
        var service = GitTestServices.CreateConflictResolutionService();
        await service.ChooseConflictSideAsync(repository, conflict, ConflictResolutionSide.CurrentLocal);
        await service.StageResolvedConflictAsync(repository, conflict);

        var unmerged = RunGit("ls-files", "--unmerged", "-z");
        Assert.DoesNotContain(Selected, unmerged);
        Assert.Contains(Neighbor, unmerged);
        Assert.Equal("selected ours\n", File.ReadAllText(Path.Combine(_directory, Selected)));
    }

    private async Task<Repository> InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        RunGit("init", "-b", "main");
        RunGit("config", "user.name", "CSharpGit Tests");
        RunGit("config", "user.email", "tests@example.invalid");
        RunGit("config", "core.autocrlf", "false");
        return await _repositories.OpenAsync(_directory);
    }

    private async Task<WorkingTreeChange> ReadChangeAsync(Repository repository, string path) =>
        Assert.Single((await _state.ReadAsync(repository)).Changes, change => change.Path == path);

    private void Write(string path, string content) =>
        File.WriteAllText(Path.Combine(_directory, path), content);

    private void CommitAll(string message)
    {
        RunGit("add", "--all");
        RunGit("commit", "-m", message);
    }

    private string RunGit(params string[] arguments)
    {
        var (exitCode, output, error) = ExecuteGit(arguments);
        Assert.True(exitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
        return output;
    }

    private int RunGitExitCode(params string[] arguments) => ExecuteGit(arguments).ExitCode;

    private (int ExitCode, string Output, string Error) ExecuteGit(string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }

    public void Dispose() => TestDirectory.Delete(_directory);
}
