using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed class GitToolsService : IGitToolsService
{
    private readonly GitCommandExecutor _executor;
    private readonly IRepositoryFileVersionService _fileVersionService;
    private readonly IRepositoryPathService _pathService;
    private readonly IExternalToolProcessService _externalProcess;

    internal GitToolsService(
        GitCommandExecutor executor,
        IRepositoryFileVersionService fileVersionService,
        IRepositoryPathService pathService,
        IExternalToolProcessService externalProcess)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _fileVersionService = fileVersionService ?? throw new ArgumentNullException(nameof(fileVersionService));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _externalProcess = externalProcess ?? throw new ArgumentNullException(nameof(externalProcess));
    }

    public Task<GitToolConfigurationSnapshot> ReadAsync(
        Repository? repository,
        GitToolKind kind,
        CancellationToken cancellationToken = default) =>
        kind == GitToolKind.Editor
            ? ReadEditorAsync(repository, cancellationToken)
            : ReadDiffOrMergeAsync(repository, kind, cancellationToken);

    public async Task SaveAsync(
        Repository? repository,
        GitToolEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        EnsureWritableScope(repository, edit.Scope);
        var value = edit.Value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(edit.Kind == GitToolKind.Editor
                ? "Enter a Git editor command."
                : "Enter a Git tool name.", nameof(edit));

        if (edit.Kind == GitToolKind.Editor)
        {
            await WriteIfChangedAsync(repository, edit.Scope, "core.editor", value, cancellationToken);
            return;
        }

        ValidateToolName(value);
        var selectionKey = await SelectWritableSelectionKeyAsync(repository, edit.Kind, edit.Scope, cancellationToken);
        await WriteIfChangedAsync(repository, edit.Scope, selectionKey, value, cancellationToken);

        var prefix = edit.Kind == GitToolKind.Diff ? "difftool" : "mergetool";
        if (edit.UpdatePath)
            await WriteOrUnsetIfChangedAsync(repository, edit.Scope, $"{prefix}.{value}.path", NormalizeOptional(edit.Path), cancellationToken);
        if (edit.UpdateCommand)
        {
            var command = NormalizeOptional(edit.Command);
            ValidateCustomCommand(edit.Kind, command);
            await WriteOrUnsetIfChangedAsync(repository, edit.Scope, $"{prefix}.{value}.cmd", command, cancellationToken);
        }
        if (edit.UpdateTrustExitCode)
        {
            var trustKey = edit.Kind == GitToolKind.Diff
                ? "difftool.trustExitCode"
                : $"mergetool.{value}.trustExitCode";
            await WriteOrUnsetIfChangedAsync(repository, edit.Scope, trustKey, BoolText(edit.TrustExitCode), cancellationToken);
        }
        if (edit.Kind == GitToolKind.Merge && edit.UpdateKeepBackup)
            await WriteOrUnsetIfChangedAsync(repository, edit.Scope, "mergetool.keepBackup", BoolText(edit.KeepBackup), cancellationToken);
    }

    public async Task RemoveOverrideAsync(
        Repository? repository,
        GitToolKind kind,
        GitToolWriteScope scope,
        CancellationToken cancellationToken = default)
    {
        EnsureWritableScope(repository, scope);
        if (kind == GitToolKind.Editor)
        {
            await UnsetIfPresentAsync(repository, scope, "core.editor", cancellationToken);
            return;
        }

        var keys = kind == GitToolKind.Diff
            ? new[] { "diff.guitool", "diff.tool" }
            : new[] { "merge.guitool", "merge.tool" };
        foreach (var key in keys)
        {
            if (await ReadScopedConfigValueAsync(repository, key, scope, cancellationToken) is null) continue;
            await UnsetIfPresentAsync(repository, scope, key, cancellationToken);
            return;
        }
    }

    public async Task OpenEditorAsync(
        Repository? repository,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A file path is required.", nameof(filePath));
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The file is no longer available.", fullPath);

        var configuration = await ReadEditorAsync(repository, cancellationToken);
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
        var configuration = await ReadDiffOrMergeAsync(repository, GitToolKind.Diff, cancellationToken);
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

        var configuration = await ReadDiffOrMergeAsync(repository, GitToolKind.Merge, cancellationToken);
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
        var configuration = await ReadDiffOrMergeAsync(repository, GitToolKind.Merge, cancellationToken);
        EnsureRunnable(configuration, "Merge tool");
        var result = await RunGitResultAsync(
            repository.WorkingDirectory,
            "MergeToolWorkflow",
            GitCommandKind.User,
            cancellationToken,
            ["mergetool", "--gui", "--no-prompt"]);
        if (result.ExitCode != 0) throw ToolFailure(configuration.EffectiveValue, "Merge", result);
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

    private async Task<GitToolConfigurationSnapshot> ReadEditorAsync(
        Repository? repository,
        CancellationToken cancellationToken)
    {
        var global = await ReadEditorScopeAsync(repository, GitToolConfigurationSource.Global, GitToolWriteScope.Global, cancellationToken);
        var local = repository is null
            ? EmptyScope(GitToolConfigurationSource.Repository)
            : await ReadEditorScopeAsync(repository, GitToolConfigurationSource.Repository, GitToolWriteScope.Repository, cancellationToken);
        var worktree = repository is null
            ? EmptyScope(GitToolConfigurationSource.Worktree)
            : await ReadEditorWorktreeAsync(repository, cancellationToken);
        var system = await ReadEditorSystemAsync(repository, cancellationToken);

        ConfigValue effective;
        var gitEditor = NormalizeOptional(Environment.GetEnvironmentVariable("GIT_EDITOR"));
        if (gitEditor is not null)
        {
            effective = new ConfigValue("GIT_EDITOR", gitEditor, GitToolConfigurationSource.Environment, "Environment: GIT_EDITOR");
        }
        else if (await ReadEffectiveConfigValueAsync(repository, "core.editor", cancellationToken) is { } configured)
        {
            effective = configured;
        }
        else if (NormalizeOptional(Environment.GetEnvironmentVariable("VISUAL")) is { } visual)
        {
            effective = new ConfigValue("VISUAL", visual, GitToolConfigurationSource.Environment, "Environment: VISUAL");
        }
        else if (NormalizeOptional(Environment.GetEnvironmentVariable("EDITOR")) is { } editor)
        {
            effective = new ConfigValue("EDITOR", editor, GitToolConfigurationSource.Environment, "Environment: EDITOR");
        }
        else
        {
            var result = await _executor.ExecuteForResultPreservingGitEditorAsync(
                WorkingDirectory(repository),
                "GitEditorDefault",
                cancellationToken,
                ["var", "GIT_EDITOR"]);
            var command = result.ExitCode == 0 ? NormalizeOptional(result.StandardOutput) : null;
            effective = new ConfigValue("git-default", command ?? "vi", GitToolConfigurationSource.GitDefault, "Git default");
        }

        var presets = EditorPresets();
        var resolved = _externalProcess.ResolveExecutable(effective.Value);
        var explicitEditorPath = ExtractExplicitCommandPath(effective.Value);
        var validation = ValidateConfiguration(GitToolKind.Editor, effective.Value, explicitEditorPath, null, resolved, presets, []);
        return new GitToolConfigurationSnapshot(
            GitToolKind.Editor,
            effective.Value,
            effective.Source,
            effective.Origin,
            null,
            effective.Value,
            null,
            null,
            resolved,
            global,
            local,
            worktree,
            system,
            presets,
            [],
            validation);
    }

    private async Task<GitToolConfigurationSnapshot> ReadDiffOrMergeAsync(
        Repository? repository,
        GitToolKind kind,
        CancellationToken cancellationToken)
    {
        if (kind is not (GitToolKind.Diff or GitToolKind.Merge)) throw new ArgumentOutOfRangeException(nameof(kind));

        var selection = await ReadEffectiveSelectionAsync(repository, kind, cancellationToken);
        var supported = await ReadSupportedToolsAsync(repository, kind, cancellationToken);
        var presets = BuildToolPresets(kind, supported);

        ConfigValue? path = null;
        ConfigValue? command = null;
        ConfigValue? trust = null;
        ConfigValue? keepBackup = null;
        if (selection is not null)
        {
            path = await ReadEffectiveToolFieldAsync(repository, kind, selection.Value, "path", cancellationToken);
            command = await ReadEffectiveToolFieldAsync(repository, kind, selection.Value, "cmd", cancellationToken);
            if (kind == GitToolKind.Merge)
                trust = await ReadEffectiveToolFieldAsync(repository, kind, selection.Value, "trustExitCode", cancellationToken);
        }
        if (kind == GitToolKind.Diff)
            trust = await ReadEffectiveConfigValueAsync(repository, "difftool.trustExitCode", cancellationToken);
        else
            keepBackup = await ReadEffectiveConfigValueAsync(repository, "mergetool.keepBackup", cancellationToken);

        var global = await ReadToolScopeAsync(repository, kind, GitToolConfigurationSource.Global, GitToolWriteScope.Global, cancellationToken);
        var local = repository is null
            ? EmptyScope(GitToolConfigurationSource.Repository)
            : await ReadToolScopeAsync(repository, kind, GitToolConfigurationSource.Repository, GitToolWriteScope.Repository, cancellationToken);
        var worktree = repository is null
            ? EmptyScope(GitToolConfigurationSource.Worktree)
            : await ReadToolWorktreeAsync(repository, kind, cancellationToken);
        var system = await ReadToolSystemAsync(repository, kind, cancellationToken);

        var resolved = ResolveToolExecutable(selection?.Value, path?.Value, command?.Value, presets);
        var validation = ValidateConfiguration(kind, selection?.Value, path?.Value, command?.Value, resolved, presets, supported);
        return new GitToolConfigurationSnapshot(
            kind,
            selection?.Value,
            selection?.Source ?? GitToolConfigurationSource.NotConfigured,
            selection?.Origin,
            path?.Value,
            command?.Value,
            ParseGitBoolean(trust?.Value),
            ParseGitBoolean(keepBackup?.Value),
            resolved,
            global,
            local,
            worktree,
            system,
            presets,
            supported,
            validation);
    }

    private async Task<GitToolScopeConfiguration> ReadEditorScopeAsync(
        Repository? repository,
        GitToolConfigurationSource source,
        GitToolWriteScope scope,
        CancellationToken cancellationToken)
    {
        var value = await ReadScopedConfigValueAsync(repository, "core.editor", scope, cancellationToken);
        return value is null
            ? EmptyScope(source)
            : new GitToolScopeConfiguration(source, "core.editor", value.Value, value.Origin, Command: value.Value);
    }

    private async Task<GitToolScopeConfiguration> ReadEditorWorktreeAsync(Repository repository, CancellationToken cancellationToken)
    {
        var value = await ReadConfigAtArgumentScopeAsync(repository, "core.editor", "--worktree", GitToolConfigurationSource.Worktree, cancellationToken);
        return value is null
            ? EmptyScope(GitToolConfigurationSource.Worktree)
            : new GitToolScopeConfiguration(GitToolConfigurationSource.Worktree, "core.editor", value.Value, value.Origin, Command: value.Value);
    }

    private async Task<GitToolScopeConfiguration> ReadEditorSystemAsync(Repository? repository, CancellationToken cancellationToken)
    {
        var value = await ReadConfigAtArgumentScopeAsync(repository, "core.editor", "--system", GitToolConfigurationSource.System, cancellationToken);
        return value is null
            ? EmptyScope(GitToolConfigurationSource.System)
            : new GitToolScopeConfiguration(GitToolConfigurationSource.System, "core.editor", value.Value, value.Origin, Command: value.Value);
    }

    private async Task<GitToolScopeConfiguration> ReadToolScopeAsync(
        Repository? repository,
        GitToolKind kind,
        GitToolConfigurationSource source,
        GitToolWriteScope scope,
        CancellationToken cancellationToken)
    {
        var selection = await ReadScopeSelectionAsync(repository, kind, scope, cancellationToken);
        var trust = kind == GitToolKind.Diff
            ? await ReadScopedConfigValueAsync(repository, "difftool.trustExitCode", scope, cancellationToken)
            : selection is null
                ? null
                : await ReadScopedToolFieldAsync(repository, kind, selection.Value, "trustExitCode", scope, cancellationToken);
        var keep = kind == GitToolKind.Merge
            ? await ReadScopedConfigValueAsync(repository, "mergetool.keepBackup", scope, cancellationToken)
            : null;
        if (selection is null)
            return new GitToolScopeConfiguration(
                source,
                null,
                null,
                null,
                TrustExitCode: ParseGitBoolean(trust?.Value),
                KeepBackup: ParseGitBoolean(keep?.Value));

        var path = await ReadScopedToolFieldAsync(repository, kind, selection.Value, "path", scope, cancellationToken);
        var command = await ReadScopedToolFieldAsync(repository, kind, selection.Value, "cmd", scope, cancellationToken);
        return new GitToolScopeConfiguration(
            source,
            selection.Key,
            selection.Value,
            selection.Origin,
            path?.Value,
            command?.Value,
            ParseGitBoolean(trust?.Value),
            ParseGitBoolean(keep?.Value));
    }

    private async Task<GitToolScopeConfiguration> ReadToolWorktreeAsync(
        Repository repository,
        GitToolKind kind,
        CancellationToken cancellationToken)
    {
        var selection = await ReadSelectionAtArgumentScopeAsync(repository, kind, "--worktree", GitToolConfigurationSource.Worktree, cancellationToken);
        var trust = kind == GitToolKind.Diff
            ? await ReadConfigAtArgumentScopeAsync(repository, "difftool.trustExitCode", "--worktree", GitToolConfigurationSource.Worktree, cancellationToken)
            : selection is null
                ? null
                : await ReadToolFieldAtArgumentScopeAsync(repository, kind, selection.Value, "trustExitCode", "--worktree", GitToolConfigurationSource.Worktree, cancellationToken);
        var keep = kind == GitToolKind.Merge
            ? await ReadConfigAtArgumentScopeAsync(repository, "mergetool.keepBackup", "--worktree", GitToolConfigurationSource.Worktree, cancellationToken)
            : null;
        if (selection is null)
            return new GitToolScopeConfiguration(
                GitToolConfigurationSource.Worktree,
                null,
                null,
                null,
                TrustExitCode: ParseGitBoolean(trust?.Value),
                KeepBackup: ParseGitBoolean(keep?.Value));

        var path = await ReadToolFieldAtArgumentScopeAsync(repository, kind, selection.Value, "path", "--worktree", GitToolConfigurationSource.Worktree, cancellationToken);
        var command = await ReadToolFieldAtArgumentScopeAsync(repository, kind, selection.Value, "cmd", "--worktree", GitToolConfigurationSource.Worktree, cancellationToken);
        return new GitToolScopeConfiguration(
            GitToolConfigurationSource.Worktree,
            selection.Key,
            selection.Value,
            selection.Origin,
            path?.Value,
            command?.Value,
            ParseGitBoolean(trust?.Value),
            ParseGitBoolean(keep?.Value));
    }

    private async Task<GitToolScopeConfiguration> ReadToolSystemAsync(
        Repository? repository,
        GitToolKind kind,
        CancellationToken cancellationToken)
    {
        var selection = await ReadSelectionAtArgumentScopeAsync(repository, kind, "--system", GitToolConfigurationSource.System, cancellationToken);
        var trust = kind == GitToolKind.Diff
            ? await ReadConfigAtArgumentScopeAsync(repository, "difftool.trustExitCode", "--system", GitToolConfigurationSource.System, cancellationToken)
            : selection is null
                ? null
                : await ReadToolFieldAtArgumentScopeAsync(repository, kind, selection.Value, "trustExitCode", "--system", GitToolConfigurationSource.System, cancellationToken);
        var keep = kind == GitToolKind.Merge
            ? await ReadConfigAtArgumentScopeAsync(repository, "mergetool.keepBackup", "--system", GitToolConfigurationSource.System, cancellationToken)
            : null;
        if (selection is null)
            return new GitToolScopeConfiguration(
                GitToolConfigurationSource.System,
                null,
                null,
                null,
                TrustExitCode: ParseGitBoolean(trust?.Value),
                KeepBackup: ParseGitBoolean(keep?.Value));

        var path = await ReadToolFieldAtArgumentScopeAsync(repository, kind, selection.Value, "path", "--system", GitToolConfigurationSource.System, cancellationToken);
        var command = await ReadToolFieldAtArgumentScopeAsync(repository, kind, selection.Value, "cmd", "--system", GitToolConfigurationSource.System, cancellationToken);
        return new GitToolScopeConfiguration(
            GitToolConfigurationSource.System,
            selection.Key,
            selection.Value,
            selection.Origin,
            path?.Value,
            command?.Value,
            ParseGitBoolean(trust?.Value),
            ParseGitBoolean(keep?.Value));
    }

    private async Task<ConfigValue?> ReadEffectiveSelectionAsync(Repository? repository, GitToolKind kind, CancellationToken cancellationToken)
    {
        foreach (var key in SelectionKeys(kind, includeCrossToolFallback: true))
            if (await ReadEffectiveConfigValueAsync(repository, key, cancellationToken) is { } value)
                return value;
        return null;
    }

    private async Task<ConfigValue?> ReadScopeSelectionAsync(
        Repository? repository,
        GitToolKind kind,
        GitToolWriteScope scope,
        CancellationToken cancellationToken)
    {
        foreach (var key in SelectionKeys(kind, includeCrossToolFallback: true))
            if (await ReadScopedConfigValueAsync(repository, key, scope, cancellationToken) is { } value)
                return value;
        return null;
    }

    private async Task<ConfigValue?> ReadSelectionAtArgumentScopeAsync(
        Repository? repository,
        GitToolKind kind,
        string scopeArgument,
        GitToolConfigurationSource source,
        CancellationToken cancellationToken)
    {
        foreach (var key in SelectionKeys(kind, includeCrossToolFallback: true))
            if (await ReadConfigAtArgumentScopeAsync(repository, key, scopeArgument, source, cancellationToken) is { } value)
                return value;
        return null;
    }

    private async Task<string> SelectWritableSelectionKeyAsync(
        Repository? repository,
        GitToolKind kind,
        GitToolWriteScope scope,
        CancellationToken cancellationToken)
    {
        foreach (var key in SelectionKeys(kind, includeCrossToolFallback: false))
            if (await ReadScopedConfigValueAsync(repository, key, scope, cancellationToken) is not null)
                return key;
        return kind == GitToolKind.Diff ? "diff.guitool" : "merge.guitool";
    }

    private async Task<ConfigValue?> ReadEffectiveToolFieldAsync(
        Repository? repository,
        GitToolKind kind,
        string tool,
        string field,
        CancellationToken cancellationToken)
    {
        foreach (var key in ToolFieldKeys(kind, tool, field))
            if (await ReadEffectiveConfigValueAsync(repository, key, cancellationToken) is { } value)
                return value;
        return null;
    }

    private async Task<ConfigValue?> ReadScopedToolFieldAsync(
        Repository? repository,
        GitToolKind kind,
        string tool,
        string field,
        GitToolWriteScope scope,
        CancellationToken cancellationToken)
    {
        foreach (var key in ToolFieldKeys(kind, tool, field))
            if (await ReadScopedConfigValueAsync(repository, key, scope, cancellationToken) is { } value)
                return value;
        return null;
    }

    private async Task<ConfigValue?> ReadToolFieldAtArgumentScopeAsync(
        Repository? repository,
        GitToolKind kind,
        string tool,
        string field,
        string scopeArgument,
        GitToolConfigurationSource source,
        CancellationToken cancellationToken)
    {
        foreach (var key in ToolFieldKeys(kind, tool, field))
            if (await ReadConfigAtArgumentScopeAsync(repository, key, scopeArgument, source, cancellationToken) is { } value)
                return value;
        return null;
    }

    private async Task<ConfigValue?> ReadEffectiveConfigValueAsync(
        Repository? repository,
        string key,
        CancellationToken cancellationToken)
    {
        var result = await RunGitResultAsync(
            WorkingDirectory(repository),
            "GitToolsRead",
            GitCommandKind.Internal,
            cancellationToken,
            ["config", "--includes", "--show-scope", "--show-origin", "--get", key]);
        if (result.ExitCode == 1) return null;
        if (result.ExitCode != 0) throw GitFailure("Could not read Git configuration", result);
        return ParseEffectiveConfig(key, result.StandardOutput);
    }

    private Task<ConfigValue?> ReadScopedConfigValueAsync(
        Repository? repository,
        string key,
        GitToolWriteScope scope,
        CancellationToken cancellationToken)
    {
        if (scope == GitToolWriteScope.Repository && repository is null) return Task.FromResult<ConfigValue?>(null);
        var argument = scope == GitToolWriteScope.Global ? "--global" : "--local";
        var source = scope == GitToolWriteScope.Global ? GitToolConfigurationSource.Global : GitToolConfigurationSource.Repository;
        return ReadConfigAtArgumentScopeAsync(repository, key, argument, source, cancellationToken);
    }

    private async Task<ConfigValue?> ReadConfigAtArgumentScopeAsync(
        Repository? repository,
        string key,
        string scopeArgument,
        GitToolConfigurationSource source,
        CancellationToken cancellationToken)
    {
        var result = await RunGitResultAsync(
            WorkingDirectory(repository),
            "GitToolsReadScope",
            GitCommandKind.Internal,
            cancellationToken,
            ["config", "--includes", scopeArgument, "--show-origin", "--get", key]);
        if (result.ExitCode is 1 or 5 or 128) return null;
        if (result.ExitCode != 0) throw GitFailure("Could not read Git configuration", result);
        return ParseScopedConfig(key, result.StandardOutput, source);
    }

    private async Task<IReadOnlyList<string>> ReadSupportedToolsAsync(
        Repository? repository,
        GitToolKind kind,
        CancellationToken cancellationToken)
    {
        var command = kind == GitToolKind.Diff ? "difftool" : "mergetool";
        var result = await RunGitResultAsync(
            WorkingDirectory(repository),
            "GitToolHelp",
            GitCommandKind.Internal,
            cancellationToken,
            [command, "--tool-help"]);
        if (result.ExitCode != 0) return [];

        var tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length == 0 || !char.IsWhiteSpace(line[0])) continue;
            var candidate = line.Trim();
            if (candidate.Length == 0 || candidate.Any(char.IsWhiteSpace)) continue;
            if (candidate.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '+' or '.'))
                tools.Add(candidate);
        }
        return tools.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task WriteIfChangedAsync(
        Repository? repository,
        GitToolWriteScope scope,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        var existing = await ReadScopedConfigValueAsync(repository, key, scope, cancellationToken);
        if (string.Equals(existing?.Value, value, StringComparison.Ordinal)) return;

        var result = await RunGitResultAsync(
            WorkingDirectory(repository),
            "GitToolsWrite",
            GitCommandKind.User,
            cancellationToken,
            ["config", ScopeArgument(scope), key, value]);
        if (result.ExitCode != 0) throw GitFailure($"Could not save {key}", result);
    }

    private async Task WriteOrUnsetIfChangedAsync(
        Repository? repository,
        GitToolWriteScope scope,
        string key,
        string? value,
        CancellationToken cancellationToken)
    {
        var existing = await ReadScopedConfigValueAsync(repository, key, scope, cancellationToken);
        if (value is null)
        {
            if (existing is null) return;
            await UnsetIfPresentAsync(repository, scope, key, cancellationToken);
            return;
        }
        if (string.Equals(existing?.Value, value, StringComparison.Ordinal)) return;
        await WriteIfChangedAsync(repository, scope, key, value, cancellationToken);
    }

    private async Task UnsetIfPresentAsync(
        Repository? repository,
        GitToolWriteScope scope,
        string key,
        CancellationToken cancellationToken)
    {
        if (await ReadScopedConfigValueAsync(repository, key, scope, cancellationToken) is null) return;
        var result = await RunGitResultAsync(
            WorkingDirectory(repository),
            "GitToolsUnset",
            GitCommandKind.User,
            cancellationToken,
            ["config", ScopeArgument(scope), "--unset-all", key]);
        if (result.ExitCode is 0 or 1 or 5) return;
        throw GitFailure($"Could not remove {key}", result);
    }

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
        var configuration = await ReadDiffOrMergeAsync(repository, GitToolKind.Diff, cancellationToken);
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
        var configuration = await ReadDiffOrMergeAsync(repository, GitToolKind.Merge, cancellationToken);
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

    private static string? ExtractExplicitCommandPath(string command)
    {
        var executable = ReadCommandExecutableToken(command.Trim());
        if (string.IsNullOrWhiteSpace(executable)) return null;
        return Path.IsPathRooted(executable)
               || executable.Contains(Path.DirectorySeparatorChar)
               || executable.Contains(Path.AltDirectorySeparatorChar)
            ? executable
            : null;
    }

    private static string ReadCommandExecutableToken(string command)
    {
        if (command.Length == 0) return string.Empty;
        if (command[0] is '"' or '\'')
        {
            var quote = command[0];
            var end = command.IndexOf(quote, 1);
            return end > 1 ? command[1..end] : command[1..];
        }

        var whitespace = command.IndexOfAny([' ', '\t', '\r', '\n']);
        return whitespace < 0 ? command : command[..whitespace];
    }

    private string? ResolveToolExecutable(
        string? tool,
        string? path,
        string? command,
        IReadOnlyList<GitToolPreset> presets)
    {
        if (!string.IsNullOrWhiteSpace(path)) return _externalProcess.ResolveExecutable(path);
        if (!string.IsNullOrWhiteSpace(command)) return _externalProcess.ResolveExecutable(command);
        if (string.IsNullOrWhiteSpace(tool)) return null;
        var preset = presets.FirstOrDefault(candidate => !candidate.IsCustom && string.Equals(candidate.Value, tool, StringComparison.OrdinalIgnoreCase));
        return _externalProcess.ResolveExecutable(preset?.ExecutableCommand ?? tool);
    }

    private IReadOnlyList<GitToolValidationMessage> ValidateConfiguration(
        GitToolKind kind,
        string? value,
        string? explicitPath,
        string? customCommand,
        string? resolvedExecutable,
        IReadOnlyList<GitToolPreset> presets,
        IReadOnlyList<string> supportedTools)
    {
        var messages = new List<GitToolValidationMessage>();
        if (string.IsNullOrWhiteSpace(value)) return messages;

        if (!string.IsNullOrWhiteSpace(explicitPath) && !_externalProcess.IsExecutablePathUsable(explicitPath))
            messages.Add(new GitToolValidationMessage(GitToolValidationSeverity.Error, $"Configured executable path is not usable: {explicitPath}"));

        if (!string.IsNullOrWhiteSpace(customCommand))
        {
            foreach (var variable in RequiredVariables(kind))
                if (!customCommand.Contains(variable, StringComparison.Ordinal))
                    messages.Add(new GitToolValidationMessage(GitToolValidationSeverity.Error, $"Custom command must contain {variable}."));
        }

        if (resolvedExecutable is null && string.IsNullOrWhiteSpace(explicitPath))
            messages.Add(new GitToolValidationMessage(GitToolValidationSeverity.Warning, "Executable could not be resolved in the current environment."));

        return messages;
    }

    private static void ValidateCustomCommand(GitToolKind kind, string? command)
    {
        if (command is null) return;
        foreach (var variable in RequiredVariables(kind))
            if (!command.Contains(variable, StringComparison.Ordinal))
                throw new ArgumentException($"Custom {kind.ToString().ToLowerInvariant()} command must contain {variable}.");
    }

    private static IReadOnlyList<string> RequiredVariables(GitToolKind kind) => kind switch
    {
        GitToolKind.Diff => ["$LOCAL", "$REMOTE"],
        GitToolKind.Merge => ["$LOCAL", "$REMOTE", "$MERGED"],
        _ => []
    };

    private static IReadOnlyList<string> SelectionKeys(GitToolKind kind, bool includeCrossToolFallback) => kind switch
    {
        GitToolKind.Diff when includeCrossToolFallback => ["diff.guitool", "merge.guitool", "diff.tool", "merge.tool"],
        GitToolKind.Diff => ["diff.guitool", "diff.tool"],
        GitToolKind.Merge => ["merge.guitool", "merge.tool"],
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static IEnumerable<string> ToolFieldKeys(GitToolKind kind, string tool, string field)
    {
        if (kind == GitToolKind.Diff)
        {
            yield return $"difftool.{tool}.{field}";
            yield return $"mergetool.{tool}.{field}";
            yield break;
        }
        yield return $"mergetool.{tool}.{field}";
    }

    private static IReadOnlyList<GitToolPreset> EditorPresets()
    {
        var presets = new List<GitToolPreset>
        {
            new("vscode", "Visual Studio Code", "code --wait", "code"),
            new("vim", "Vim", "vim", "vim"),
            new("nvim", "Neovim", "nvim", "nvim")
        };
        if (OperatingSystem.IsWindows())
            presets.Insert(1, new GitToolPreset("visual-studio", "Visual Studio", "devenv.exe /edit", "devenv.exe"));
        if (OperatingSystem.IsMacOS())
            presets.Add(new GitToolPreset("textedit", "TextEdit", "open -W -a TextEdit", "open"));
        presets.Add(new GitToolPreset("custom", "Custom", string.Empty, IsCustom: true));
        return presets;
    }

    private static IReadOnlyList<GitToolPreset> BuildToolPresets(GitToolKind kind, IReadOnlyList<string> supportedTools)
    {
        var presets = new List<GitToolPreset>
        {
            new("vscode", "Visual Studio Code", "vscode", "code"),
            new("meld", "Meld", "meld", "meld"),
            new("kdiff3", "KDiff3", "kdiff3", "kdiff3"),
            new("beyond-compare", "Beyond Compare", "bc", OperatingSystem.IsWindows() ? "bcomp" : "bcompare"),
            new("p4merge", "P4Merge", "p4merge", "p4merge"),
            new("araxis", "Araxis Merge", "araxis", OperatingSystem.IsWindows() ? "compare" : "compare")
        };
        if (OperatingSystem.IsWindows())
        {
            presets.Insert(1, new GitToolPreset("vsdiffmerge", "Visual Studio / vsdiffmerge", "vsdiffmerge", "vsdiffmerge"));
            presets.Add(new GitToolPreset("winmerge", "WinMerge", "winmerge", "WinMergeU"));
        }
        if (OperatingSystem.IsMacOS())
            presets.Add(new GitToolPreset("opendiff", "FileMerge / opendiff", "opendiff", "opendiff"));

        foreach (var tool in supportedTools)
        {
            if (presets.Any(preset => string.Equals(preset.Value, tool, StringComparison.OrdinalIgnoreCase))) continue;
            presets.Add(new GitToolPreset($"git-{tool}", $"Git: {tool}", tool, tool, IsDynamic: true));
        }
        presets.Add(new GitToolPreset("custom", "Custom", string.Empty, IsCustom: true));
        return presets;
    }

    private static ConfigValue? ParseEffectiveConfig(string key, string output)
    {
        var line = LastOutputLine(output);
        if (line is null) return null;
        var parts = line.Split('\t');
        if (parts.Length >= 3)
            return new ConfigValue(key, string.Join('\t', parts.Skip(2)), ParseSource(parts[0]), parts[1]);
        if (parts.Length == 2)
        {
            var prefix = parts[0].Trim();
            var separator = prefix.IndexOfAny([' ', '\t']);
            if (separator > 0)
                return new ConfigValue(key, parts[1], ParseSource(prefix[..separator]), prefix[(separator + 1)..].Trim());
        }
        return new ConfigValue(key, line, GitToolConfigurationSource.NotConfigured, null);
    }

    private static ConfigValue? ParseScopedConfig(string key, string output, GitToolConfigurationSource source)
    {
        var line = LastOutputLine(output);
        if (line is null) return null;
        var separator = line.IndexOf('\t');
        return separator < 0
            ? new ConfigValue(key, line, source, null)
            : new ConfigValue(key, line[(separator + 1)..], source, line[..separator].Trim());
    }

    private static string? LastOutputLine(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

    private static GitToolConfigurationSource ParseSource(string value) => value.Trim().ToLowerInvariant() switch
    {
        "worktree" => GitToolConfigurationSource.Worktree,
        "local" => GitToolConfigurationSource.Repository,
        "global" => GitToolConfigurationSource.Global,
        "system" => GitToolConfigurationSource.System,
        "command" => GitToolConfigurationSource.Environment,
        _ => GitToolConfigurationSource.NotConfigured
    };

    private static bool? ParseGitBoolean(string? value)
    {
        if (value is null) return null;
        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "on" or "1" => true,
            "false" or "no" or "off" or "0" => false,
            _ => null
        };
    }

    private static string? BoolText(bool? value) => value switch
    {
        true => "true",
        false => "false",
        null => null
    };

    private static GitToolScopeConfiguration EmptyScope(GitToolConfigurationSource source) =>
        new(source, null, null, null);

    private static string ScopeArgument(GitToolWriteScope scope) => scope == GitToolWriteScope.Global ? "--global" : "--local";

    private static string WorkingDirectory(Repository? repository) => repository?.WorkingDirectory ?? Environment.CurrentDirectory;

    private static void EnsureWritableScope(Repository? repository, GitToolWriteScope scope)
    {
        if (scope == GitToolWriteScope.Repository && repository is null)
            throw new InvalidOperationException("Open a repository before editing repository Git configuration.");
    }

    private static void EnsureRunnable(GitToolConfigurationSnapshot configuration, string displayName)
    {
        if (string.IsNullOrWhiteSpace(configuration.EffectiveValue))
            throw new InvalidOperationException($"{displayName} is not configured.");
        var error = configuration.Validation.FirstOrDefault(message => message.Severity == GitToolValidationSeverity.Error);
        if (error is not null) throw new InvalidOperationException(error.Message);
    }

    private static void ValidateToolName(string value)
    {
        if (value.Any(character => char.IsWhiteSpace(character) || character is '\0' or '\r' or '\n'))
            throw new ArgumentException("Git tool name cannot contain whitespace or control characters.");
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    private sealed record ConfigValue(
        string Key,
        string Value,
        GitToolConfigurationSource Source,
        string? Origin);
}
