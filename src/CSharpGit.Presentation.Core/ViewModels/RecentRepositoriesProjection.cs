using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.ViewModels;

internal sealed record RepositoryGroupProjection(
    RepositoryGroupSettings Settings,
    IReadOnlyList<RecentRepositorySettings> Repositories);

internal sealed record RecentRepositoriesProjection(
    IReadOnlyList<RepositoryGroupProjection> Groups,
    IReadOnlyList<RecentRepositorySettings> Recent)
{
    // Compatibility for consumers that only need the flattened set of pinned repositories.
    public IReadOnlyList<RecentRepositorySettings> Pinned =>
        Groups.SelectMany(group => group.Repositories).ToArray();
}

internal static class RecentRepositoriesProjectionBuilder
{
    public static RecentRepositoriesProjection Build(
        IReadOnlyList<RecentRepositorySettings> repositories,
        string? searchText) =>
        Build([new RepositoryGroupSettings("favorites", "Favorites", 0, true)], repositories, searchText);

    public static RecentRepositoriesProjection Build(
        IReadOnlyList<RepositoryGroupSettings> groups,
        IReadOnlyList<RecentRepositorySettings> repositories,
        string? searchText)
    {
        var query = searchText?.Trim();
        var searching = !string.IsNullOrWhiteSpace(query);
        var filtered = searching
            ? repositories.Where(repository => Matches(repository, query!)).ToArray()
            : repositories.ToArray();

        var pinned = groups.OrderBy(group => group.Order)
            .Select(group => new RepositoryGroupProjection(group, filtered
                .Where(repository => repository.IsPinned
                    && string.Equals(repository.GroupId ?? "favorites", group.Id, StringComparison.Ordinal))
                .OrderBy(repository => repository.PinnedOrder ?? int.MaxValue)
                .ThenByDescending(repository => repository.LastOpenedUtc)
                .ThenBy(repository => repository.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray()))
            .Where(group => !searching || group.Repositories.Count > 0)
            .ToArray();

        var recent = filtered
            .Where(repository => !repository.IsPinned)
            .OrderByDescending(repository => repository.LastOpenedUtc)
            .ThenBy(repository => repository.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new RecentRepositoriesProjection(pinned, recent);
    }

    internal static RecentRepositorySettings? FindSingleSearchMatch(
        IReadOnlyList<RepositoryGroupSettings> groups,
        IReadOnlyList<RecentRepositorySettings> repositories,
        string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return null;

        var projection = Build(groups, repositories, searchText);
        var matches = projection.Groups.SelectMany(group => group.Repositories)
            .Concat(projection.Recent)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static IReadOnlyList<RecentRepositorySettings> BuildQuickList(
        IReadOnlyList<RecentRepositorySettings> repositories,
        string? currentRepositoryPath,
        int limit) =>
        BuildQuickList([new RepositoryGroupSettings("favorites", "Favorites", 0, true)],
            repositories, currentRepositoryPath, limit);

    public static IReadOnlyList<RecentRepositorySettings> BuildQuickList(
        IReadOnlyList<RepositoryGroupSettings> groups,
        IReadOnlyList<RecentRepositorySettings> repositories,
        string? currentRepositoryPath,
        int limit)
    {
        if (limit <= 0) return [];
        var projection = Build(groups, repositories, null);
        return projection.Pinned
            .Concat(projection.Recent)
            .Where(repository => currentRepositoryPath is null
                || !PathsEqual(repository.Path, currentRepositoryPath))
            .Take(limit)
            .ToArray();
    }

    internal static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), PathComparison);

    private static string NormalizePath(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        { return Path.TrimEndingDirectorySeparator(path); }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool Matches(RecentRepositorySettings repository, string query) =>
        repository.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || repository.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
        || repository.LastBranchName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
