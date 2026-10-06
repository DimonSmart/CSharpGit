using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public enum DiffLoadMode
{
    Preview,
    Full
}

public static class DiffPreviewPolicy
{
    public const long LargeFileBytes = 5L * 1024 * 1024;
    public const int LargeChangedLines = 10_000;
    public const int AutomaticOutputBytes = 1024 * 1024;
}

public sealed class DiffPreviewTooLargeException : Exception
{
    public DiffPreviewTooLargeException(int limitBytes)
        : base($"Diff output exceeded the automatic {limitBytes}-byte preview limit.")
    {
        LimitBytes = limitBytes;
    }

    public int LimitBytes { get; }
}

public interface IWorkingTreeDiffLoadService
{
    Task<FileDiff> ReadDiffAsync(
        Repository repository,
        WorkingTreeChange change,
        WorkingTreeDiffKind kind,
        DiffLoadMode mode,
        CancellationToken cancellationToken = default);
}
