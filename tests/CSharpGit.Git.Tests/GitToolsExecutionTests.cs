using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Git;

namespace CSharpGit.Git.Tests;

[Collection(GitToolsEnvironmentCollection.CollectionName)]
public sealed class GitToolsExecutionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-git-tools-exec-{Guid.NewGuid():N}");
    private readonly string _home;
    private readonly string _repositoryPath;
    private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);
    private readonly RecordingExternalProcessService _externalProcess = new();
    private readonly GitCommandExecutor _executor;
    private readonly GitToolsService _service;
    private readonly Repository _repository;

    public GitToolsExecutionTests()
    {
        _home = Path.Combine(_root, "home");
        _repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_repositoryPath);

        SetEnvironment("HOME", _home);
        SetEnvironment("USERPROFILE", _home);
        SetEnvironment("XDG_CONFIG_HOME", Path.Combine(_home, ".config"));
        SetEnvironment("GIT_CONFIG_NOSYSTEM", "1");
        SetEnvironment("GIT_EDITOR", null);
        SetEnvironment("VISUAL", null);
        SetEnvironment("EDITOR", null);
        File.WriteAllText(Path.Combine(_home, ".gitconfig"), string.Empty);

        RunGit("init", "-b", "main");
        RunGit("config", "user.name", "CSharpGit Tests");
        RunGit("config", "user.email", "tests@example.invalid");

        _repository = new Repository(
            Path.GetFullPath(_repositoryPath),
            Path.GetFullPath(_repositoryPath),
            Path.GetFullPath(Path.Combine(_repositoryPath, ".git")),
            false);
        _executor = new GitCommandExecutor(new GitCliOptions());
        _service = new GitToolsService(
            _executor,
            new GitRepositoryFileVersionService(_executor),
            new TestRepositoryPathService(),
            _externalProcess);
    }

    [Fact]
    public async Task ExternalDiffSupportsEmptySideUnicodePathAndDoesNotMutateRepository()
    {
        ConfigureDiffTool("csharpgit-test", "true \"$LOCAL\" \"$REMOTE\"");
        const string relativePath = "folder name/данные 日本語.txt";
        var fullPath = Path.Combine(_repositoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "new file\n");

        var beforeStatus = ReadGit("status", "--porcelain=v1", "-z", "--untracked-files=all");
        var beforeConfig = File.ReadAllBytes(Path.Combine(_repositoryPath, ".git", "config"));
        var pair = new DiffFileVersionPair(
            new DiffFileVersion(
                DiffFileVersionLocation.Unavailable,
                relativePath,
                EntryKind: GitEntryKind.Missing,
                UnavailableReason: "The old side does not exist."),
            new DiffFileVersion(DiffFileVersionLocation.WorkingCopy, relativePath),
            relativePath,
            "A");

        await _service.RunExternalDiffAsync(_repository, pair);

        Assert.Equal(beforeStatus, ReadGit("status", "--porcelain=v1", "-z", "--untracked-files=all"));
        Assert.Equal(beforeConfig, File.ReadAllBytes(Path.Combine(_repositoryPath, ".git", "config")));
    }

    [Fact]
    public async Task SelectedAndRepositoryMergeToolWorkflowsLeaveResolutionToGitState()
    {
        CreateConflict();
        ConfigureMergeTool("csharpgit-test", "true \"$LOCAL\" \"$REMOTE\" \"$MERGED\"");
        var stateService = new GitCliRepositoryService(_executor);
        var beforeConfig = File.ReadAllBytes(Path.Combine(_repositoryPath, ".git", "config"));

        var before = await stateService.ReadAsync(_repository);
        var conflict = Assert.Single(before.CurrentOperation.Conflicts);
        Assert.False(conflict.IsResolved);

        await _service.RunMergeToolForFileAsync(_repository, conflict);
        var afterSelected = await stateService.ReadAsync(_repository);
        Assert.Contains(afterSelected.CurrentOperation.Conflicts, item => item.Path == conflict.Path && !item.IsResolved);

        await _service.RunMergeToolWorkflowAsync(_repository);
        var afterWorkflow = await stateService.ReadAsync(_repository);
        Assert.Contains(afterWorkflow.CurrentOperation.Conflicts, item => item.Path == conflict.Path && !item.IsResolved);
        Assert.Equal(beforeConfig, File.ReadAllBytes(Path.Combine(_repositoryPath, ".git", "config")));
    }

    [Fact]
    public async Task DiffAndMergeTestsUseTemporaryStateOnly()
    {
        Commit("tracked.txt", "base\n", "base");
        await File.AppendAllTextAsync(Path.Combine(_repositoryPath, "tracked.txt"), "working change\n");
        ConfigureDiffTool("csharpgit-test", "true \"$LOCAL\" \"$REMOTE\"");
        ConfigureMergeTool("csharpgit-test", "true \"$LOCAL\" \"$REMOTE\" \"$MERGED\"");

        var beforeStatus = ReadGit("status", "--porcelain=v1", "-z", "--untracked-files=all");
        var beforeConfig = File.ReadAllBytes(Path.Combine(_repositoryPath, ".git", "config"));

        await _service.TestAsync(_repository, GitToolKind.Diff);
        await _service.TestAsync(_repository, GitToolKind.Merge);

        Assert.Equal(beforeStatus, ReadGit("status", "--porcelain=v1", "-z", "--untracked-files=all"));
        Assert.Equal(beforeConfig, File.ReadAllBytes(Path.Combine(_repositoryPath, ".git", "config")));
    }

    [Fact]
    public async Task EditorTestUsesEffectiveGitEditorAndTemporaryFile()
    {
        RunGit("config", "core.editor", "code --wait");
        var beforeConfig = File.ReadAllBytes(Path.Combine(_repositoryPath, ".git", "config"));

        await _service.TestAsync(_repository, GitToolKind.Editor);

        var invocation = Assert.Single(_externalProcess.EditorInvocations);
        Assert.Equal("code --wait", invocation.Command);
        Assert.Equal(_repositoryPath, invocation.WorkingDirectory);
        Assert.True(invocation.FileExistedWhenInvoked);
        Assert.False(File.Exists(invocation.FilePath));
        Assert.Equal(beforeConfig, File.ReadAllBytes(Path.Combine(_repositoryPath, ".git", "config")));
    }

    [Fact]
    public async Task MergeLaunchFailureNamesToolOperationAndGitReason()
    {
        CreateConflict();
        ConfigureMergeTool(
            "csharpgit-failing",
            "csharpgit-command-that-does-not-exist \"$LOCAL\" \"$REMOTE\" \"$MERGED\"");
        var state = await new GitCliRepositoryService(_executor).ReadAsync(_repository);
        var conflict = Assert.Single(state.CurrentOperation.Conflicts);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RunMergeToolForFileAsync(_repository, conflict));

        Assert.Contains("Merge", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("csharpgit-failing", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Git exited with code", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private void ConfigureDiffTool(string name, string command)
    {
        RunGit("config", "diff.guitool", name);
        RunGit("config", $"difftool.{name}.cmd", command);
        RunGit("config", $"difftool.{name}.trustExitCode", "true");
    }

    private void ConfigureMergeTool(string name, string command)
    {
        RunGit("config", "merge.guitool", name);
        RunGit("config", $"mergetool.{name}.cmd", command);
        RunGit("config", $"mergetool.{name}.trustExitCode", "true");
        RunGit("config", "mergetool.keepBackup", "false");
    }

    private void CreateConflict()
    {
        Commit("conflict.txt", "base\n", "base");
        RunGit("checkout", "-b", "left");
        Commit("conflict.txt", "left\n", "left");
        RunGit("checkout", "-b", "right", "HEAD~1");
        Commit("conflict.txt", "right\n", "right");
        RunGit("checkout", "left");
        var result = RunGitCore(["merge", "right"]);
        Assert.NotEqual(0, result.ExitCode);
    }

    private void Commit(string relativePath, string contents, string message)
    {
        var path = Path.Combine(_repositoryPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        RunGit("add", "--", relativePath);
        RunGit("commit", "-m", message);
    }

    public void Dispose()
    {
        foreach (var pair in _originalEnvironment)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        try
        {
            if (!Directory.Exists(_root)) return;
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void SetEnvironment(string name, string? value)
    {
        if (!_originalEnvironment.ContainsKey(name))
            _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    private void RunGit(params string[] arguments)
    {
        var result = RunGitCore(arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Git failed with {result.ExitCode}: {result.Error}{result.Output}");
    }

    private string ReadGit(params string[] arguments)
    {
        var result = RunGitCore(arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Git failed with {result.ExitCode}: {result.Error}{result.Output}");
        return result.Output;
    }

    private (int ExitCode, string Output, string Error) RunGitCore(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Git did not start.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }

    private sealed class TestRepositoryPathService : IRepositoryPathService
    {
        public string ResolveExistingWorkingTreeFile(Repository repository, string gitPath, bool allowFinalLink = false) =>
            Path.Combine(repository.WorkingDirectory, gitPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed class RecordingExternalProcessService : IExternalToolProcessService
    {
        public List<EditorInvocation> EditorInvocations { get; } = [];

        public string? ResolveExecutable(string commandOrPath)
        {
            if (string.IsNullOrWhiteSpace(commandOrPath)) return null;
            var value = commandOrPath.Trim();
            if (value[0] is '\'' or '"')
            {
                var end = value.IndexOf(value[0], 1);
                return end > 1 ? value[1..end] : value[1..];
            }
            var whitespace = value.IndexOfAny([' ', '\t', '\r', '\n']);
            return whitespace < 0 ? value : value[..whitespace];
        }

        public bool IsExecutablePathUsable(string path) => File.Exists(path);

        public Task RunShellCommandAsync(
            string rawCommand,
            string fileArgument,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            EditorInvocations.Add(new EditorInvocation(
                rawCommand,
                fileArgument,
                workingDirectory,
                File.Exists(fileArgument)));
            return Task.CompletedTask;
        }
    }

    private sealed record EditorInvocation(
        string Command,
        string FilePath,
        string WorkingDirectory,
        bool FileExistedWhenInvoked);
}
