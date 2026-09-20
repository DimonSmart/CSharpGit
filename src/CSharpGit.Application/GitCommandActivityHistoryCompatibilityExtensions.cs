using CSharpGit.Application.Abstractions;

namespace CSharpGit.Application;

public static class GitCommandActivityHistoryCompatibilityExtensions
{
    public static void Completed(
        this GitCommandActivityHistory history,
        Guid id,
        int exitCode,
        string standardOutput,
        string standardError)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (!string.IsNullOrEmpty(standardOutput))
            history.OutputReceived(id, GitOutputStream.StandardOutput, standardOutput);
        if (!string.IsNullOrEmpty(standardError))
            history.OutputReceived(id, GitOutputStream.StandardError, standardError);
        history.Completed(id, exitCode);
    }

    public static void Cancelled(
        this GitCommandActivityHistory history,
        Guid id,
        int? exitCode,
        string standardOutput,
        string standardError)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (!string.IsNullOrEmpty(standardOutput))
            history.OutputReceived(id, GitOutputStream.StandardOutput, standardOutput);
        if (!string.IsNullOrEmpty(standardError))
            history.OutputReceived(id, GitOutputStream.StandardError, standardError);
        history.Cancelled(id, exitCode);
    }
}
