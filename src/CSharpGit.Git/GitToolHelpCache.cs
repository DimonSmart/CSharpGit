using System.Security.Cryptography;
using System.Text;

using CSharpGit.Domain;

namespace CSharpGit.Git;

/// <summary>
/// Bounded cache of successful tool-help results only. Configuration values themselves
/// are never cached. A fingerprint of Git-resolved tool definitions invalidates the
/// entry immediately when a repository or include changes.
/// </summary>
internal static class GitToolHelpCache
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, (DateTimeOffset Expires, IReadOnlyList<string> Tools)> Entries = [];
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private const int MaximumEntries = 64;

    internal static string Key(GitToolKind kind, string executable, GitConfigSnapshot config)
    {
        // All inputs affect Git's built-in/custom tool discovery. Do not retain
        // user config values or paths in the key (only a cryptographic digest).
        var text = new StringBuilder();
        text.Append((int)kind).Append('\0')
            .Append(executable).Append('\0')
            .Append(Environment.GetEnvironmentVariable("PATH")).Append('\0')
            .Append(Environment.GetEnvironmentVariable("GIT_EXEC_PATH")).Append('\0')
            .Append(Environment.GetEnvironmentVariable("PATHEXT")).Append('\0')
            .Append(Environment.GetEnvironmentVariable("DISPLAY")).Append('\0')
            .Append(Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL")).Append('\0')
            .Append(Environment.GetEnvironmentVariable("GIT_CONFIG_SYSTEM")).Append('\0')
            .Append(Environment.GetEnvironmentVariable("HOME")).Append('\0')
            .Append(Environment.GetEnvironmentVariable("USERPROFILE")).Append('\0')
            .Append(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")).Append('\0')
            .Append(ExecutableTimestamp(executable)).Append('\0');

        foreach (var entry in config.Entries)
        {
            if (!entry.Key.StartsWith("difftool.", StringComparison.OrdinalIgnoreCase) &&
                !entry.Key.StartsWith("mergetool.", StringComparison.OrdinalIgnoreCase))
                continue;
            text.Append(entry.Key).Append('\0').Append(entry.Source).Append('\0')
                .Append(entry.Value).Append('\0');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    internal static bool TryRead(string key, out IReadOnlyList<string> tools)
    {
        lock (Sync)
        {
            if (Entries.TryGetValue(key, out var entry) && entry.Expires > DateTimeOffset.UtcNow)
            {
                tools = entry.Tools;
                return true;
            }
        }
        tools = [];
        return false;
    }

    internal static void Store(string key, IReadOnlyList<string> tools)
    {
        lock (Sync)
        {
            if (Entries.Count >= MaximumEntries)
            {
                // This is a tiny, optional optimization; prefer a bounded reset over
                // a process-lifetime cache that accumulates per-repository keys.
                Entries.Clear();
            }
            Entries[key] = (DateTimeOffset.UtcNow + Lifetime, tools.ToArray());
        }
    }

    private static long ExecutableTimestamp(string executable)
    {
        try
        {
            if (File.Exists(executable)) return File.GetLastWriteTimeUtc(executable).Ticks;
            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path)) return 0;
            var names = OperatingSystem.IsWindows() && !Path.HasExtension(executable)
                ? new[] { executable + ".exe", executable }
                : new[] { executable };
            foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var name in names)
                {
                    var candidate = Path.Combine(folder.Trim('"'), name);
                    if (File.Exists(candidate)) return File.GetLastWriteTimeUtc(candidate).Ticks;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Refresh via short TTL if executable metadata cannot be read.
        }
        return 0;
    }
}
