namespace CSharpGit.Domain;

public enum RepositoryOperation
{
    None,
    Merge,
    Rebase,
    CherryPick,
    Revert,
    Bisect
}

public enum FileChangeKind { None, Modified, Added, Untracked, Deleted, Renamed, Conflicted }

public enum ConflictKind { Textual, AddAdd, ModifyDelete, DeleteModify, Binary }
public enum ConflictResolutionSide { CurrentLocal, IncomingRemote }

public enum GitConfigurationScope { Global, RepositoryLocal }
public enum MergeToolConfigurationKind { Preset, CustomExecutable, CustomCommand }

public sealed record MergeToolConfiguration(
    string Name,
    MergeToolConfigurationKind Kind,
    GitConfigurationScope Scope,
    string? ExecutablePath = null,
    string? CommandArguments = null);

public static class MergeToolPresets
{
    public static IReadOnlyList<string> Known { get; } =
        ["araxis", "bc", "bc3", "bc4", "codecompare", "deltawalker", "diffmerge", "diffuse", "ecmerge",
         "emerge", "examdiff", "guiffy", "gvimdiff", "kdiff3", "meld", "nvimdiff", "opendiff", "p4merge",
         "smerge", "tkdiff", "tortoisemerge", "vimdiff", "vscode", "winmerge", "xxdiff"];
}

public sealed record ConflictFile(
    string Path,
    ConflictKind Kind,
    bool IsResolved,
    bool CanOpenManually,
    bool CanChooseCurrentLocal,
    bool CanChooseIncomingRemote,
    bool CanKeepDeletion,
    bool CanStage,
    bool CanRunMergeTool,
    string CurrentLocalLabel,
    string IncomingRemoteLabel)
{
    public string Display => $"{Path} — {Kind}, {(IsResolved ? "resolved" : "unresolved")}";
}

public sealed record RepositoryOperationState(
    RepositoryOperation Kind,
    IReadOnlyList<ConflictFile> Conflicts,
    bool CanContinue,
    bool CanAbort,
    bool CanSkip)
{
    public static RepositoryOperationState None { get; } = new(RepositoryOperation.None, [], false, false, false);
}

public sealed record GitBranch(
    string Name,
    string Commit,
    bool IsCurrent = false,
    string? Upstream = null,
    int Ahead = 0,
    int Behind = 0);

public sealed record GitRemote(string Name, string FetchUrl, string PushUrl);

public sealed record GitTag(string Name, string Commit);

public sealed record GitStash(string Name, string Commit, string Message)
{
    public string Display => $"{Name}: {Message}";
}

public enum MergeResultKind { FastForward, MergeCommit, UpToDate, Conflicts, Refused }

public sealed record MergeResult(MergeResultKind Kind, string Message);

public enum RebaseAction { Pick, Reword, Squash, Fixup, Drop }
public sealed record RebasePlanItem(string Commit, string Subject, RebaseAction Action, string? NewMessage = null)
{
    public string Display => $"{Action.ToString().ToLowerInvariant()} {Commit[..Math.Min(8, Commit.Length)]} {Subject}";
}
public sealed record InteractiveRebasePlan(string Onto, IReadOnlyList<RebasePlanItem> Items);
public enum RebaseResultKind { Completed, Conflicts, Failed }
public sealed record RebaseResult(RebaseResultKind Kind, string Message);

public enum ApplyCommitResultKind
{
    Completed,
    Conflicts,
    Failed
}

public sealed record ApplyCommitResult(
    ApplyCommitResultKind Kind,
    string Message,
    string? HeadCommit = null);

public enum ResetMode
{
    Soft,
    Mixed,
    Hard
}

public sealed record GitReferences(
    IReadOnlyList<GitBranch> LocalBranches,
    IReadOnlyList<GitBranch> RemoteBranches,
    IReadOnlyList<GitRemote> Remotes,
    IReadOnlyList<GitTag> Tags)
{
    public static GitReferences Empty { get; } = new([], [], [], []);
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ByCommit =>
        LocalBranches.Select(branch => (branch.Commit, branch.Name))
            .Concat(RemoteBranches.Select(branch => (branch.Commit, branch.Name)))
            .Concat(Tags.Select(tag => (tag.Commit, $"tag: {tag.Name}")))
            .GroupBy(item => item.Commit, item => item.Item2, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.ToList(), StringComparer.Ordinal);
}

public sealed record WorkingTreeChange(string Path, char IndexStatus, char WorkingTreeStatus, string? OriginalPath = null)
{
    public bool IsStaged => IndexStatus is not ' ' and not '?';
    public bool IsUnstaged => WorkingTreeStatus is not ' ';
    public bool IsConflicted => IndexStatus == 'U' || WorkingTreeStatus == 'U' ||
        (IndexStatus, WorkingTreeStatus) is ('A', 'A') or ('D', 'D') or ('A', 'U') or ('U', 'D') or ('U', 'A') or ('D', 'U');
    public FileChangeKind Kind => IsConflicted ? FileChangeKind.Conflicted :
        IndexStatus == '?' ? FileChangeKind.Untracked :
        IndexStatus == 'R' || WorkingTreeStatus == 'R' ? FileChangeKind.Renamed :
        IndexStatus == 'D' || WorkingTreeStatus == 'D' ? FileChangeKind.Deleted :
        IndexStatus == 'A' || WorkingTreeStatus == 'A' ? FileChangeKind.Added : FileChangeKind.Modified;
    public string StatusDisplay => $"{Kind}: {(IsStaged ? "staged" : "")}{(IsStaged && IsUnstaged ? " + " : "")}{(IsUnstaged ? "unstaged" : "")}";
}

public sealed record RepositoryState(
    Repository Repository,
    string? HeadReference,
    string? HeadCommit,
    bool IsDetached,
    RepositoryOperation Operation,
    IReadOnlyList<WorkingTreeChange> Changes,
    IReadOnlyDictionary<string, string> GlobalConfiguration,
    IReadOnlyDictionary<string, string> LocalConfiguration,
    DateTimeOffset ReadAtUtc,
    GitReferences? References = null,
    IReadOnlyList<GitStash>? StashEntries = null,
    RepositoryOperationState? OperationDetails = null)
{
    public GitReferences Refs => References ?? GitReferences.Empty;
    public IReadOnlyList<GitStash> Stashes => StashEntries ?? [];
    public RepositoryOperationState CurrentOperation => OperationDetails ?? RepositoryOperationState.None;
}
