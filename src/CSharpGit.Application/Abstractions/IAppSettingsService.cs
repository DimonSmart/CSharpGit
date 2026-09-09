namespace CSharpGit.Application.Abstractions;

public enum CommitTimeDisplayMode
{
    Smart,
    Relative,
    Absolute
}

public interface IAppSettingsService
{
    CommitTimeDisplayMode CommitTimeDisplayMode { get; }
    event EventHandler? Changed;

    Task SetCommitTimeDisplayModeAsync(
        CommitTimeDisplayMode mode,
        CancellationToken cancellationToken = default);
}
