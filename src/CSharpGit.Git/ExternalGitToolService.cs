using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class ExternalGitToolService : IExternalGitToolService
{
    private readonly GitCommandExecutor _executor;
    private readonly IGitToolConfigurationService _configurationService;
    private readonly IRepositoryFileVersionService _fileVersionService;
    private readonly IRepositoryPathService _pathService;
    private readonly IExternalToolProcessService _externalProcess;

    internal ExternalGitToolService(
        GitCommandExecutor executor,
        IGitToolConfigurationService configurationService,
        IRepositoryFileVersionService fileVersionService,
        IRepositoryPathService pathService,
        IExternalToolProcessService externalProcess)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _fileVersionService = fileVersionService ?? throw new ArgumentNullException(nameof(fileVersionService));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _externalProcess = externalProcess ?? throw new ArgumentNullException(nameof(externalProcess));
    }

    public async Task OpenEditorAsync(
        Repository? repository,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A file path is required.", nameof(filePath));
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The file is no longer available.", fullPath);

        var configuration = await _configurationService.ReadAsync(repository, GitToolKind.Editor, cancellationToken);
        EnsureRunnable(configuration, "Git editor");
        await _externalProcess.RunShellCommandAsync(
            configuration.EffectiveValue!,
            fullPath,
            repository?.WorkingDirectory ?? Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory,
            cancellationToken);
    }

    public async Task RunExternalDiffAsync(
        Repository repository,
        DiffFileVersionPair pair,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(pair);
        var configuration = await _configurationService.ReadAsync(repository, GitToolKind.Diff, cancellationToken);
        EnsureRunnable(configuration, "Diff tool");

        var temporaryRoot = CreateTemporaryDirectory("diff");
        try
        {
            var original = await PrepareDiffSideAsync(repository, pair.Original, DiffFileSide.Original, temporaryRoot, cancellationToken);
            var changed = await PrepareDiffSideAsync(repository, pair.Changed, DiffFileSide.Changed, temporaryRoot, cancellationToken);
            var result = await RunGitResultAsync(
                repository.WorkingDirectory,
                "ExternalDiff",
                GitCommandKind.User,
                cancellationToken,
                ["difftool", "--gui", "--no-prompt", "--no-index", "--", original, changed]);
            if (result.ExitCode is not 0 and not 1)
                throw ToolFailure(configuration.EffectiveValue, "Diff", result);
        }
        finally
        {
            TryDeleteDirectory(temporaryRoot);
        }
    }

    public async Task RunMergeToolForFileAsync(
        Repository repository,
        ConflictFile conflict,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(conflict);
        if (!conflict.CanRunMergeTool) throw new InvalidOperationException("Merge tool is unavailable for the selected conflict.");
        if (string.IsNullOrWhiteSpace(conflict.Path) || Path.IsPathRooted(conflict.Path))
            throw new InvalidOperationException("The selected conflict path is invalid.");

        var configuration = await _configurationService.ReadAsync(repository, GitToolKind.Merge, cancellationToken);
        EnsureRunnable(configuration, "Merge tool");
        var result = await RunGitResultAsync(
            repository.WorkingDirectory,
            "MergeToolFile",
            GitCommandKind.User,
            cancellationToken,
            ["mergetool", "--gui", "--no-prompt", "--", conflict.Path]);
        if (result.ExitCode != 0) throw ToolFailure(configuration.EffectiveValue, "Merge", result);
    }

    public async Task RunMergeToolWorkflowAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var configuration = await _configurationService.ReadAsync(repository, GitToolKind.Merge, cancellationToken);
        EnsureRunnable(configuration, "Merge tool");
        var result = await RunGitResultAsync(
            repository.WorkingDirectory,
            "MergeToolWorkflow",
            GitCommandKind.User,
            cancellationToken,
            ["mergetool", "--gui", "--no-prompt"]);
        if (result.ExitCode != 0) throw ToolFailure(configuration.EffectiveValue, "Merge", result);
    }

    public async Task OpenConflictInEditorAsync(
        Repository repository,
        ConflictFile conflict,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(conflict);

        if (!conflict.CanOpenManually)
            throw new InvalidOperationException(
                "This conflict cannot be opened as a working-copy file.");

        var path = _pathService.ResolveExistingWorkingTreeFile(
            repository,
            conflict.Path);
        await OpenEditorAsync(
            repository,
            path,
            cancellationToken);
    }

    public Task TestAsync(
        Repository? repository,
        GitToolKind kind,
        CancellationToken cancellationToken = default) =>
        kind switch
        {
            GitToolKind.Editor => TestEditorAsync(repository, cancellationToken),
            GitToolKind.Diff => TestDiffAsync(repository, cancellationToken),
            GitToolKind.Merge => TestMergeAsync(repository, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private async Task<string> PrepareDiffSideAsync(
        Repository repository,
        DiffFileVersion version,
        DiffFileSide side,
        string temporaryRoot,
        CancellationToken cancellationToken)
    {
        if (version.Location == DiffFileVersionLocation.WorkingCopy)
            return _pathService.ResolveExistingWorkingTreeFile(repository, version.GitPath);

        if (version.Location == DiffFileVersionLocation.GitSnapshot && version.EntryKind == GitEntryKind.RegularFile)
            return (await _fileVersionService.MaterializeAsync(repository, version, side, cancellationToken)).Path;

        if (version.Location == DiffFileVersionLocation.Unavailable && version.EntryKind == GitEntryKind.Missing)
        {
            var extension = SafeExtension(version.GitPath);
            var path = Path.Combine(temporaryRoot, side == DiffFileSide.Original ? $"empty-old{extension}" : $"empty-new{extension}");
            await File.WriteAllBytesAsync(path, [], cancellationToken);
            return path;
        }

        throw new NotSupportedException(version.UnavailableReason ?? "This Git entry cannot be compared by an external diff tool.");
    }

    private async Task TestEditorAsync(Repository? repository, CancellationToken cancellationToken)
    {
        var directory = CreateTemporaryDirectory("editor-test");
        try
        {
            var path = Path.Combine(directory, "csharpgit-editor-test.txt");
            await File.WriteAllTextAsync(path, "CSharpGit Git Editor test.\nYou can close this file without saving.\n", cancellationToken);
            await OpenEditorAsync(repository, path, cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private async Task TestDiffAsync(Repository? repository, CancellationToken cancellationToken)
    {
        var configuration = await _configurationService.ReadAsync(repository, GitToolKind.Diff, cancellationToken);
        EnsureRunnable(configuration, "Diff tool");
        var directory = CreateTemporaryDirectory("diff-test");
        try
        {
            var original = Path.Combine(directory, "original.txt");
            var changed = Path.Combine(directory, "changed.txt");
            await File.WriteAllTextAsync(original, "CSharpGit diff tool test\nold line\n", cancellationToken);
            await File.WriteAllTextAsync(changed, "CSharpGit diff tool test\nnew line\n", cancellationToken);
            var result = await RunGitResultAsync(
                repository?.WorkingDirectory ?? directory,
                "DiffToolTest",
                GitCommandKind.User,
                cancellationToken,
                ["difftool", "--gui", "--no-prompt", "--no-index", "--", original, changed]);
            if (result.ExitCode is not 0 and not 1)
                throw ToolFailure(configuration.EffectiveValue, "Diff test", result);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private async Task TestMergeAsync(Repository? repository, CancellationToken cancellationToken)
    {
        var configuration = await _configurationService.ReadAsync(repository, GitToolKind.Merge, cancellationToken);
        EnsureRunnable(configuration, "Merge tool");
        var directory = CreateTemporaryDirectory("merge-test");
        try
        {
            await RequireGitSuccessAsync(directory, "MergeTestInit", cancellationToken, ["init"]);
            await RequireGitSuccessAsync(directory, "MergeTestConfig", cancellationToken, ["config", "user.name", "CSharpGit Test"]);
            await RequireGitSuccessAsync(directory, "MergeTestConfig", cancellationToken, ["config", "user.email", "csharpgit-test@invalid.local"]);

            var path = Path.Combine(directory, "merge-test.txt");
            await File.WriteAllTextAsync(path, "base\n", cancellationToken);
            await RequireGitSuccessAsync(directory, "MergeTestAdd", cancellationToken, ["add", "merge-test.txt"]);
            await RequireGitSuccessAsync(directory, "MergeTestCommit", cancellationToken, ["commit", "-m", "base"]);
            await RequireGitSuccessAsync(directory, "MergeTestBranch", cancellationToken, ["checkout", "-b", "csharpgit-left"]);
            await File.WriteAllTextAsync(path, "left\n", cancellationToken);
            await RequireGitSuccessAsync(directory, "MergeTestCommit", cancellationToken, ["commit", "-am", "left"]);
            await RequireGitSuccessAsync(directory, "MergeTestBranch", cancellationToken, ["checkout", "-b", "csharpgit-right", "HEAD~1"]);
            await File.WriteAllTextAsync(path, "right\n", cancellationToken);
            await RequireGitSuccessAsync(directory, "MergeTestCommit", cancellationToken, ["commit", "-am", "right"]);
            await RequireGitSuccessAsync(directory, "MergeTestBranch", cancellationToken, ["checkout", "csharpgit-left"]);

            var merge = await RunGitResultAsync(directory, "MergeTestConflict", GitCommandKind.Internal, cancellationToken, ["merge", "csharpgit-right"]);
            if (merge.ExitCode == 0) throw new InvalidOperationException("Could not create the isolated merge-tool test conflict.");

            var arguments = BuildTemporaryMergeToolArguments(configuration);
            arguments.AddRange(["mergetool", "--gui", "--no-prompt", "--", "merge-test.txt"]);
            var result = await RunGitResultAsync(directory, "MergeToolTest", GitCommandKind.User, cancellationToken, arguments);
            if (result.ExitCode != 0) throw ToolFailure(configuration.EffectiveValue, "Merge test", result);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static List<string> BuildTemporaryMergeToolArguments(GitToolConfigurationSnapshot configuration)
    {
        var arguments = new List<string>();
        AddTemporaryConfig(arguments, "merge.guitool", configuration.EffectiveValue);
        if (!string.IsNullOrWhiteSpace(configuration.EffectiveValue))
        {
            var prefix = $"mergetool.{configuration.EffectiveValue}";
            AddTemporaryConfig(arguments, $"{prefix}.path", configuration.EffectivePath);
            AddTemporaryConfig(arguments, $"{prefix}.cmd", configuration.EffectiveCommand);
            AddTemporaryConfig(arguments, $"{prefix}.trustExitCode", BoolText(configuration.EffectiveTrustExitCode));
        }
        AddTemporaryConfig(arguments, "mergetool.keepBackup", BoolText(configuration.EffectiveKeepBackup));
        return arguments;
    }

    private static void AddTemporaryConfig(List<string> arguments, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        arguments.Add("-c");
        arguments.Add($"{key}={value}");
    }

    private async Task RequireGitSuccessAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments)
    {
        var result = await RunGitResultAsync(workingDirectory, operation, GitCommandKind.Internal, cancellationToken, arguments);
        if (result.ExitCode != 0) throw GitFailure("Could not prepare the isolated Git tools test", result);
    }

    private Task<GitCommandResult> RunGitResultAsync(
        string workingDirectory,
        string operation,
        GitCommandKind kind,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments) =>
        _executor.ExecuteForResultAsync(workingDirectory, operation, kind, cancellationToken, null, arguments);

    private static string? BoolText(bool? value) => value switch
    {
        true => "true",
        false => "false",
        null => null
    };

    private static void EnsureRunnable(GitToolConfigurationSnapshot configuration, string displayName)
    {
        if (string.IsNullOrWhiteSpace(configuration.EffectiveValue))
            throw new InvalidOperationException($"{displayName} is not configured.");
        var error = configuration.Validation.FirstOrDefault(message => message.Severity == GitToolValidationSeverity.Error);
        if (error is not null) throw new InvalidOperationException(error.Message);
    }
    private static string SafeExtension(string gitPath)
    {
        try
        {
            var extension = Path.GetExtension(gitPath);
            return extension.Length <= 24 ? extension : string.Empty;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static string CreateTemporaryDirectory(string purpose)
    {
        var directory = Path.Combine(Path.GetTempPath(), "CSharpGit", "git-tools", purpose, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static InvalidOperationException ToolFailure(string? tool, string operation, GitCommandResult result)
    {
        var name = string.IsNullOrWhiteSpace(tool) ? "configured tool" : tool;
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? $"Git exited with code {result.ExitCode}."
            : $"Git exited with code {result.ExitCode}. {result.StandardError.Trim()}";
        return new InvalidOperationException($"{operation} tool \"{name}\" failed to start. {detail}");
    }

    private static InvalidOperationException GitFailure(string prefix, GitCommandResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? $"Git exited with code {result.ExitCode}."
            : $"Git exited with code {result.ExitCode}. {result.StandardError.Trim()}";
        return new InvalidOperationException($"{prefix}. {detail}");
    }
}
