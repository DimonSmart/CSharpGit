using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Git;

namespace CSharpGit.Git.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class GitToolsEnvironmentCollection
{
    public const string CollectionName = "Git tools environment";
}

[Collection(GitToolsEnvironmentCollection.CollectionName)]
public sealed class GitToolsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-git-tools-{Guid.NewGuid():N}");
    private readonly string _home;
    private readonly string _repositoryPath;
    private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);
    private readonly GitToolsService _service;
    private readonly Repository _repository;

    public GitToolsServiceTests()
    {
        _home = Path.Combine(_root, "home");
        _repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_repositoryPath);

        SetEnvironment("HOME", _home);
        SetEnvironment("USERPROFILE", _home);
        SetEnvironment("XDG_CONFIG_HOME", Path.Combine(_home, ".config"));
        SetEnvironment("GIT_CONFIG_NOSYSTEM", "1");
        SetEnvironment("GIT_CONFIG_SYSTEM", null);
        SetEnvironment("GIT_EDITOR", null);
        SetEnvironment("VISUAL", null);
        SetEnvironment("EDITOR", null);
        File.WriteAllText(Path.Combine(_home, ".gitconfig"), string.Empty);

        RunGit(_repositoryPath, "init", "-b", "main");
        _repository = new Repository(
            Path.GetFullPath(_repositoryPath),
            Path.GetFullPath(_repositoryPath),
            Path.GetFullPath(Path.Combine(_repositoryPath, ".git")),
            false);

        var executor = new GitCommandExecutor(new GitCliOptions());
        _service = new GitToolsService(
            executor,
            new ThrowingFileVersionService(),
            new TestRepositoryPathService(),
            new TestExternalProcessService());
    }

    [Fact]
    public async Task ReadsGlobalConfigurationAndRepositoryOverrideThenFallsBackAfterRemoval()
    {
        RunGit(_repositoryPath, "config", "--global", "diff.tool", "vscode");

        var global = await _service.ReadAsync(_repository, GitToolKind.Diff);

        Assert.Equal("vscode", global.EffectiveValue);
        Assert.Equal(GitToolConfigurationSource.Global, global.EffectiveSource);
        Assert.Equal("vscode", global.Global.SelectionValue);
        Assert.False(global.Repository.IsConfigured);

        RunGit(_repositoryPath, "config", "diff.guitool", "meld");
        var overridden = await _service.ReadAsync(_repository, GitToolKind.Diff);

        Assert.Equal("meld", overridden.EffectiveValue);
        Assert.Equal(GitToolConfigurationSource.Repository, overridden.EffectiveSource);
        Assert.Equal("meld", overridden.Repository.SelectionValue);

        await _service.RemoveOverrideAsync(_repository, GitToolKind.Diff, GitToolWriteScope.Repository);
        var fallback = await _service.ReadAsync(_repository, GitToolKind.Diff);

        Assert.Equal("vscode", fallback.EffectiveValue);
        Assert.Equal(GitToolConfigurationSource.Global, fallback.EffectiveSource);
    }

    [Fact]
    public async Task ReadsSystemScopeSeparatelyWhileGlobalRemainsEffective()
    {
        var systemConfig = Path.Combine(_root, "system.gitconfig");
        File.WriteAllText(systemConfig, "[diff]\n\tguitool = system-diff\n[core]\n\teditor = system-editor\n");
        SetEnvironment("GIT_CONFIG_NOSYSTEM", null);
        SetEnvironment("GIT_CONFIG_SYSTEM", systemConfig);
        RunGit(_repositoryPath, "config", "--global", "diff.guitool", "global-diff");
        RunGit(_repositoryPath, "config", "--global", "core.editor", "global-editor");

        var diff = await _service.ReadAsync(_repository, GitToolKind.Diff);
        var editor = await _service.ReadAsync(_repository, GitToolKind.Editor);

        Assert.Equal("global-diff", diff.EffectiveValue);
        Assert.Equal(GitToolConfigurationSource.Global, diff.EffectiveSource);
        Assert.Equal("system-diff", diff.System.SelectionValue);
        Assert.Contains("system.gitconfig", diff.System.Origin ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("global-editor", editor.EffectiveValue);
        Assert.Equal("system-editor", editor.System.SelectionValue);
        Assert.Contains("system.gitconfig", editor.System.Origin ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GuiDiffFallbackUsesMergeGuiBeforeGenericDiffAndMergeTools()
    {
        RunGit(_repositoryPath, "config", "--global", "merge.tool", "generic-merge");
        RunGit(_repositoryPath, "config", "--global", "diff.tool", "generic-diff");
        RunGit(_repositoryPath, "config", "--global", "merge.guitool", "gui-merge");

        var snapshot = await _service.ReadAsync(_repository, GitToolKind.Diff);

        Assert.Equal("gui-merge", snapshot.EffectiveValue);
        Assert.Equal(GitToolConfigurationSource.Global, snapshot.EffectiveSource);
    }

    [Fact]
    public async Task DiffToolSpecificPathFallsBackToMergeToolConfiguration()
    {
        RunGit(_repositoryPath, "config", "diff.guitool", "shared-tool");
        RunGit(_repositoryPath, "config", "mergetool.shared-tool.path", Path.Combine(_root, "Shared Tool"));

        var snapshot = await _service.ReadAsync(_repository, GitToolKind.Diff);

        Assert.Equal(Path.Combine(_root, "Shared Tool"), snapshot.EffectivePath);
        Assert.Null(snapshot.EffectiveTrustExitCode);
    }

    [Fact]
    public async Task DiffTrustExitCodeUsesStandardNonToolSpecificKey()
    {
        await _service.SaveAsync(
            _repository,
            new GitToolEdit(
                GitToolKind.Diff,
                GitToolWriteScope.Repository,
                "my-diff",
                TrustExitCode: true,
                UpdateTrustExitCode: true));

        var snapshot = await _service.ReadAsync(_repository, GitToolKind.Diff);

        Assert.Equal("true", ReadGit(_repositoryPath, "config", "--get", "difftool.trustExitCode"));
        Assert.Equal(string.Empty, ReadGitAllowFailure(_repositoryPath, "config", "--get", "difftool.my-diff.trustExitCode"));
        Assert.True(snapshot.EffectiveTrustExitCode);
        Assert.True(snapshot.Repository.TrustExitCode);
    }

    [Fact]
    public async Task IncludedGlobalConfigurationKeepsItsOrigin()
    {
        var includePath = Path.Combine(_home, "included-tools.config");
        File.WriteAllText(includePath, "[diff]\n\ttool = included-diff\n");
        RunGit(_repositoryPath, "config", "--global", "include.path", includePath);

        var snapshot = await _service.ReadAsync(_repository, GitToolKind.Diff);

        Assert.Equal("included-diff", snapshot.EffectiveValue);
        Assert.Equal(GitToolConfigurationSource.Global, snapshot.EffectiveSource);
        Assert.Contains("included-tools.config", snapshot.EffectiveOrigin ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorktreeConfigurationIsShownAsHigherPrioritySource()
    {
        RunGit(_repositoryPath, "config", "--global", "diff.guitool", "global-tool");
        RunGit(_repositoryPath, "config", "extensions.worktreeConfig", "true");
        RunGit(_repositoryPath, "config", "--worktree", "diff.guitool", "worktree-tool");

        var snapshot = await _service.ReadAsync(_repository, GitToolKind.Diff);

        Assert.Equal("worktree-tool", snapshot.EffectiveValue);
        Assert.Equal(GitToolConfigurationSource.Worktree, snapshot.EffectiveSource);
        Assert.Equal("worktree-tool", snapshot.Worktree.SelectionValue);
        Assert.Equal("global-tool", snapshot.Global.SelectionValue);
    }

    [Fact]
    public async Task ExistingGenericSelectionKeyIsPreservedWhenEdited()
    {
        RunGit(_repositoryPath, "config", "diff.tool", "before");

        await _service.SaveAsync(
            _repository,
            new GitToolEdit(GitToolKind.Diff, GitToolWriteScope.Repository, "after"));

        Assert.Equal("after", ReadGit(_repositoryPath, "config", "--get", "diff.tool"));
        Assert.Equal(string.Empty, ReadGitAllowFailure(_repositoryPath, "config", "--get", "diff.guitool"));
    }

    [Fact]
    public async Task NewGuiSelectionUsesGuiKeyAndDoesNotWritePromptConfiguration()
    {
        await _service.SaveAsync(
            _repository,
            new GitToolEdit(GitToolKind.Merge, GitToolWriteScope.Repository, "meld"));

        Assert.Equal("meld", ReadGit(_repositoryPath, "config", "--get", "merge.guitool"));
        Assert.Equal(string.Empty, ReadGitAllowFailure(_repositoryPath, "config", "--get", "merge.tool"));
        Assert.Equal(string.Empty, ReadGitAllowFailure(_repositoryPath, "config", "--get", "mergetool.prompt"));
    }

    [Fact]
    public async Task CustomCommandRoundTripsWithoutQuotingChanges()
    {
        const string command = "'tool with spaces' --left=\"$LOCAL\" --right=\"$REMOTE\"";

        await _service.SaveAsync(
            _repository,
            new GitToolEdit(
                GitToolKind.Diff,
                GitToolWriteScope.Repository,
                "my-diff",
                Command: command,
                UpdateCommand: true));
        var snapshot = await _service.ReadAsync(_repository, GitToolKind.Diff);

        Assert.Equal(command, snapshot.Repository.Command);
        Assert.Equal(command, ReadGit(_repositoryPath, "config", "--get", "difftool.my-diff.cmd"));
    }

    [Fact]
    public async Task EditorEnvironmentOverridesCoreEditorWhileConfiguredScopesRemainVisible()
    {
        RunGit(_repositoryPath, "config", "--global", "core.editor", "vim");
        RunGit(_repositoryPath, "config", "core.editor", "code --wait");
        SetEnvironment("GIT_EDITOR", "nvim");

        var snapshot = await _service.ReadAsync(_repository, GitToolKind.Editor);

        Assert.Equal("nvim", snapshot.EffectiveValue);
        Assert.Equal(GitToolConfigurationSource.Environment, snapshot.EffectiveSource);
        Assert.Equal("vim", snapshot.Global.SelectionValue);
        Assert.Equal("code --wait", snapshot.Repository.SelectionValue);
    }

    [Fact]
    public async Task InvalidExplicitPathIsErrorButUnresolvedPathCommandIsOnlyWarning()
    {
        var missingPath = Path.Combine(_root, "does-not-exist", "tool");
        RunGit(_repositoryPath, "config", "diff.guitool", "bad-path");
        RunGit(_repositoryPath, "config", "difftool.bad-path.path", missingPath);

        var invalid = await _service.ReadAsync(_repository, GitToolKind.Diff);

        Assert.Contains(invalid.Validation, message => message.Severity == GitToolValidationSeverity.Error);

        RunGit(_repositoryPath, "config", "--unset-all", "difftool.bad-path.path");
        RunGit(_repositoryPath, "config", "diff.guitool", "missing-tool");
        var unresolved = await _service.ReadAsync(_repository, GitToolKind.Diff);

        Assert.DoesNotContain(unresolved.Validation, message => message.Severity == GitToolValidationSeverity.Error);
        Assert.Contains(unresolved.Validation, message =>
            message.Severity == GitToolValidationSeverity.Warning
            && message.Message.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SavingUnchangedScopeDoesNotRewriteGitConfig()
    {
        RunGit(_repositoryPath, "config", "--global", "core.editor", "code --wait");
        var configPath = Path.Combine(_home, ".gitconfig");
        var sentinel = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(configPath, sentinel);

        await _service.SaveAsync(
            _repository,
            new GitToolEdit(GitToolKind.Editor, GitToolWriteScope.Global, "code --wait"));

        Assert.Equal(sentinel, File.GetLastWriteTimeUtc(configPath));
    }

    [Fact]
    public void SourceParserContainsSystemScopeAndGuiLaunchesNeverPersistPromptKeys()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitToolsService.cs"));

        Assert.Contains("\"system\" => GitToolConfigurationSource.System", source);
        Assert.Contains("\"difftool.trustExitCode\"", source);
        Assert.Contains("\"--gui\", \"--no-prompt\"", source);
        Assert.DoesNotContain("difftool.prompt", source);
        Assert.DoesNotContain("mergetool.prompt", source);
    }

    public void Dispose()
    {
        foreach (var pair in _originalEnvironment)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var result = RunGitCore(workingDirectory, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Git failed with {result.ExitCode}: {result.Error}{result.Output}");
    }

    private static string ReadGit(string workingDirectory, params string[] arguments)
    {
        var result = RunGitCore(workingDirectory, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Git failed with {result.ExitCode}: {result.Error}{result.Output}");
        return result.Output.Trim();
    }

    private static string ReadGitAllowFailure(string workingDirectory, params string[] arguments)
    {
        var result = RunGitCore(workingDirectory, arguments);
        return result.ExitCode == 0 ? result.Output.Trim() : string.Empty;
    }

    private static (int ExitCode, string Output, string Error) RunGitCore(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
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

    private sealed class ThrowingFileVersionService : IRepositoryFileVersionService
    {
        public Task<DiffFileVersionPair> ResolveCommitAsync(Repository repository, string commitHash, string selectedPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DiffFileVersionPair> ResolveWorkingTreeAsync(Repository repository, WorkingTreeChange change, WorkingTreeDiffKind kind, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MaterializedFileVersion> MaterializeAsync(Repository repository, DiffFileVersion version, DiffFileSide side, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestRepositoryPathService : IRepositoryPathService
    {
        public string ResolveExistingWorkingTreeFile(Repository repository, string gitPath, bool allowFinalLink = false) =>
            Path.Combine(repository.WorkingDirectory, gitPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed class TestExternalProcessService : IExternalToolProcessService
    {
        public string? ResolveExecutable(string commandOrPath) =>
            string.Equals(commandOrPath, "missing-tool", StringComparison.Ordinal) ? null : commandOrPath;

        public bool IsExecutablePathUsable(string path) => File.Exists(path);

        public Task RunShellCommandAsync(string rawCommand, string fileArgument, string workingDirectory, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
