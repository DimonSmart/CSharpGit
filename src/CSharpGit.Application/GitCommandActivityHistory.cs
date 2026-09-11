using System.Text;
using System.Text.RegularExpressions;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Application;

public sealed class GitCommandActivityHistory : IGitCommandActivitySink, IGitCommandActivitySource
{
    public const int MaximumHistoryEntries = 100;
    public const int MaximumOutputBytes = 1024 * 1024;
    private const string TruncationMarker = "\n[output truncated]\n";

    private readonly object _sync = new();
    private readonly List<GitCommandActivity> _entries = [];

    public event EventHandler<GitCommandActivityChangedEventArgs>? Changed;

    public Guid Started(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandKind commandKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        var startedAt = DateTimeOffset.UtcNow;
        var safeArguments = GitCommandFormatter.SanitizeArguments(arguments);
        var activity = new GitCommandActivity(
            Guid.NewGuid(),
            startedAt,
            null,
            TimeSpan.Zero,
            workingDirectory,
            GitCommandFormatter.DisplayExecutable(executable),
            safeArguments,
            GitCommandFormatter.Format(executable, safeArguments, argumentsAreSanitized: true),
            commandKind,
            null,
            string.Empty,
            string.Empty,
            GitCommandStatus.Running,
            false,
            false);

        lock (_sync)
        {
            _entries.Add(activity);
            while (_entries.Count > MaximumHistoryEntries)
                _entries.RemoveAt(0);
        }

        Changed?.Invoke(this, new GitCommandActivityChangedEventArgs(activity));
        return activity.Id;
    }

    public void Completed(Guid id, int exitCode, string standardOutput, string standardError) =>
        Finish(id, exitCode, standardOutput, standardError,
            exitCode == 0 ? GitCommandStatus.Succeeded : GitCommandStatus.Failed);

    public void Cancelled(Guid id, int? exitCode, string standardOutput, string standardError) =>
        Finish(id, exitCode, standardOutput, standardError, GitCommandStatus.Cancelled);

    public IReadOnlyList<GitCommandActivity> GetSnapshot(GitCommandFilter filter = GitCommandFilter.UserCommands)
    {
        lock (_sync)
        {
            return _entries
                .Where(entry => filter == GitCommandFilter.AllCommands || entry.CommandKind == GitCommandKind.User)
                .Reverse()
                .ToArray();
        }
    }

    public GitCommandActivity? GetLatest(GitCommandFilter filter = GitCommandFilter.UserCommands)
    {
        lock (_sync)
        {
            for (var index = _entries.Count - 1; index >= 0; index--)
            {
                var entry = _entries[index];
                if (filter == GitCommandFilter.AllCommands || entry.CommandKind == GitCommandKind.User)
                    return entry;
            }
            return null;
        }
    }

    private void Finish(
        Guid id,
        int? exitCode,
        string standardOutput,
        string standardError,
        GitCommandStatus status)
    {
        GitCommandActivity? updated = null;
        lock (_sync)
        {
            var index = _entries.FindIndex(entry => entry.Id == id);
            if (index < 0) return;

            var current = _entries[index];
            var completedAt = DateTimeOffset.UtcNow;
            var output = TruncateOutput(standardOutput ?? string.Empty);
            var error = TruncateOutput(standardError ?? string.Empty);
            updated = current with
            {
                CompletedAt = completedAt,
                Duration = completedAt - current.StartedAt,
                ExitCode = exitCode,
                StandardOutput = output.Text,
                StandardError = error.Text,
                Status = status,
                StandardOutputTruncated = output.Truncated,
                StandardErrorTruncated = error.Truncated
            };
            _entries[index] = updated;
        }

        Changed?.Invoke(this, new GitCommandActivityChangedEventArgs(updated));
    }

    internal static (string Text, bool Truncated) TruncateOutput(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= MaximumOutputBytes) return (value, false);

        var markerBytes = Encoding.UTF8.GetByteCount(TruncationMarker);
        var prefixLength = Math.Max(0, MaximumOutputBytes - markerBytes);
        var prefix = Encoding.UTF8.GetString(bytes, 0, prefixLength);
        return (prefix + TruncationMarker, true);
    }
}

public static class GitCommandActivitySession
{
    public static GitCommandActivityHistory Current { get; } = new();
}

public static partial class GitCommandFormatter
{
    private static readonly Regex UrlCredentialsRegex = UrlCredentialsPattern();
    private static readonly Regex AuthorizationRegex = AuthorizationPattern();
    private static readonly Regex SensitiveAssignmentRegex = SensitiveAssignmentPattern();

    private static readonly HashSet<string> SensitiveStandaloneOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--password",
        "--token",
        "--authorization",
        "--credential",
        "password",
        "token",
        "authorization",
        "credential"
    };

    public static string Format(string executable, IReadOnlyList<string> arguments) =>
        Format(executable, SanitizeArguments(arguments), argumentsAreSanitized: true);

    internal static string Format(
        string executable,
        IReadOnlyList<string> arguments,
        bool argumentsAreSanitized)
    {
        var safeArguments = argumentsAreSanitized ? arguments : SanitizeArguments(arguments);
        return string.Join(' ', new[] { DisplayExecutable(executable) }.Concat(safeArguments.Select(QuoteArgument)));
    }

    public static IReadOnlyList<string> SanitizeArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var result = new string[arguments.Count];
        var maskNext = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index] ?? string.Empty;
            if (maskNext)
            {
                result[index] = "***";
                maskNext = false;
                continue;
            }

            var trimmed = argument.Trim();
            if (SensitiveStandaloneOptions.Contains(trimmed))
            {
                result[index] = trimmed;
                maskNext = true;
                continue;
            }

            result[index] = SanitizeValue(argument);
        }
        return result;
    }

    public static string DisplayExecutable(string executable)
    {
        var fileName = Path.GetFileName(executable);
        if (string.Equals(fileName, "git.exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "git", StringComparison.OrdinalIgnoreCase))
            return "git";
        return string.IsNullOrWhiteSpace(fileName) ? executable : fileName;
    }

    private static string SanitizeValue(string value)
    {
        var result = UrlCredentialsRegex.Replace(value, match =>
            $"{match.Groups["scheme"].Value}{match.Groups["user"].Value}:***@");
        result = AuthorizationRegex.Replace(result, match => $"{match.Groups["prefix"].Value}***");
        result = SensitiveAssignmentRegex.Replace(result, match => $"{match.Groups["prefix"].Value}***");
        return result;
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length == 0) return "\"\"";
        if (!argument.Any(char.IsWhiteSpace) && !argument.Contains('"')) return argument;

        var builder = new StringBuilder(argument.Length + 2);
        builder.Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                builder.Append('\\', backslashes);
                backslashes = 0;
            }
            builder.Append(character);
        }
        if (backslashes > 0) builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }

    [GeneratedRegex(@"(?<scheme>https?://)(?<user>[^:/@\s]+):[^@/\s]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlCredentialsPattern();

    [GeneratedRegex(@"(?<prefix>authorization\s*:\s*(?:(?:basic|bearer)\s+)?)\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationPattern();

    [GeneratedRegex(@"(?<prefix>(?:--)?(?:password|token|access[_-]?token|credential)\s*=\s*)[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveAssignmentPattern();
}
