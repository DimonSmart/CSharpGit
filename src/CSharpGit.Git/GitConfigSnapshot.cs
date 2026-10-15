using CSharpGit.Domain;

namespace CSharpGit.Git;

/// <summary>
/// A per-operation, ordered view of Git's own config resolution. Never cached across operations.
/// The NUL protocol is: scope NUL origin NUL key LF value NUL for every entry.
/// </summary>
internal sealed class GitConfigSnapshot
{
    private readonly IReadOnlyList<GitConfigValue> _entries;
    private readonly IReadOnlyDictionary<string, GitConfigValue[]> _byKey;

    private GitConfigSnapshot(IReadOnlyList<GitConfigValue> entries)
    {
        _entries = entries;
        _byKey = entries.GroupBy(entry => NormalizeKey(entry.Key), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
    }

    internal IReadOnlyList<GitConfigValue> Entries => _entries;

    internal GitConfigValue? Effective(string key) => Values(key).LastOrDefault();

    internal GitConfigValue? Scoped(string key, GitConfigSource source) =>
        Values(key).LastOrDefault(entry => entry.Source == source);

    internal IReadOnlyList<GitConfigValue> Values(string key) =>
        _byKey.TryGetValue(NormalizeKey(key), out var values) ? values : [];

    internal IReadOnlyList<GitConfigValue> ScopedValues(string key, GitConfigSource source) =>
        Values(key).Where(entry => entry.Source == source).ToArray();

    internal static GitConfigSnapshot Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (output.Length == 0) return new GitConfigSnapshot([]);

        var entries = new List<GitConfigValue>();
        var offset = 0;
        while (offset < output.Length)
        {
            var scopeToken = ReadToken(output, ref offset);
            var origin = ReadToken(output, ref offset);
            var record = ReadToken(output, ref offset);
            var scope = scopeToken switch
            {
                "command" => GitConfigSource.Command,
                "worktree" => GitConfigSource.Worktree,
                "local" => GitConfigSource.Repository,
                "global" => GitConfigSource.Global,
                "system" => GitConfigSource.System,
                _ => GitConfigSource.NotConfigured
            };
            var separator = record.IndexOf('\n');
            if (separator < 0 && record.Length == 0)
                throw new FormatException("Git returned a configuration record without a key.");
            var key = separator < 0 ? record : record[..separator];
            var value = separator < 0 ? string.Empty : record[(separator + 1)..];
            entries.Add(new GitConfigValue(key, value, scope, origin));
        }
        return new GitConfigSnapshot(entries);
    }

    // Git configuration can contain many repeated keys and embedded newlines.
    // Read only the three NUL-delimited fields for each record, without allocating
    // an array of all fields before parsing.
    private static string ReadToken(string output, ref int offset)
    {
        var next = output.IndexOf('\0', offset);
        if (next < 0)
            throw new FormatException("Malformed NUL-delimited Git configuration output.");
        var token = output[offset..next];
        offset = next + 1;
        return token;
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
