using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Git;

namespace CSharpGit.Git.Tests;

[Collection(GitToolsEnvironmentCollection.CollectionName)]
public sealed class GitEditorValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-editor-validation-{Guid.NewGuid():N}");
    private readonly string _home;
    private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);
    private readonly GitToolsService _service;

    public GitEditorValidationTests()
    {
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(_home);

        SetEnvironment("HOME", _home);
        SetEnvironment("USERPROFILE", _home);
        SetEnvironment("XDG_CONFIG_HOME", Path.Combine(_home, ".config"));
        SetEnvironment("GIT_CONFIG_NOSYSTEM", "1");
        SetEnvironment("GIT_EDITOR", null);
        SetEnvironment("VISUAL", null);
        SetEnvironment("EDITOR", null);
        File.WriteAllText(Path.Combine(_home, ".gitconfig"), string.Empty);

        _service = new GitToolsService(
            new GitCommandExecutor(new GitCliOptions()),
            new ThrowingFileVersionService(),
            new ThrowingRepositoryPathService(),
            new UnresolvedExternalProcessService());
    }

    [Fact]
    public async Task MissingExplicitEditorPathIsValidationError()
    {
        var missingPath = Path.Combine(_root, "missing editor", OperatingSystem.IsWindows() ? "editor.exe" : "editor");
        RunGitConfig("core.editor", $"\"{missingPath}\" --wait");

        var snapshot = await _service.ReadAsync(null, GitToolKind.Editor);

        Assert.Contains(snapshot.Validation, message =>
            message.Severity == GitToolValidationSeverity.Error
            && message.Message.Contains("path is not usable", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot.Validation, message => message.Severity == GitToolValidationSeverity.Warning);
    }

    [Fact]
    public async Task MissingBareEditorCommandIsWarningNotError()
    {
        RunGitConfig("core.editor", "csharpgit-editor-command-not-on-path --wait");

        var snapshot = await _service.ReadAsync(null, GitToolKind.Editor);

        Assert.DoesNotContain(snapshot.Validation, message => message.Severity == GitToolValidationSeverity.Error);
        Assert.Contains(snapshot.Validation, message =>
            message.Severity == GitToolValidationSeverity.Warning
            && message.Message.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase));
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

    private void RunGitConfig(string key, string value)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("config");
        startInfo.ArgumentList.Add("--global");
        startInfo.ArgumentList.Add(key);
        startInfo.ArgumentList.Add(value);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Git did not start.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git failed with {process.ExitCode}: {error}{output}");
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

    private sealed class ThrowingRepositoryPathService : IRepositoryPathService
    {
        public string ResolveExistingWorkingTreeFile(Repository repository, string gitPath, bool allowFinalLink = false) =>
            throw new NotSupportedException();
    }

    private sealed class UnresolvedExternalProcessService : IExternalToolProcessService
    {
        public string? ResolveExecutable(string commandOrPath) => null;
        public bool IsExecutablePathUsable(string path) => false;
        public Task RunShellCommandAsync(string rawCommand, string fileArgument, string workingDirectory, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
