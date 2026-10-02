using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation;

internal enum WorkingTreeInvalidationReason
{
    None,
    StatusChanged,
    GitVisiblePathInvalidated
}

internal static class WorkingTreeInvalidationEvaluator
{
    public static WorkingTreeInvalidationReason Evaluate(
        WorkingTreeStatusSnapshot displayed,
        WorkingTreeStatusSnapshot current,
        RepositoryInvalidationBatch invalidation)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(invalidation);

        if (!displayed.Equals(current))
            return WorkingTreeInvalidationReason.StatusChanged;

        if (!invalidation.HasWorkingTreeChanges)
            return WorkingTreeInvalidationReason.None;

        foreach (var path in invalidation.WorkingTreePaths)
        {
            var invalidatedPath = Normalize(path);
            foreach (var entry in current.Entries)
            {
                if (Intersects(invalidatedPath, Normalize(entry.Path), entry.IsSubmodule)
                    || entry.OriginalPath is { } original
                    && Intersects(invalidatedPath, Normalize(original), entry.IsSubmodule))
                    return WorkingTreeInvalidationReason.GitVisiblePathInvalidated;
            }
        }

        return WorkingTreeInvalidationReason.None;
    }

    private static bool Intersects(
        string invalidatedPath,
        string gitVisiblePath,
        bool isSubmodule)
    {
        if (invalidatedPath.Length == 0) return true;
        if (RepositoryChangeMonitor.PathComparer.Equals(invalidatedPath, gitVisiblePath))
            return true;

        if (gitVisiblePath.StartsWith(
                invalidatedPath + "/",
                PathComparison))
            return true;

        return isSubmodule
               && invalidatedPath.StartsWith(
                   gitVisiblePath + "/",
                   PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string Normalize(string path)
    {
        var result = path.Replace('\\', '/').Trim('/');
        return result == "." ? string.Empty : result;
    }
}
