namespace CSharpGit.Application.Abstractions;

public enum GitCommandKind
{
    User,
    Internal
}

public enum GitCommandStatus
{
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public enum GitCommandFilter
{
    UserCommands,
    AllCommands
}

public sealed record GitCommandActivity(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    TimeSpan Duration,
    string WorkingDirectory,
    string Executable,
    IReadOnlyList<string> Arguments,
    string DisplayCommand,
    GitCommandKind CommandKind,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    GitCommandStatus Status,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated);

public sealed class GitCommandActivityChangedEventArgs(GitCommandActivity activity) : EventArgs
{
    public GitCommandActivity Activity { get; } = activity;
}

public interface IGitCommandActivitySink
{
    Guid Started(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandKind commandKind);

    void Completed(
        Guid id,
        int exitCode,
        string standardOutput,
        string standardError);

    void Cancelled(
        Guid id,
        int? exitCode,
        string standardOutput,
        string standardError);
}

public interface IGitCommandActivitySource
{
    event EventHandler<GitCommandActivityChangedEventArgs>? Changed;

    IReadOnlyList<GitCommandActivity> GetSnapshot(GitCommandFilter filter = GitCommandFilter.UserCommands);

    GitCommandActivity? GetLatest(GitCommandFilter filter = GitCommandFilter.UserCommands);
}
