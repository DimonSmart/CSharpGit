using System.Text;
using System.Text.RegularExpressions;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Application;

public sealed class GitCommandActivityHistory : IGitCommandActivitySink, IGitCommandActivitySource
{
    public const int MaximumHistoryEntries = 100;
    public const int MaximumOutputBytes = 1024 * 1024;
    internal const string TruncationMarker = "\n[output truncated]\n";

    private readonly object _sync = new();
    private readonly List<ActivityState> _entries = [];
    private readonly Dictionary<Guid, ActivityState> _byId = [];

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

        var safeArguments = GitCommandFormatter.SanitizeArguments(arguments);
        var state = new ActivityState(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            workingDirectory,
            GitCommandFormatter.DisplayExecutable(executable),
            safeArguments,
            GitCommandFormatter.Format(executable, safeArguments, argumentsAreSanitized: true),
            commandKind);

        ActivityState? evicted = null;
        GitCommandActivity snapshot;
        lock (_sync)
        {
            _entries.Add(state);
            _byId.Add(state.Id, state);
            if (_entries.Count > MaximumHistoryEntries)
            {
                evicted = _entries[0];
                _entries.RemoveAt(0);
                _byId.Remove(evicted.Id);
            }

            snapshot = CreateSnapshot(state);
        }

        Changed?.Invoke(this, new GitCommandActivityChangedEventArgs(
            snapshot,
            GitCommandActivityChangeKind.Started,
            evicted?.Id,
            evicted?.CommandKind));
        return state.Id;
    }

    public void OutputReceived(Guid id, GitOutputStream stream, string chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.Length == 0) return;

        GitCommandActivityChangedEventArgs? eventArgs = null;
        lock (_sync)
        {
            if (!_byId.TryGetValue(id, out var state) || state.Status != GitCommandStatus.Running)
                return;

            var result = state.GetBuffer(stream).Append(chunk);
            if (result.Changed)
            {
                eventArgs = new GitCommandActivityChangedEventArgs(
                    id,
                    state.CommandKind,
                    stream,
                    result.PublishedChunk,
                    result.RequiresResync);
            }
        }

        if (eventArgs is not null)
            Changed?.Invoke(this, eventArgs);
    }

    public void Completed(Guid id, int exitCode) =>
        Finish(id, exitCode, exitCode == 0 ? GitCommandStatus.Succeeded : GitCommandStatus.Failed);

    public void Cancelled(Guid id, int? exitCode) =>
        Finish(id, exitCode, GitCommandStatus.Cancelled);

    public GitCommandActivity? Get(Guid id)
    {
        lock (_sync)
            return _byId.TryGetValue(id, out var state) ? CreateSnapshot(state) : null;
    }

    public IReadOnlyList<GitCommandActivity> GetSnapshot(GitCommandFilter filter = GitCommandFilter.UserCommands)
    {
        lock (_sync)
        {
            return _entries
                .Where(entry => filter == GitCommandFilter.AllCommands || entry.CommandKind == GitCommandKind.User)
                .Reverse<ActivityState>()
                .Select(CreateSnapshot)
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
                    return CreateSnapshot(entry);
            }
            return null;
        }
    }

    private void Finish(Guid id, int? exitCode, GitCommandStatus status)
    {
        GitCommandActivity snapshot;
        lock (_sync)
        {
            if (!_byId.TryGetValue(id, out var state)) return;
            if (state.Status != GitCommandStatus.Running) return;

            var completedAt = DateTimeOffset.UtcNow;
            state.CompletedAt = completedAt;
            state.Duration = completedAt - state.StartedAt;
            state.ExitCode = exitCode;
            state.Status = status;
            snapshot = CreateSnapshot(state);
        }

        Changed?.Invoke(this, new GitCommandActivityChangedEventArgs(
            snapshot,
            status == GitCommandStatus.Cancelled
                ? GitCommandActivityChangeKind.Cancelled
                : GitCommandActivityChangeKind.Completed));
    }

    private static GitCommandActivity CreateSnapshot(ActivityState state)
    {
        var duration = state.CompletedAt is null
            ? DateTimeOffset.UtcNow - state.StartedAt
            : state.Duration;

        return new GitCommandActivity(
            state.Id,
            state.StartedAt,
            state.CompletedAt,
            duration,
            state.WorkingDirectory,
            state.Executable,
            state.Arguments,
            state.DisplayCommand,
            state.CommandKind,
            state.ExitCode,
            state.StandardOutput.ToString(),
            state.StandardError.ToString(),
            state.Status,
            state.StandardOutput.Truncated,
            state.StandardError.Truncated);
    }

    private sealed class ActivityState
    {
        public ActivityState(
            Guid id,
            DateTimeOffset startedAt,
            string workingDirectory,
            string executable,
            IReadOnlyList<string> arguments,
            string displayCommand,
            GitCommandKind commandKind)
        {
            Id = id;
            StartedAt = startedAt;
            WorkingDirectory = workingDirectory;
            Executable = executable;
            Arguments = arguments;
            DisplayCommand = displayCommand;
            CommandKind = commandKind;
        }

        public Guid Id { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset? CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public string WorkingDirectory { get; }
        public string Executable { get; }
        public IReadOnlyList<string> Arguments { get; }
        public string DisplayCommand { get; }
        public GitCommandKind CommandKind { get; }
        public int? ExitCode { get; set; }
        public GitCommandStatus Status { get; set; } = GitCommandStatus.Running;
        public BoundedUtf8TextBuffer StandardOutput { get; } = new(MaximumOutputBytes, TruncationMarker);
        public BoundedUtf8TextBuffer StandardError { get; } = new(MaximumOutputBytes, TruncationMarker);

        public BoundedUtf8TextBuffer GetBuffer(GitOutputStream stream) => stream switch
        {
            GitOutputStream.StandardOutput => StandardOutput,
            GitOutputStream.StandardError => StandardError,
            _ => throw new ArgumentOutOfRangeException(nameof(stream))
        };
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
