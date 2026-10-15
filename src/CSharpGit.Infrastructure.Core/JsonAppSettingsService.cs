using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Infrastructure;

public sealed class JsonAppSettingsService : IAppSettingsService
{
    private const int MaxUnpinnedRecentRepositories = 1000;
    private const string FavoritesId = "favorites";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new TolerantApplicationThemeModeConverter(),
            new TolerantPullStrategyConverter(),
            new JsonStringEnumConverter()
        }
    };
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly string _filePath;
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private SettingsState _state;

    public JsonAppSettingsService()
        : this(GetDefaultFilePath())
    {
    }

    internal JsonAppSettingsService(string filePath)
    {
        _filePath = filePath;
        _state = LoadSettings();
    }

    public ApplicationThemeMode ThemeMode => Volatile.Read(ref _state).ThemeMode;

    public CommitTimeDisplayMode CommitTimeDisplayMode => Volatile.Read(ref _state).CommitTimeDisplayMode;

    public bool LoggingEnabled => Volatile.Read(ref _state).LoggingEnabled;

    public ApplicationLogLevel LogLevel => Volatile.Read(ref _state).LogLevel;

    public GitConsoleAutoOpenMode GitConsoleAutoOpenMode => Volatile.Read(ref _state).GitConsoleAutoOpenMode;

    public bool ShowReflog => Volatile.Read(ref _state).ShowReflog;

    public bool AutoSetupRemoteOnPush => Volatile.Read(ref _state).AutoSetupRemoteOnPush;

    public PullStrategy DefaultPullStrategy => Volatile.Read(ref _state).DefaultPullStrategy;

    public bool ForcePullAutoStash => Volatile.Read(ref _state).ForcePullAutoStash;

    public bool ShowAuthorAvatars => Volatile.Read(ref _state).ShowAuthorAvatars;

    public bool OnlineAvatarLookupEnabled => Volatile.Read(ref _state).OnlineAvatarLookupEnabled;

    public bool HistoryPerformanceDiagnosticsEnabled => Volatile.Read(ref _state).HistoryPerformanceDiagnosticsEnabled;

    public HistoryRenderingMode HistoryRenderingMode => Volatile.Read(ref _state).HistoryRenderingMode;

    public string DefaultRepositoriesDirectory => Volatile.Read(ref _state).DefaultRepositoriesDirectory;

    public IReadOnlyList<RecentRepositorySettings> RecentRepositories => Volatile.Read(ref _state).RecentRepositories;

    public IReadOnlyList<RepositoryGroupSettings> RepositoryGroups => Volatile.Read(ref _state).RepositoryGroups;
    public bool RecentRepositoriesExpanded => Volatile.Read(ref _state).RecentRepositoriesExpanded;

    public Task SetRecentRepositoriesExpandedAsync(bool expanded, CancellationToken cancellationToken = default) =>
        UpdateAsync(current => current.RecentRepositoriesExpanded == expanded
            ? current : current with { RecentRepositoriesExpanded = expanded }, cancellationToken);

    public RepositoryLauncherSettingsSnapshot RepositoryLauncherSnapshot
    {
        get
        {
            var state = Volatile.Read(ref _state);
            return new RepositoryLauncherSettingsSnapshot(state.RepositoryGroups, state.RecentRepositories);
        }
    }

    public event EventHandler? Changed;

    public Task SetThemeModeAsync(
        ApplicationThemeMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(ApplicationThemeMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        return UpdateAsync(
            current => current.ThemeMode == mode ? current : current with { ThemeMode = mode },
            cancellationToken);
    }

    public Task SetCommitTimeDisplayModeAsync(
        CommitTimeDisplayMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(CommitTimeDisplayMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        return UpdateAsync(
            current => current.CommitTimeDisplayMode == mode
                ? current
                : current with { CommitTimeDisplayMode = mode },
            cancellationToken);
    }

    public Task SetLoggingSettingsAsync(
        bool enabled,
        ApplicationLogLevel level,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(ApplicationLogLevel), level))
            throw new ArgumentOutOfRangeException(nameof(level));

        return UpdateAsync(
            current => current.LoggingEnabled == enabled && current.LogLevel == level
                ? current
                : current with { LoggingEnabled = enabled, LogLevel = level },
            cancellationToken);
    }

    public Task SetGitConsoleAutoOpenModeAsync(
        GitConsoleAutoOpenMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(GitConsoleAutoOpenMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        return UpdateAsync(
            current => current.GitConsoleAutoOpenMode == mode
                ? current
                : current with { GitConsoleAutoOpenMode = mode },
            cancellationToken);
    }

    public Task SetShowReflogAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            current => current.ShowReflog == value
                ? current
                : current with { ShowReflog = value },
            cancellationToken);

    public Task SetAutoSetupRemoteOnPushAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            current => current.AutoSetupRemoteOnPush == value
                ? current
                : current with { AutoSetupRemoteOnPush = value },
            cancellationToken);

    public Task SetDefaultPullStrategyAsync(
        PullStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(strategy))
            throw new ArgumentOutOfRangeException(nameof(strategy));
        return UpdateAsync(
            current => current.DefaultPullStrategy == strategy
                ? current
                : current with { DefaultPullStrategy = strategy },
            cancellationToken);
    }

    public Task SetForcePullAutoStashAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            current => current.ForcePullAutoStash == value
                ? current
                : current with { ForcePullAutoStash = value },
            cancellationToken);

    public Task SetShowAuthorAvatarsAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            current => current.ShowAuthorAvatars == value
                ? current
                : current with { ShowAuthorAvatars = value },
            cancellationToken);

    public Task SetOnlineAvatarLookupEnabledAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            current => current.OnlineAvatarLookupEnabled == value
                ? current
                : current with { OnlineAvatarLookupEnabled = value },
            cancellationToken);

    public Task SetHistoryPerformanceDiagnosticsEnabledAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            current => current.HistoryPerformanceDiagnosticsEnabled == value
                ? current
                : current with { HistoryPerformanceDiagnosticsEnabled = value },
            cancellationToken);

    public Task SetHistoryRenderingModeAsync(
        HistoryRenderingMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(HistoryRenderingMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        return UpdateAsync(
            current => current.HistoryRenderingMode == mode
                ? current
                : current with { HistoryRenderingMode = mode },
            cancellationToken);
    }

    public Task SetDefaultRepositoriesDirectoryAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeConfiguredRepositoriesDirectory(directory);
        return UpdateAsync(
            current => string.Equals(
                current.DefaultRepositoriesDirectory,
                normalized,
                PathComparison)
                ? current
                : current with { DefaultRepositoriesDirectory = normalized },
            cancellationToken);
    }

    public Task RecordRecentRepositoryAsync(
        string path,
        string displayName,
        string? lastBranchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var normalizedPath = NormalizePath(path);
        var normalizedDisplayName = displayName.Trim();
        var normalizedBranchName = string.IsNullOrWhiteSpace(lastBranchName) ? null : lastBranchName.Trim();

        return UpdateAsync(
            current =>
            {
                var existing = current.RecentRepositories.FirstOrDefault(
                    candidate => PathComparer.Equals(candidate.Path, normalizedPath));
                var entry = new RecentRepositorySettings(
                    normalizedPath,
                    normalizedDisplayName,
                    DateTimeOffset.UtcNow,
                    normalizedBranchName,
                    existing?.IsPinned ?? false,
                    existing?.PinnedOrder,
                    existing?.GroupId);
                var updated = current.RecentRepositories
                    .Where(candidate => !PathComparer.Equals(candidate.Path, normalizedPath))
                    .Append(entry);
                return current with { RecentRepositories = NormalizeRecentRepositories(updated, current.RepositoryGroups) };
            },
            cancellationToken);
    }

    public Task UpdateRecentRepositoryBranchAsync(
        string path,
        string? lastBranchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = NormalizePath(path);
        var normalizedBranchName = string.IsNullOrWhiteSpace(lastBranchName)
            ? null
            : lastBranchName.Trim();

        return UpdateAsync(
            current =>
            {
                var existing = current.RecentRepositories.FirstOrDefault(
                    candidate => PathComparer.Equals(candidate.Path, normalizedPath));
                if (existing is null
                    || string.Equals(
                        existing.LastBranchName,
                        normalizedBranchName,
                        StringComparison.Ordinal))
                    return current;

                var updated = current.RecentRepositories
                    .Select(candidate => PathComparer.Equals(candidate.Path, normalizedPath)
                        ? candidate with { LastBranchName = normalizedBranchName }
                        : candidate)
                    .ToArray();
                return current with { RecentRepositories = updated };
            },
            cancellationToken);
    }


    public async Task<RepositoryGroupSettings> CreateRepositoryGroupAsync(
        string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = ValidateGroupName(name);
        var created = new RepositoryGroupSettings(Guid.NewGuid().ToString("N"), normalizedName, 0, true);
        await UpdateAsync(current =>
        {
            EnsureUniqueGroupName(current.RepositoryGroups, normalizedName);
            created = created with { Order = current.RepositoryGroups.Count };
            return current with
            {
                RepositoryGroups = Array.AsReadOnly(current.RepositoryGroups.Append(created).ToArray())
            };
        }, cancellationToken).ConfigureAwait(false);
        return created;
    }

    public Task RenameRepositoryGroupAsync(
        string groupId, string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = ValidateGroupName(name);
        return UpdateAsync(current =>
        {
            var group = FindGroup(current, groupId);
            if (group.Name == normalizedName) return current;
            EnsureUniqueGroupName(current.RepositoryGroups, normalizedName, group.Id);
            return current with
            {
                RepositoryGroups = Array.AsReadOnly(current.RepositoryGroups
                    .Select(candidate => candidate.Id == group.Id ? candidate with { Name = normalizedName } : candidate)
                    .ToArray())
            };
        }, cancellationToken);
    }

    public Task DeleteRepositoryGroupAsync(string groupId, CancellationToken cancellationToken = default) =>
        UpdateAsync(current =>
        {
            var group = FindGroup(current, groupId);
            if (group.Id == FavoritesId)
                throw new InvalidOperationException("Favorites cannot be deleted.");

            var groups = current.RepositoryGroups
                .Where(candidate => candidate.Id != group.Id)
                .Select((candidate, index) => candidate with { Order = index })
                .ToArray();
            var appendOffset = current.RecentRepositories.Count(candidate => candidate.IsPinned && candidate.GroupId == FavoritesId);
            var moved = current.RecentRepositories.Select(candidate =>
                candidate.IsPinned && candidate.GroupId == group.Id
                    ? candidate with { GroupId = FavoritesId, PinnedOrder = appendOffset + candidate.PinnedOrder.GetValueOrDefault() }
                    : candidate);
            return current with
            {
                RepositoryGroups = Array.AsReadOnly(groups),
                RecentRepositories = NormalizeRecentRepositories(moved, groups)
            };
        }, cancellationToken);

    public Task MoveRepositoryGroupAsync(string groupId, int newIndex, CancellationToken cancellationToken = default) =>
        UpdateAsync(current =>
        {
            var group = FindGroup(current, groupId);
            if (newIndex < 0 || newIndex >= current.RepositoryGroups.Count)
                throw new ArgumentOutOfRangeException(nameof(newIndex));
            if (group.Order == newIndex) return current;
            var groups = current.RepositoryGroups.ToList();
            groups.RemoveAt(group.Order);
            groups.Insert(newIndex, group);
            return current with
            {
                RepositoryGroups = Array.AsReadOnly(groups
                    .Select((candidate, index) => candidate with { Order = index }).ToArray())
            };
        }, cancellationToken);

    public Task SetRepositoryGroupExpandedAsync(
        string groupId, bool expanded, CancellationToken cancellationToken = default) =>
        UpdateAsync(current =>
        {
            var group = FindGroup(current, groupId);
            if (group.IsExpanded == expanded) return current;
            return current with
            {
                RepositoryGroups = Array.AsReadOnly(current.RepositoryGroups
                    .Select(candidate => candidate.Id == group.Id ? candidate with { IsExpanded = expanded } : candidate)
                    .ToArray())
            };
        }, cancellationToken);

    public Task MoveRecentRepositoryToGroupAsync(
        string path, string groupId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedPath = NormalizePath(path);
        return UpdateAsync(current =>
        {
            var group = FindGroup(current, groupId);
            var existing = current.RecentRepositories.FirstOrDefault(candidate => PathComparer.Equals(candidate.Path, normalizedPath));
            if (existing is null || existing.IsPinned && existing.GroupId == group.Id) return current;

            var appendIndex = current.RecentRepositories.Count(candidate => candidate.IsPinned && candidate.GroupId == group.Id);
            return current with
            {
                RecentRepositories = NormalizeRecentRepositories(current.RecentRepositories.Select(candidate =>
                    PathComparer.Equals(candidate.Path, normalizedPath)
                        ? candidate with { IsPinned = true, GroupId = group.Id, PinnedOrder = appendIndex }
                        : candidate), current.RepositoryGroups)
            };
        }, cancellationToken);
    }

    public Task SetRecentRepositoryPinnedAsync(
        string path, bool pinned, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedPath = NormalizePath(path);
        return UpdateAsync(current =>
        {
            var existing = current.RecentRepositories.FirstOrDefault(
                candidate => PathComparer.Equals(candidate.Path, normalizedPath));
            if (existing is null || existing.IsPinned == pinned) return current;
            var appendIndex = current.RecentRepositories.Count(
                candidate => candidate.IsPinned && candidate.GroupId == FavoritesId);
            var updated = current.RecentRepositories.Select(candidate =>
                PathComparer.Equals(candidate.Path, normalizedPath)
                    ? candidate with
                    {
                        IsPinned = pinned,
                        GroupId = pinned ? FavoritesId : null,
                        PinnedOrder = pinned ? appendIndex : null
                    }
                    : candidate);
            return current with { RecentRepositories = NormalizeRecentRepositories(updated, current.RepositoryGroups) };
        }, cancellationToken);
    }

    public Task RemoveRecentRepositoryAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedPath = NormalizePath(path);

        return UpdateAsync(
            current =>
            {
                var updated = current.RecentRepositories
                    .Where(candidate => !PathComparer.Equals(candidate.Path, normalizedPath))
                    .ToArray();
                return updated.Length == current.RecentRepositories.Count
                    ? current
                    : current with { RecentRepositories = NormalizeRecentRepositories(updated, current.RepositoryGroups) };
            },
            cancellationToken);
    }

    public Task MovePinnedRepositoryAsync(
        string path,
        int newIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedPath = NormalizePath(path);
        return UpdateAsync(
            current =>
            {
                var existing = current.RecentRepositories.FirstOrDefault(
                    candidate => PathComparer.Equals(candidate.Path, normalizedPath));
                if (existing is not { IsPinned: true }) return current;
                var pinned = current.RecentRepositories
                    .Where(candidate => candidate.IsPinned && candidate.GroupId == existing.GroupId)
                    .OrderBy(candidate => candidate.PinnedOrder ?? int.MaxValue)
                    .ToList();
                var currentIndex = pinned.FindIndex(
                    candidate => PathComparer.Equals(candidate.Path, normalizedPath));
                if (currentIndex < 0) return current;
                if (newIndex < 0 || newIndex >= pinned.Count)
                    throw new ArgumentOutOfRangeException(nameof(newIndex));
                if (currentIndex == newIndex) return current;

                var moving = pinned[currentIndex];
                pinned.RemoveAt(currentIndex);
                pinned.Insert(newIndex, moving);

                var reordered = pinned
                    .Select((candidate, index) => candidate with { PinnedOrder = index })
                    .ToDictionary(candidate => candidate.Path, PathComparer);
                var updated = current.RecentRepositories.Select(
                    candidate => candidate.IsPinned && candidate.GroupId == existing.GroupId
                        ? reordered[candidate.Path] : candidate);
                return current with { RecentRepositories = NormalizeRecentRepositories(updated, current.RepositoryGroups) };
            },
            cancellationToken);
    }

    private async Task UpdateAsync(
        Func<SettingsState, SettingsState> createNext,
        CancellationToken cancellationToken)
    {
        var changed = false;
        await _updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Volatile.Read(ref _state);
            var next = createNext(current);
            if (ReferenceEquals(current, next)) return;

            await PersistAsync(next, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _state, next);
            changed = true;
        }
        finally
        {
            _updateGate.Release();
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private SettingsState LoadSettings()
    {
        try
        {
            if (!File.Exists(_filePath)) return SettingsState.Default;
            var document = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(_filePath), SerializerOptions);
            if (document is null) return SettingsState.Default;

            var themeMode = Enum.IsDefined(typeof(ApplicationThemeMode), document.ThemeMode)
                ? document.ThemeMode
                : ApplicationThemeMode.System;
            var commitTimeDisplayMode = Enum.IsDefined(typeof(CommitTimeDisplayMode), document.CommitTimeDisplayMode)
                ? document.CommitTimeDisplayMode
                : CommitTimeDisplayMode.Smart;
            var logLevel = document.LogLevel is { } configuredLevel
                           && Enum.IsDefined(typeof(ApplicationLogLevel), configuredLevel)
                ? configuredLevel
                : ApplicationLogLevel.Information;
            var gitConsoleAutoOpenMode = document.GitConsoleAutoOpenMode is { } configuredGitConsoleMode
                                         && Enum.IsDefined(typeof(GitConsoleAutoOpenMode), configuredGitConsoleMode)
                ? configuredGitConsoleMode
                : GitConsoleAutoOpenMode.OnErrors;
            var historyRenderingMode = document.HistoryRenderingMode is { } configuredHistoryRenderingMode
                                       && Enum.IsDefined(typeof(HistoryRenderingMode), configuredHistoryRenderingMode)
                ? configuredHistoryRenderingMode
                : document.HistorySimplifiedRenderingEnabled == true
                    ? HistoryRenderingMode.SubjectOnly
                    : HistoryRenderingMode.Full;
            var defaultRepositoriesDirectory = NormalizePersistedRepositoriesDirectory(
                document.DefaultRepositoriesDirectory);
            var repositoryGroups = NormalizeRepositoryGroups(document.RepositoryGroups);
            var recentRepositories = NormalizeRecentRepositories(ReadRecentRepositories(document.RecentRepositories), repositoryGroups);

            return new SettingsState(
                themeMode,
                commitTimeDisplayMode,
                document.LoggingEnabled,
                logLevel,
                gitConsoleAutoOpenMode,
                document.ShowReflog,
                document.AutoSetupRemoteOnPush,
                document.DefaultPullStrategy,
                document.ForcePullAutoStash,
                document.ShowAuthorAvatars ?? true,
                document.OnlineAvatarLookupEnabled ?? true,
                document.HistoryPerformanceDiagnosticsEnabled,
                historyRenderingMode,
                defaultRepositoriesDirectory,
                recentRepositories,
                repositoryGroups,
                document.RecentRepositoriesExpanded);
        }
        catch (JsonException)
        {
            return SettingsState.Default;
        }
        catch (IOException)
        {
            return SettingsState.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return SettingsState.Default;
        }
    }

    private async Task PersistAsync(
        SettingsState state,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var document = new SettingsDocument
        {
            ThemeMode = state.ThemeMode,
            CommitTimeDisplayMode = state.CommitTimeDisplayMode,
            LoggingEnabled = state.LoggingEnabled,
            LogLevel = state.LogLevel,
            GitConsoleAutoOpenMode = state.GitConsoleAutoOpenMode,
            ShowReflog = state.ShowReflog,
            AutoSetupRemoteOnPush = state.AutoSetupRemoteOnPush,
            DefaultPullStrategy = state.DefaultPullStrategy,
            ForcePullAutoStash = state.ForcePullAutoStash,
            ShowAuthorAvatars = state.ShowAuthorAvatars,
            OnlineAvatarLookupEnabled = state.OnlineAvatarLookupEnabled,
            HistoryPerformanceDiagnosticsEnabled = state.HistoryPerformanceDiagnosticsEnabled,
            HistoryRenderingMode = state.HistoryRenderingMode,
            DefaultRepositoriesDirectory = state.DefaultRepositoriesDirectory,
            RecentRepositories = JsonSerializer.SerializeToElement(state.RecentRepositories, SerializerOptions),
            RepositoryGroups = JsonSerializer.SerializeToElement(state.RepositoryGroups, SerializerOptions),
            RecentRepositoriesExpanded = state.RecentRepositoriesExpanded
        };
        var json = JsonSerializer.Serialize(document, SerializerOptions);
        var temporaryPath = _filePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }


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

    private sealed record SettingsState(
        ApplicationThemeMode ThemeMode,
        CommitTimeDisplayMode CommitTimeDisplayMode,
        bool LoggingEnabled,
        ApplicationLogLevel LogLevel,
        GitConsoleAutoOpenMode GitConsoleAutoOpenMode,
        bool ShowReflog,
        bool AutoSetupRemoteOnPush,
        PullStrategy DefaultPullStrategy,
        bool ForcePullAutoStash,
        bool ShowAuthorAvatars,
        bool OnlineAvatarLookupEnabled,
        bool HistoryPerformanceDiagnosticsEnabled,
        HistoryRenderingMode HistoryRenderingMode,
        string DefaultRepositoriesDirectory,
        IReadOnlyList<RecentRepositorySettings> RecentRepositories,
        IReadOnlyList<RepositoryGroupSettings> RepositoryGroups,
        bool RecentRepositoriesExpanded)
    {
        public static SettingsState Default { get; } = new(
            ApplicationThemeMode.System,
            CommitTimeDisplayMode.Smart,
            false,
            ApplicationLogLevel.Information,
            GitConsoleAutoOpenMode.OnErrors,
            false,
            false,
            PullStrategy.FastForwardOnly,
            false,
            true,
            true,
            false,
            HistoryRenderingMode.Full,
            GetDefaultRepositoriesDirectory(),
            Array.AsReadOnly(Array.Empty<RecentRepositorySettings>()),
            Array.AsReadOnly(new[] { new RepositoryGroupSettings(FavoritesId, "Favorites", 0, true) }),
            true);
    }

    private sealed class SettingsDocument
    {
        public ApplicationThemeMode ThemeMode { get; init; } = ApplicationThemeMode.System;
        public CommitTimeDisplayMode CommitTimeDisplayMode { get; init; } = CommitTimeDisplayMode.Smart;
        public bool LoggingEnabled { get; init; }
        public ApplicationLogLevel? LogLevel { get; init; }
        public GitConsoleAutoOpenMode? GitConsoleAutoOpenMode { get; init; }
        public bool ShowReflog { get; init; }
        public bool AutoSetupRemoteOnPush { get; init; }
        public PullStrategy DefaultPullStrategy { get; init; } = PullStrategy.FastForwardOnly;
        public bool ForcePullAutoStash { get; init; }
        public bool? ShowAuthorAvatars { get; init; }
        public bool? OnlineAvatarLookupEnabled { get; init; }
        public bool HistoryPerformanceDiagnosticsEnabled { get; init; }
        public HistoryRenderingMode? HistoryRenderingMode { get; init; }
        public string? DefaultRepositoriesDirectory { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? HistorySimplifiedRenderingEnabled { get; init; }
        public JsonElement RecentRepositories { get; init; }
        public JsonElement RepositoryGroups { get; init; }
        public bool RecentRepositoriesExpanded { get; init; } = true;
    }

    private sealed class TolerantPullStrategyConverter : JsonConverter<PullStrategy>
    {
        public override PullStrategy Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var name = reader.GetString();
                if (string.Equals(name, "GitConfiguration", StringComparison.Ordinal))
                    return PullStrategy.FastForwardOnly;
                if (Enum.TryParse<PullStrategy>(name, false, out var strategy)
                    && Enum.IsDefined(strategy))
                    return strategy;
            }
            if (reader.TokenType == JsonTokenType.Number
                && reader.TryGetInt32(out var value)
                && Enum.IsDefined(typeof(PullStrategy), value))
                return (PullStrategy)value;
            if (reader.TokenType is not JsonTokenType.String and not JsonTokenType.Number and not JsonTokenType.Null)
            {
                using var _ = JsonDocument.ParseValue(ref reader);
            }
            return PullStrategy.FastForwardOnly;
        }

        public override void Write(
            Utf8JsonWriter writer,
            PullStrategy value,
            JsonSerializerOptions options) =>
            writer.WriteStringValue((Enum.IsDefined(value) ? value : PullStrategy.FastForwardOnly).ToString());
    }

    private sealed class TolerantApplicationThemeModeConverter : JsonConverter<ApplicationThemeMode>
    {
        public override ApplicationThemeMode Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (Enum.TryParse<ApplicationThemeMode>(value, false, out var mode)
                    && Enum.IsDefined(typeof(ApplicationThemeMode), mode))
                    return mode;
                return ApplicationThemeMode.System;
            }

            if (reader.TokenType != JsonTokenType.Null)
            {
                using var _ = JsonDocument.ParseValue(ref reader);
            }
            return ApplicationThemeMode.System;
        }

        public override void Write(
            Utf8JsonWriter writer,
            ApplicationThemeMode value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
