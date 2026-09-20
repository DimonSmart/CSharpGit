using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git;

internal static class GitProgressPolicy
{
    private static readonly HashSet<string> SupportedSubcommands = new(StringComparer.Ordinal)
    {
        "fetch",
        "pull",
        "push",
        "clone",
        "checkout"
    };

    private static readonly HashSet<string> ProgressOverrides = new(StringComparer.Ordinal)
    {
        "--progress",
        "--no-progress",
        "--quiet",
        "-q"
    };

    public static IReadOnlyList<string> Apply(IReadOnlyList<string> arguments, GitCommandKind commandKind)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (commandKind != GitCommandKind.User || arguments.Count == 0)
            return arguments;
        if (arguments.Any(ProgressOverrides.Contains))
            return arguments;

        var subcommandIndex = -1;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!SupportedSubcommands.Contains(arguments[index])) continue;
            subcommandIndex = index;
            break;
        }

        if (subcommandIndex < 0) return arguments;

        var result = new List<string>(arguments.Count + 1);
        for (var index = 0; index < arguments.Count; index++)
        {
            result.Add(arguments[index]);
            if (index == subcommandIndex)
                result.Add("--progress");
        }
        return result;
    }
}
