using CSharpGit.Domain;

namespace CSharpGit.Git;

// --get/--get-all --null use three NUL-terminated fields per value;
// --list --null instead stores key + LF + value in the third field.
internal static class GitConfigGetParser
{
    private const string MalformedOutput = "Malformed NUL-delimited Git config get output.";

    internal readonly record struct Entry(GitConfigSource Scope, string Origin, string Value);

    internal static Entry ParseOne(string output)
    {
        var entries = ParseAll(output);
        if (entries.Count != 1) throw new FormatException(MalformedOutput);
        return entries[0];
    }

    internal static IReadOnlyList<Entry> ParseAll(string output)
    {
        if (string.IsNullOrEmpty(output)) throw new FormatException(MalformedOutput);

        var entries = new List<Entry>();
        var offset = 0;
        while (offset < output.Length)
        {
            var scope = ReadToken(output, ref offset) switch
            {
                "command" => GitConfigSource.Command,
                "worktree" => GitConfigSource.Worktree,
                "local" => GitConfigSource.Repository,
                "global" => GitConfigSource.Global,
                "system" => GitConfigSource.System,
                _ => throw new FormatException(MalformedOutput)
            };
            var origin = ReadToken(output, ref offset);
            var value = ReadToken(output, ref offset);
            if (origin.Length == 0) throw new FormatException(MalformedOutput);
            entries.Add(new Entry(scope, origin, value));
        }
        return entries;
    }

    private static string ReadToken(string output, ref int offset)
    {
        var terminator = output.IndexOf('\0', offset);
        if (terminator < 0) throw new FormatException(MalformedOutput);
        var token = output[offset..terminator];
        offset = terminator + 1;
        return token;
    }
}
