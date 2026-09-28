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

    private static bool Matches(RecentRepositorySettings repository, string query) =>
        repository.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || repository.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
        || repository.LastBranchName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
