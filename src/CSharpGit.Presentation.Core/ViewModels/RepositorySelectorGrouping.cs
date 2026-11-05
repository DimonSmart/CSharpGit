namespace CSharpGit.Presentation.ViewModels;

// Repository indexes keep the original entries (including the current repository)
// associated with their full paths and branch metadata after presentation grouping.
internal sealed record RepositorySelectorMenuRow(int? RepositoryIndex, string Path, string DisplayPath);

internal static class RepositorySelectorGrouping
{
    private const int MinimumGroupSize = 4;

    public static IReadOnlyList<RepositorySelectorMenuRow> Build(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var candidates = new Dictionary<string, List<int>>(comparer);
        var fullPaths = new string?[paths.Count];

        for (var index = 0; index < paths.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(paths[index]) || !Path.IsPathFullyQualified(paths[index]))
                continue;

            try
            {
                var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths[index]));
                fullPaths[index] = fullPath;
                var root = Path.GetPathRoot(fullPath);
                for (string? directory = fullPath;
                     directory is not null && !comparer.Equals(directory, root);
                     directory = Path.GetDirectoryName(directory))
                {
                    if (!candidates.TryGetValue(directory, out var indices))
                        candidates.Add(directory, indices = []);
                    indices.Add(index);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // An invalid saved path stays selectable but is not used for grouping.
            }
        }

        // Assign the most specific groups first. Only unassigned paths count toward
        // a parent, so a parent cannot become a one-item group after its child groups.
        var groups = new string?[paths.Count];
        foreach (var candidate in candidates
                     .OrderByDescending(candidate => candidate.Key.Length)
                     .ThenBy(candidate => candidate.Key, comparer))
        {
            var unassigned = candidate.Value.Where(index => groups[index] is null).ToArray();
            if (unassigned.Length < MinimumGroupSize) continue;
            foreach (var index in unassigned)
                groups[index] = candidate.Key;
        }

        var rows = new List<RepositorySelectorMenuRow>();
        var emitted = new HashSet<string>(comparer);
        for (var index = 0; index < paths.Count; index++)
        {
            if (groups[index] is not { } group)
            {
                rows.Add(new RepositorySelectorMenuRow(index, paths[index], paths[index]));
                continue;
            }

            if (!emitted.Add(group)) continue;
            rows.Add(new RepositorySelectorMenuRow(null, group, group));
            for (var member = 0; member < paths.Count; member++)
            {
                if (!comparer.Equals(groups[member], group)) continue;
                var relative = Path.GetRelativePath(group, fullPaths[member]!);
                rows.Add(new RepositorySelectorMenuRow(member, paths[member],
                    relative == "." ? "root" : relative));
            }
        }

        return rows;
    }
}
