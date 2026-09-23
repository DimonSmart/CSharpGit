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

        var subcommandIndex = FindSubcommandIndex(arguments);
        if (subcommandIndex < 0 || !SupportedSubcommands.Contains(arguments[subcommandIndex]))
            return arguments;

        var result = new List<string>(arguments.Count + 1);
        for (var index = 0; index < arguments.Count; index++)
        {
            result.Add(arguments[index]);
            if (index == subcommandIndex)
                result.Add("--progress");
        }
        return result;
    }

    private static int FindSubcommandIndex(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (argument is "-c" or "-C" or "--git-dir" or "--work-tree" or "--namespace" or "--config-env")
            {
                index++;
                continue;
            }

            if (argument.StartsWith("--git-dir=", StringComparison.Ordinal)
                || argument.StartsWith("--work-tree=", StringComparison.Ordinal)
                || argument.StartsWith("--namespace=", StringComparison.Ordinal)
                || argument.StartsWith("--config-env=", StringComparison.Ordinal)
                || argument.StartsWith("-", StringComparison.Ordinal))
                continue;

            return index;
        }

        return -1;
    }
}
