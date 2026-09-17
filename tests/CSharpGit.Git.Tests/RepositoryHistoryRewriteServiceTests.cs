using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class RepositoryHistoryRewriteServiceTests : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(Path.GetTempPath(), $"csharpgit-history-rewrite-{Guid.NewGuid():N}");
    private readonly List<string> _backupPaths = [];
    private readonly GitCliRepositoryService _repositoryService = new();
    private readonly GitRepositoryHistoryRewriteService _rewriteService = new();

    [Theory]
    [InlineData("")]
    [InlineData("../secret.txt")]
    [InlineData("./secret.txt")]
    [InlineData("/secret.txt")]
    [InlineData("folder//secret.txt")]
    [InlineData("folder\\secret.txt")]
    [InlineData("C:/secret.txt")]
    public async Task InvalidRepositoryRelativePathIsRejected(string path)
    {
        var repository = await CreateRepositoryAsync();
        var exception = await Assert.ThrowsAsync<RepositoryHistoryRewriteException>(
            () => _rewriteService.AnalyzePathRemovalAsync(repository, path));

        Assert.Equal(HistoryRewriteFailureKind.InvalidPath, exception.Kind);
    }


    [Theory]
    [InlineData("-leading-dash.txt")]
    [InlineData("folder/a;b$(...).zip")]
    [InlineData("colon:name.txt")]
    [InlineData("folder\\literal-name.txt")]
    public void OpaqueGitPathPunctuationIsAccepted(string path)
    {
        GitRepositoryHistoryRewriteService.ValidatePath(path);
    }

    [Fact]
    public async Task RewriteRemovesPathAndPreservesReferenceAndRemoteTopology()
    {
        var repository = await CreateRepositoryAsync();

        var targetPath = "assets/big file-данные.bin";
        WriteFile(targetPath, "remove me\n");
        WriteFile("keep.txt", "keep v1\n");
        RunGit("add", ".");
        RunGit("commit", "-m", "add files");

        WriteFile("keep.txt", "keep v2\n");
        RunGit("add", "--", "keep.txt");
        RunGit("commit", "-m", "change kept file");
        RunGit("tag", "archive");

        RunGit("remote", "add", "origin", "https://example.invalid/repository.git");
        RunGit("update-ref", "refs/remotes/origin/main", "HEAD");
        RunGit("branch", "--set-upstream-to=origin/main", "main");

        var tool = await _rewriteService.GetToolStatusAsync(repository);
        if (!tool.IsAvailable) return;

        var originalHead = RunGitOutput("rev-parse", "HEAD");
        var analysis = await _rewriteService.AnalyzePathRemovalAsync(repository, targetPath);

        Assert.True(analysis.PathHistoryCommitCount > 0);
        Assert.Contains("main", analysis.AffectedLocalBranches);
        Assert.Contains("archive", analysis.AffectedTags);
        Assert.Contains("origin/main", analysis.AffectedRemoteTrackingBranches);

        var result = await _rewriteService.RemovePathFromHistoryAsync(repository, targetPath);
        _backupPaths.Add(result.BackupPath);

        Assert.True(Directory.Exists(result.BackupPath));
        Assert.True(result.RewrittenCommitCount > 0);
        Assert.NotEqual(originalHead, result.HeadObjectId);
        Assert.Empty(RunGitOutput("rev-list", "--all", "--", targetPath));
        Assert.Equal("keep v2", RunGitOutput("show", "HEAD:keep.txt"));
        Assert.Equal("https://example.invalid/repository.git", RunGitOutput("remote", "get-url", "origin"));
        Assert.Equal("+refs/heads/*:refs/remotes/origin/*", RunGitOutput("config", "--get", "remote.origin.fetch"));
        Assert.Equal("origin", RunGitOutput("config", "--get", "branch.main.remote"));
        Assert.Equal("refs/heads/main", RunGitOutput("config", "--get", "branch.main.merge"));
        Assert.NotEmpty(RunGitOutput("show-ref", "--verify", "refs/heads/main"));
        Assert.NotEmpty(RunGitOutput("show-ref", "--verify", "refs/tags/archive"));
        Assert.NotEmpty(RunGitOutput("show-ref", "--verify", "refs/remotes/origin/main"));
        Assert.Empty(RunGitOutput("status", "--porcelain=v1", "-uall"));
    }

    [Fact]
    public async Task DirtyWorkingTreeBlocksAnalysisBeforeBackupOrRewrite()
    {
        var repository = await CreateRepositoryAsync();
        WriteFile("tracked.txt", "base\n");
        RunGit("add", ".");
        RunGit("commit", "-m", "base");

        var tool = await _rewriteService.GetToolStatusAsync(repository);
        if (!tool.IsAvailable) return;

        WriteFile("tracked.txt", "dirty\n");

        var exception = await Assert.ThrowsAsync<RepositoryHistoryRewriteException>(
            () => _rewriteService.AnalyzePathRemovalAsync(repository, "tracked.txt"));

        Assert.Equal(HistoryRewriteFailureKind.UnsafeRepositoryState, exception.Kind);
        Assert.Contains("uncommitted", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UntrackedFileBlocksAnalysis()
    {
        var repository = await CreateRepositoryAsync();
        WriteFile("tracked.txt", "base\n");
        RunGit("add", ".");
        RunGit("commit", "-m", "base");

        var tool = await _rewriteService.GetToolStatusAsync(repository);
        if (!tool.IsAvailable) return;

        WriteFile("untracked.txt", "untracked\n");

        var exception = await Assert.ThrowsAsync<RepositoryHistoryRewriteException>(
            () => _rewriteService.AnalyzePathRemovalAsync(repository, "tracked.txt"));

        Assert.Equal(HistoryRewriteFailureKind.UnsafeRepositoryState, exception.Kind);
        Assert.Contains("untracked", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Repository> CreateRepositoryAsync()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit("init", "-b", "main");
        RunGit("config", "user.email", "tests@example.invalid");
        RunGit("config", "user.name", "CSharpGit Tests");
        return await _repositoryService.OpenAsync(_temporaryDirectory);
    }

    private void WriteFile(string path, string content)
    {
        var fullPath = Path.Combine(_temporaryDirectory, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
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
        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed: {output}\n{error}");
        return output.Trim();
    }

    private void RunGit(params string[] arguments) => _ = RunGitOutput(arguments);

    public void Dispose()
    {
        TestDirectory.Delete(_temporaryDirectory);
        foreach (var backupPath in _backupPaths)
            TestDirectory.Delete(backupPath);
    }
}
