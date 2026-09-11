using CSharpGit.Domain;

namespace CSharpGit.Git;

internal static class GitDiffParser
{
    public static bool IsBinary(string output) =>
        output.Contains("Binary files ", StringComparison.Ordinal) ||
        output.Contains("GIT binary patch", StringComparison.Ordinal);

    public static IReadOnlyList<DiffLine> ParseLines(string output) => output.Split('\n')
        .Select(line => new DiffLine(
            line.TrimEnd('\r'),
            line.StartsWith("+++") || line.StartsWith("---") || line.StartsWith("@@") || line.StartsWith("diff ") || line.StartsWith("index ")
                ? DiffLineKind.Header
                : line.StartsWith('+')
                    ? DiffLineKind.Added
                    : line.StartsWith('-')
                        ? DiffLineKind.Removed
                        : DiffLineKind.Context))
        .ToList();
}
