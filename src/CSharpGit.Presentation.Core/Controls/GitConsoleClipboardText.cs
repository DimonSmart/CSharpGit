using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.Controls;

internal static class GitConsoleClipboardText
{
    public static string Build(GitCommandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var exitCode = activity.ExitCode?.ToString() ?? string.Empty;
        var standardOutput = GitOutputTextEscaper.EscapeUnsafeControls(activity.StandardOutput);
        var standardError = GitOutputTextEscaper.EscapeUnsafeControls(activity.StandardError);

        return
            $"Command: {activity.DisplayCommand}{Environment.NewLine}" +
            $"Working directory: {activity.WorkingDirectory}{Environment.NewLine}" +
            $"Exit code: {exitCode}{Environment.NewLine}" +
            $"Duration: {GitCommandConsoleItem.FormatDuration(activity.Duration)}{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{standardOutput}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{standardError}";
    }
}
