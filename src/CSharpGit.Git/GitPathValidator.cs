using CSharpGit.Domain;

namespace CSharpGit.Git;

internal static class GitPathValidator
{
    internal static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            throw new ArgumentException("Invalid file path.", nameof(path));
    }

    internal static void ValidateChange(WorkingTreeChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        ValidatePath(change.Path);
        if (change.OriginalPath is not null)
            ValidatePath(change.OriginalPath);
    }

    internal static string[] PathArguments(
        string command,
        WorkingTreeChange change,
        params string[] options) =>
        [command, .. options, "--", change.Path, .. (change.OriginalPath is null ? Array.Empty<string>() : [change.OriginalPath])];

    internal static string ResolveSafeWorkingTreePath(
        Repository repository,
        string path)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidatePath(path);
        var root = Path.GetFullPath(repository.WorkingDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootedPrefix =
            Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootedPrefix, comparison))
            throw new ArgumentException(
                "The file path is outside the repository.",
                nameof(path));
        return fullPath;
    }
}
