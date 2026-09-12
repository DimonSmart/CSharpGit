namespace CSharpGit.Domain;

public enum GitToolKind
{
    Editor,
    Diff,
    Merge
}

public enum GitToolConfigurationSource
{
    Environment,
    Worktree,
    Repository,
    Global,
    System,
    GitDefault,
    NotConfigured
}

public enum GitToolWriteScope
{
    Global,
    Repository
}

public enum GitToolValidationSeverity
{
    Warning,
    Error
}

public sealed record GitToolPreset(
    string Id,
    string DisplayName,
    string Value,
    string? ExecutableCommand = null,
    bool IsCustom = false,
    bool IsDynamic = false);

public sealed record GitToolValidationMessage(
    GitToolValidationSeverity Severity,
    string Message);

public sealed record GitToolScopeConfiguration(
    GitToolConfigurationSource Source,
    string? SelectionKey,
    string? SelectionValue,
    string? Origin,
    string? Path = null,
    string? Command = null,
    bool? TrustExitCode = null,
    bool? KeepBackup = null)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SelectionValue)
        || !string.IsNullOrWhiteSpace(Path)
        || !string.IsNullOrWhiteSpace(Command)
        || TrustExitCode is not null
        || KeepBackup is not null;
}

public sealed record GitToolConfigurationSnapshot(
    GitToolKind Kind,
    string? EffectiveValue,
    GitToolConfigurationSource EffectiveSource,
    string? EffectiveOrigin,
    string? EffectivePath,
    string? EffectiveCommand,
    bool? EffectiveTrustExitCode,
    bool? EffectiveKeepBackup,
    string? ResolvedExecutable,
    GitToolScopeConfiguration Global,
    GitToolScopeConfiguration Repository,
    GitToolScopeConfiguration Worktree,
    GitToolScopeConfiguration System,
    IReadOnlyList<GitToolPreset> Presets,
    IReadOnlyList<string> SupportedTools,
    IReadOnlyList<GitToolValidationMessage> Validation)
{
    public bool HasErrors => Validation.Any(message => message.Severity == GitToolValidationSeverity.Error);
}

public sealed record GitToolEdit(
    GitToolKind Kind,
    GitToolWriteScope Scope,
    string Value,
    string? Path = null,
    string? Command = null,
    bool? TrustExitCode = null,
    bool? KeepBackup = null,
    bool UpdatePath = false,
    bool UpdateCommand = false,
    bool UpdateTrustExitCode = false,
    bool UpdateKeepBackup = false);
