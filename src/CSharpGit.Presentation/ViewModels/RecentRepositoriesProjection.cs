using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.ViewModels;

internal sealed record RecentRepositoriesProjection(
    IReadOnlyList<RecentRepositorySettings> Pinned,
    IReadOnlyList<RecentRepositorySettings> Recent);

internal static class RecentRepositoriesProjectionBuilder
{
    public static RecentRepositoriesProjection Build(
        IReadOnlyList<RecentRepositorySettings> repositories,
        string? searchText)
    {
        var query = searchText?.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? repositories
            : repositories.Where(repository => Matches(repository, query)).ToArray();

        var pinned = filtered
            .Where(repository => repository.IsPinned)
            .OrderBy(repository => repository.PinnedOrder ?? int.MaxValue)
            .ThenByDescending(repository => repository.LastOpenedUtc)
            .ThenBy(repository => repository.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var recent = filtered
            .Where(repository => !repository.IsPinned)
            .OrderByDescending(repository => repository.LastOpenedUtc)
            .ThenBy(repository => repository.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RecentRepositoriesProjection(pinned, recent);
    }

    public static IReadOnlyList<RecentRepositorySettings> BuildQuickList(
        IReadOnlyList<RecentRepositorySettings> repositories,
        string? currentRepositoryPath,
        int limit)
    {
        if (limit <= 0) return [];

        var projection = Build(repositories, null);
        return projection.Pinned
            .Concat(projection.Recent)
            .Where(repository =>
                currentRepositoryPath is null
                || !PathsEqual(repository.Path, currentRepositoryPath))
            .Take(limit)
            .ToArray();
    }

    internal static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), PathComparison);

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Path.TrimEndingDirectorySeparator(path);
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool Matches(RecentRepositorySettings repository, string query) =>
        repository.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || repository.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
        || repository.LastBranchName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
