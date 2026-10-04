using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public enum StashScope
{
    AllTrackedChanges,
    StagedChangesOnly,
    SelectedPaths
}

public sealed record StashSelectedPath(
    string Path,
    string? OriginalPath = null,
    bool IsUntracked = false);

public sealed record CreateStashRequest(
    string? Message,
    StashScope Scope,
    IReadOnlyList<StashSelectedPath>? Paths = null,
    bool IncludeUntracked = false);

public enum StashChangeState
{
    Staged,
    Unstaged,
    StagedAndUnstaged,
    Untracked
}

public sealed record StashChangedFile(
    ChangedFile File,
    StashChangeState State,
    bool HasCombinedDiff)
{
    public string StateLabel => State switch
    {
        StashChangeState.Staged => "staged",
        StashChangeState.Unstaged => "unstaged",
        StashChangeState.StagedAndUnstaged => "staged + unstaged",
        StashChangeState.Untracked => "untracked",
        _ => string.Empty
    };
}

public sealed record StashDetails(
    GitStash Stash,
    CommitHistoryItem WorkingTreeCommit,
    string BaseCommit,
    string IndexCommit,
    string? UntrackedCommit,
    IReadOnlyList<StashChangedFile> Changes)
{
    public int AddedLines => Changes
        .Where(change => change.State == StashChangeState.Untracked || change.HasCombinedDiff)
        .Sum(change => change.File.AddedLines ?? 0);

    public int RemovedLines => Changes
        .Where(change => change.State != StashChangeState.Untracked && change.HasCombinedDiff)
        .Sum(change => change.File.RemovedLines ?? 0);
}

public interface IStashService
{
    Task<StashDetails> ReadAsync(
        Repository repository,
        GitStash stash,
        CancellationToken cancellationToken = default);
}
