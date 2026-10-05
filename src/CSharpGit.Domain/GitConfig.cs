namespace CSharpGit.Domain;

public enum GitConfigSource
{
    Command,
    Worktree,
    Repository,
    Global,
    System,
    NotConfigured
}

public enum GitConfigScope
{
    Worktree,
    Repository,
    Global,
    System
}

public sealed record GitConfigValue(
    string Key,
    string Value,
    GitConfigSource Source,
    string? Origin);
