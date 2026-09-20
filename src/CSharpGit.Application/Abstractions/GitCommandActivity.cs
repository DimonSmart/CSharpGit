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

public enum GitOutputStream
{
    StandardOutput,
    StandardError
}

public enum GitCommandActivityChangeKind
{
    Started,
    Output,
    Completed,
    Cancelled
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

public sealed class GitCommandActivityChangedEventArgs : EventArgs
{
    public GitCommandActivityChangedEventArgs(
        GitCommandActivity activity,
        GitCommandActivityChangeKind changeKind,
        Guid? evictedActivityId = null,
        GitCommandKind? evictedCommandKind = null)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (changeKind == GitCommandActivityChangeKind.Output)
            throw new ArgumentException("Use the output event constructor for output changes.", nameof(changeKind));

        ActivityId = activity.Id;
        CommandKind = activity.CommandKind;
        ChangeKind = changeKind;
        Activity = activity;
        EvictedActivityId = evictedActivityId;
        EvictedCommandKind = evictedCommandKind;
    }

    public GitCommandActivityChangedEventArgs(
        Guid activityId,
        GitCommandKind commandKind,
        GitOutputStream outputStream,
        string outputChunk,
        bool outputRequiresResync = false)
    {
        ArgumentNullException.ThrowIfNull(outputChunk);
        ActivityId = activityId;
        CommandKind = commandKind;
        ChangeKind = GitCommandActivityChangeKind.Output;
        OutputStream = outputStream;
        OutputChunk = outputChunk;
        OutputRequiresResync = outputRequiresResync;
    }

    public Guid ActivityId { get; }
    public GitCommandKind CommandKind { get; }
    public GitCommandActivityChangeKind ChangeKind { get; }
    public GitCommandActivity? Activity { get; }
    public GitOutputStream? OutputStream { get; }
    public string? OutputChunk { get; }
    public bool OutputRequiresResync { get; }
    public Guid? EvictedActivityId { get; }
    public GitCommandKind? EvictedCommandKind { get; }
}

public interface IGitCommandActivitySink
{
    Guid Started(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandKind commandKind);

    void OutputReceived(Guid id, GitOutputStream stream, string chunk);

    void Completed(Guid id, int exitCode);

    void Cancelled(Guid id, int? exitCode);
}

public interface IGitCommandActivitySource
{
    event EventHandler<GitCommandActivityChangedEventArgs>? Changed;

    GitCommandActivity? Get(Guid id);

    IReadOnlyList<GitCommandActivity> GetSnapshot(GitCommandFilter filter = GitCommandFilter.UserCommands);

    GitCommandActivity? GetLatest(GitCommandFilter filter = GitCommandFilter.UserCommands);
}
