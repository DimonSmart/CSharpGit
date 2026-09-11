using System.Diagnostics;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class CommitChangesServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-commit-changes-{Guid.NewGuid():N}");
    private readonly GitCliRepositoryService _repositoryService = new();
    private readonly GitRepositoryFileVersionService _versionService = new();

    [Fact]
    public async Task RootAndRenameUseOneProcessPerLazyOperationAndPreservePaths()
    {
        var repository = await CreateRepositoryAsync();
        CommitText("root.txt", "root\n", "root");
        var root = RunGitOutput("rev-parse", "HEAD");
        var service = CreateService();

        var beforeRoot = service.GitInvocationCount;
        var rootFiles = await service.ReadChangedFilesAsync(repository, root, null);
        Assert.Equal(beforeRoot + 1, service.GitInvocationCount);
        var rootFile = Assert.Single(rootFiles);
        Assert.Equal("A", rootFile.Status);
        Assert.Equal("root.txt", rootFile.Path);

        var lines = string.Join('\n', Enumerable.Range(1, 40).Select(index => $"line {index}")) + "\n";
        CommitText("old name.txt", lines, "base rename");
        RunGit("mv", "old name.txt", "new name.txt");
        RunGit("commit", "-m", "rename");
        var commit = RunGitOutput("rev-parse", "HEAD");
        var parent = RunGitOutput("rev-parse", "HEAD^1");

        var beforeFiles = service.GitInvocationCount;
        var files = await service.ReadChangedFilesAsync(repository, commit, parent);
        Assert.Equal(beforeFiles + 1, service.GitInvocationCount);
        var renamed = Assert.Single(files);
        Assert.Equal("R", renamed.Status);
        Assert.Equal("old name.txt", renamed.OriginalPath);
        Assert.Equal("new name.txt", renamed.Path);

        var beforeDiff = service.GitInvocationCount;
        var diff = await service.ReadDiffAsync(repository, commit, parent, renamed);
        Assert.Equal(beforeDiff + 1, service.GitInvocationCount);
        var text = string.Join('\n', diff.Lines.Select(line => line.Text));
        Assert.Contains("rename from old name.txt", text);
        Assert.Contains("rename to new name.txt", text);
    }

    [Fact]
    public async Task ChangedFilesCoverAddModifyDeleteCopyAndBinary()
    {
        var repository = await CreateRepositoryAsync();
        var source = string.Join('\n', Enumerable.Range(1, 50).Select(index => $"source {index}")) + "\n";
        CommitText("source.txt", source, "base");
        CommitText("delete.txt", "delete me\n", "delete base");
        CommitText("modify.txt", "old\n", "modify base");
        CommitBytes("binary.bin", [0, 1, 2, 0, 255], "binary base");
        var parent = RunGitOutput("rev-parse", "HEAD");

        File.Copy(Path.Combine(_temporaryDirectory, "source.txt"), Path.Combine(_temporaryDirectory, "copy.txt"));
        File.AppendAllText(Path.Combine(_temporaryDirectory, "source.txt"), "source changed\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "modify.txt"), "new\n");
        File.Delete(Path.Combine(_temporaryDirectory, "delete.txt"));
        File.WriteAllText(Path.Combine(_temporaryDirectory, "added.txt"), "added\n");
        File.WriteAllBytes(Path.Combine(_temporaryDirectory, "binary.bin"), [0, 1, 3, 0, 254]);
        RunGit("add", "-A");
        RunGit("commit", "-m", "mixed changes");
        var commit = RunGitOutput("rev-parse", "HEAD");

        var service = CreateService();
        var files = await service.ReadChangedFilesAsync(repository, commit, parent);
        Assert.Contains(files, file => file.Status == "A" && file.Path == "added.txt");
        Assert.Contains(files, file => file.Status == "M" && file.Path == "modify.txt");
        Assert.Contains(files, file => file.Status == "D" && file.Path == "delete.txt");
        Assert.Contains(files, file => file.Status == "C" && file.Path == "copy.txt" && file.OriginalPath == "source.txt");
        Assert.Contains(files, file => file.Path == "binary.bin" && file.IsBinary);
    }

    [Fact]
    public async Task MergeUsesExplicitFirstParentForFilesAndDiff()
    {
        var repository = await CreateRepositoryAsync();
        CommitText("base.txt", "base\n", "base");
        RunGit("checkout", "-b", "feature");
        CommitText("feature.txt", "feature\n", "feature");
        RunGit("checkout", "main");
        CommitText("main.txt", "main\n", "main");
        RunGit("merge", "--no-ff", "feature", "-m", "merge feature");
        var merge = RunGitOutput("rev-parse", "HEAD");
        var firstParent = RunGitOutput("rev-parse", "HEAD^1");

        var service = CreateService();
        var files = await service.ReadChangedFilesAsync(repository, merge, firstParent);
        var feature = Assert.Single(files, file => file.Path == "feature.txt");
        var diff = await service.ReadDiffAsync(repository, merge, firstParent, feature);
        Assert.Contains(diff.Lines, line => line.Text.Contains("+feature", StringComparison.Ordinal));
    }

    private GitFileAwareHistoryService CreateService() =>
        new(new GitReferenceHistoryService(), _versionService);

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

    public void Dispose() => TestDirectory.Delete(_temporaryDirectory);
}
