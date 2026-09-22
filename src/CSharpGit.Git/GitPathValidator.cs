using CSharpGit.Domain;

namespace CSharpGit.Git;

internal static class GitPathValidator
{
    internal static void ValidateRepositoryRelative(string path, string parameterName = "path")
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            throw new ArgumentException("Invalid file path.", parameterName);
    }

    internal static void ValidateChange(WorkingTreeChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        ValidateRepositoryRelative(change.Path);
        if (change.OriginalPath is not null)
            ValidateRepositoryRelative(change.OriginalPath);
    }

    internal static string ResolveSafeWorkingTreePath(
        Repository repository,
        string path)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateRepositoryRelative(path);

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
