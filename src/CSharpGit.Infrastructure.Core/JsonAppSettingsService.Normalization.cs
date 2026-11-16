using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Infrastructure;

// Normalization and legacy JSON compatibility remain part of the single
// persisted-settings owner, but do not participate in filesystem transactions.
public sealed partial class JsonAppSettingsService
{
    private static IReadOnlyList<RecentRepositorySettings> ReadRecentRepositories(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Array) return [];
        var entries = new List<RecentRepositorySettings>();
        foreach (var element in json.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            try
            {
                // A malformed new GroupId must not invalidate the rest of a legacy settings document.
                var parsed = element.Deserialize<RecentRepositorySettings>(SerializerOptions);
                if (parsed is not null) entries.Add(parsed);
            }
            catch (JsonException)
            {
                try
                {
                    var node = System.Text.Json.Nodes.JsonNode.Parse(element.GetRawText());
                    if (node is not System.Text.Json.Nodes.JsonObject obj) continue;
                    obj.Remove("GroupId");
                    var parsed = obj.Deserialize<RecentRepositorySettings>(SerializerOptions);
                    if (parsed is not null) entries.Add(parsed);
                }
                catch (JsonException) { }
                catch (InvalidOperationException) { }
            }
        }
        return entries;
    }

    private static IReadOnlyList<RepositoryGroupSettings> NormalizeRepositoryGroups(JsonElement json)
    {
        var candidates = new List<(string? Id, string? Name, int Order, bool Expanded, int Position)>();
        if (json.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in json.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                string? ReadString(string property) =>
                    element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                int ReadOrder() =>
                    element.TryGetProperty("Order", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : -1;
                bool ReadExpanded() =>
                    !element.TryGetProperty("IsExpanded", out var v) || v.ValueKind != JsonValueKind.False;
                candidates.Add((ReadString("Id"), ReadString("Name"), ReadOrder(), ReadExpanded(), candidates.Count));
            }
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var favoritesPosition = candidates.FindIndex(candidate => string.Equals(candidate.Id?.Trim(), FavoritesId, StringComparison.OrdinalIgnoreCase));
        if (favoritesPosition < 0)
            candidates.Add((FavoritesId, "Favorites", 0, true, candidates.Count));

        // Favorites gets first claim on its reserved ID even if it occurs late in the document.
        favoritesPosition = candidates.FindIndex(candidate => string.Equals(candidate.Id?.Trim(), FavoritesId, StringComparison.OrdinalIgnoreCase));
        var favorites = candidates[favoritesPosition];
        var normalized = new List<(RepositoryGroupSettings Group, int Position)>();
        normalized.Add((new RepositoryGroupSettings(FavoritesId, favorites.Name?.Trim() ?? "Favorites", favorites.Order, favorites.Expanded), favorites.Position));
        seenIds.Add(FavoritesId);
        for (var i = 0; i < candidates.Count; i++)
        {
            if (i == favoritesPosition) continue;
            var candidate = candidates[i];
            var id = candidate.Id?.Trim();
            if (string.IsNullOrEmpty(id) || !seenIds.Add(id))
            {
                var attempt = 0;
                do
                {
                    var data = Encoding.UTF8.GetBytes($"{candidate.Position}:{candidate.Id}:{candidate.Name}:{attempt++}");
                    id = Convert.ToHexString(SHA256.HashData(data))[..32].ToLowerInvariant();
                } while (!seenIds.Add(id));
            }
            normalized.Add((new RepositoryGroupSettings(id, candidate.Name?.Trim() ?? "", candidate.Order, candidate.Expanded), candidate.Position));
        }

        // Process favorites first for name conflicts, regardless of its display order.
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var renamed = normalized.Select(pair =>
        {
            var name = pair.Group.Name;
            if (string.IsNullOrWhiteSpace(name)) name = "Group";
            name = string.Concat(name.EnumerateRunes().Take(64).Select(rune => rune.ToString()));
            var baseName = name;
            var suffix = 2;
            while (!seenNames.Add(name))
                name = $"{baseName} ({suffix++})";
            return (Group: pair.Group with { Name = name }, pair.Position);
        });
        return Array.AsReadOnly(renamed
            .OrderBy(pair => pair.Group.Order >= 0 ? 0 : 1)
            .ThenBy(pair => pair.Group.Order >= 0 ? pair.Group.Order : int.MaxValue)
            .ThenBy(pair => pair.Position)
            .Select((pair, index) => pair.Group with { Order = index })
            .ToArray());
    }

    private static string ValidateGroupName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var result = name.Trim();
        if (result.EnumerateRunes().Count() > 64)
            throw new ArgumentException("A group name cannot exceed 64 Unicode code points.", nameof(name));
        return result;
    }

    private static void EnsureUniqueGroupName(
        IReadOnlyList<RepositoryGroupSettings> groups, string name, string? exceptId = null)
    {
        if (groups.Any(group => group.Id != exceptId &&
                string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A group with this name already exists.", nameof(name));
    }

    private static RepositoryGroupSettings FindGroup(SettingsState current, string groupId) =>
        current.RepositoryGroups.FirstOrDefault(group => group.Id == groupId)
        ?? throw new ArgumentException("Unknown repository group.", nameof(groupId));

    private static IReadOnlyList<RecentRepositorySettings> NormalizeRecentRepositories(
        IEnumerable<RecentRepositorySettings>? entries,
        IReadOnlyList<RepositoryGroupSettings>? groups = null)
    {
        if (entries is null) return [];
        groups ??= [new RepositoryGroupSettings(FavoritesId, "Favorites", 0, true)];
        var validIds = groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);

        var normalizedEntries = entries
            .Select(NormalizeRecentRepository)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.LastOpenedUtc)
            .ThenBy(candidate => candidate.Path, PathComparer)
            .ThenByDescending(candidate => candidate.IsPinned)
            .ThenBy(candidate => candidate.PinnedOrder ?? int.MaxValue)
            .ToArray();

        var deduplicated = normalizedEntries
            .GroupBy(candidate => candidate.Path, PathComparer)
            .Select(group => group.First() with
            {
                GroupId = group.First().IsPinned && validIds.Contains(group.First().GroupId ?? "")
                    ? group.First().GroupId : group.First().IsPinned ? FavoritesId : null
            })
            .ToArray();

        var pinned = deduplicated
            .Where(candidate => candidate.IsPinned)
            .GroupBy(candidate => candidate.GroupId!)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(candidate => candidate.PinnedOrder.HasValue ? 0 : 1)
                .ThenBy(candidate => candidate.PinnedOrder ?? int.MaxValue)
                .ThenByDescending(candidate => candidate.LastOpenedUtc)
                .ThenBy(candidate => candidate.Path, PathComparer)
                .Select((candidate, index) => candidate with
                {
                    IsPinned = true,
                    PinnedOrder = index
                }).ToArray());

        var orderedPins = groups.SelectMany(group =>
            pinned.TryGetValue(group.Id, out var repositories) ? repositories : []);

        var unpinned = deduplicated
            .Where(candidate => !candidate.IsPinned)
            .OrderByDescending(candidate => candidate.LastOpenedUtc)
            .ThenBy(candidate => candidate.Path, PathComparer)
            .Take(MaxUnpinnedRecentRepositories)
            .Select(candidate => candidate with
            {
                IsPinned = false,
                GroupId = null,
                PinnedOrder = null
            });

        return Array.AsReadOnly(orderedPins.Concat(unpinned).ToArray());
    }

    private static RecentRepositorySettings? NormalizeRecentRepository(RecentRepositorySettings entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Path)) return null;

        string normalizedPath;
        try
        {
            normalizedPath = NormalizePath(entry.Path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var displayName = string.IsNullOrWhiteSpace(entry.DisplayName)
            ? GetDisplayName(normalizedPath)
            : entry.DisplayName.Trim();
        var isPinned = entry.IsPinned;
        var pinnedOrder = isPinned && entry.PinnedOrder is >= 0
            ? entry.PinnedOrder
            : null;

        return entry with
        {
            Path = normalizedPath,
            DisplayName = displayName,
            LastOpenedUtc = entry.LastOpenedUtc.ToUniversalTime(),
            LastBranchName = string.IsNullOrWhiteSpace(entry.LastBranchName) ? null : entry.LastBranchName.Trim(),
            IsPinned = isPinned,
            GroupId = isPinned ? (string.IsNullOrWhiteSpace(entry.GroupId) ? FavoritesId
                : string.Equals(entry.GroupId.Trim(), FavoritesId, StringComparison.OrdinalIgnoreCase)
                    ? FavoritesId : entry.GroupId.Trim()) : null,
            PinnedOrder = pinnedOrder
        };
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string NormalizeConfiguredRepositoriesDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var trimmed = directory.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
            throw new ArgumentException("The repositories directory must be an absolute path.", nameof(directory));

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            throw new ArgumentException("The repositories directory is not a valid filesystem path.", nameof(directory), exception);
        }
    }

    private static string NormalizePersistedRepositoriesDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return GetDefaultRepositoriesDirectory();

        try
        {
            return NormalizeConfiguredRepositoriesDirectory(directory);
        }
        catch (ArgumentException)
        {
            return GetDefaultRepositoriesDirectory();
        }
    }

    private static string GetDefaultRepositoriesDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(root))
            root = AppContext.BaseDirectory;

        return Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(root, "source", "repos")));
    }

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string GetDisplayName(string path)
    {
        var name = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static string GetDefaultFilePath()
    {
        return Path.Combine(ApplicationDataPaths.Root, "CSharpGit.Presentation", "settings.json");
    }
}
