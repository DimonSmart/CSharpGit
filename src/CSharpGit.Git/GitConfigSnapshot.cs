using CSharpGit.Domain;

namespace CSharpGit.Git;

/// <summary>
/// A per-operation, ordered view of Git's own config resolution. Never cached across operations.
/// The NUL protocol is: scope NUL origin NUL key LF value NUL for every entry.
/// </summary>
internal sealed class GitConfigSnapshot
{
    private readonly IReadOnlyList<GitConfigValue> _entries;

    private GitConfigSnapshot(IReadOnlyList<GitConfigValue> entries) => _entries = entries;

    internal IReadOnlyList<GitConfigValue> Entries => _entries;

    internal GitConfigValue? Effective(string key) => Values(key).LastOrDefault();

    internal GitConfigValue? Scoped(string key, GitConfigSource source) =>
        Values(key).LastOrDefault(entry => entry.Source == source);

    internal IReadOnlyList<GitConfigValue> Values(string key) =>
        _entries.Where(entry => string.Equals(NormalizeKey(entry.Key), NormalizeKey(key), StringComparison.Ordinal)).ToArray();

    internal IReadOnlyList<GitConfigValue> ScopedValues(string key, GitConfigSource source) =>
        Values(key).Where(entry => entry.Source == source).ToArray();

    internal static GitConfigSnapshot Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (output.Length == 0) return new GitConfigSnapshot([]);

        var parts = output.Split('\0');
        if (parts[^1].Length != 0 || (parts.Length - 1) % 3 != 0)
            throw new FormatException("Malformed NUL-delimited Git configuration output.");

        var entries = new List<GitConfigValue>((parts.Length - 1) / 3);
        for (var i = 0; i < parts.Length - 1; i += 3)
        {
            var scope = parts[i] switch
            {
                "command" => GitConfigSource.Command,
                "worktree" => GitConfigSource.Worktree,
                "local" => GitConfigSource.Repository,
                "global" => GitConfigSource.Global,
                "system" => GitConfigSource.System,
                _ => GitConfigSource.NotConfigured
            };
            var record = parts[i + 2];
            var separator = record.IndexOf('\n');
            if (separator < 0 && record.Length == 0)
                throw new FormatException("Git returned a configuration record without a key.");
            var key = separator < 0 ? record : record[..separator];
            var value = separator < 0 ? string.Empty : record[(separator + 1)..];
            entries.Add(new GitConfigValue(key, value, scope, parts[i + 1]));
        }
        return new GitConfigSnapshot(entries);
    }

    // Git's section and variable names are case-insensitive, but subsection names are case-sensitive.
    private static string NormalizeKey(string key)
    {
        var first = key.IndexOf('.');
        if (first < 0) return key.ToLowerInvariant();
        var last = key.LastIndexOf('.');
        if (first == last) return key.ToLowerInvariant();
        return string.Concat(key[..first].ToLowerInvariant(), key[first..last], key[last..].ToLowerInvariant());
    }
}
