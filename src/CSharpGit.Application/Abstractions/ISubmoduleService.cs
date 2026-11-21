using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public sealed record SubmoduleInfo(
    string Path,
    string RecordedCommit,
    string? CurrentCommit,
    string? Branch,
    string? Url,
    bool IsInitialized,
    bool IsConflicted,
    bool HasCommitChange,
    bool ModifiedContent,
    bool UntrackedContent,
    string? ErrorMessage = null)
{
    public bool HasLocalChanges => ModifiedContent || UntrackedContent;
    public bool CanOpen => IsInitialized && !IsConflicted && ErrorMessage is null;

    public string StatusText => IsConflicted ? "Gitlink conflict"
        : ErrorMessage is not null ? "Error: " + ErrorMessage
        : !IsInitialized ? "Not initialized"
        : $"{(HasCommitChange ? "Different commit" : "Recorded commit")}" +
          (HasLocalChanges ? " · uncommitted changes" : "");
}

public interface ISubmoduleService
{
    Task<IReadOnlyList<SubmoduleInfo>> ReadAsync(Repository repository, CancellationToken cancellationToken = default);
    Task InitializeAsync(Repository repository, string path, CancellationToken cancellationToken = default);
    Task UpdateAsync(Repository repository, string path, CancellationToken cancellationToken = default);
    Task UpdateAllAsync(Repository repository, CancellationToken cancellationToken = default);
}
