using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Infrastructure;

public sealed class JsonAppSettingsService : IAppSettingsService
{
    private const int MaxUnpinnedRecentRepositories = 1000;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new TolerantApplicationThemeModeConverter(),
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

    public bool ShowAuthorAvatars => Volatile.Read(ref _state).ShowAuthorAvatars;

    public bool OnlineAvatarLookupEnabled => Volatile.Read(ref _state).OnlineAvatarLookupEnabled;

    public bool HistoryPerformanceDiagnosticsEnabled => Volatile.Read(ref _state).HistoryPerformanceDiagnosticsEnabled;

    public HistoryRenderingMode HistoryRenderingMode => Volatile.Read(ref _state).HistoryRenderingMode;

    public IReadOnlyList<RecentRepositorySettings> RecentRepositories => Volatile.Read(ref _state).RecentRepositories;

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
                    existing?.PinnedOrder);
                var updated = current.RecentRepositories
                    .Where(candidate => !PathComparer.Equals(candidate.Path, normalizedPath))
                    .Append(entry);
                return current with { RecentRepositories = NormalizeRecentRepositories(updated) };
            },
            cancellationToken);
    }

    public Task SetRecentRepositoryPinnedAsync(
        string path,
        bool pinned,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedPath = NormalizePath(path);

        return UpdateAsync(
            current =>
            {
                var existing = current.RecentRepositories.FirstOrDefault(
                    candidate => PathComparer.Equals(candidate.Path, normalizedPath));
                if (existing is null || existing.IsPinned == pinned) return current;

                int? pinnedOrder = pinned
                    ? current.RecentRepositories.Count(candidate => candidate.IsPinned)
                    : null;
                var updated = current.RecentRepositories.Select(
                    candidate => PathComparer.Equals(candidate.Path, normalizedPath)
                        ? candidate with { IsPinned = pinned, PinnedOrder = pinnedOrder }
                        : candidate);
                return current with { RecentRepositories = NormalizeRecentRepositories(updated) };
            },
            cancellationToken);
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
                    : current with { RecentRepositories = NormalizeRecentRepositories(updated) };
            },
            cancellationToken);
    }

    public Task MovePinnedRepositoryAsync(
        string path,
        int newIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (newIndex < 0) throw new ArgumentOutOfRangeException(nameof(newIndex));

        var normalizedPath = NormalizePath(path);
        return UpdateAsync(
            current =>
            {
                var pinned = current.RecentRepositories
                    .Where(candidate => candidate.IsPinned)
                    .OrderBy(candidate => candidate.PinnedOrder ?? int.MaxValue)
                    .ToList();
                var currentIndex = pinned.FindIndex(
                    candidate => PathComparer.Equals(candidate.Path, normalizedPath));
                if (currentIndex < 0) return current;
                if (newIndex >= pinned.Count) throw new ArgumentOutOfRangeException(nameof(newIndex));
                if (currentIndex == newIndex) return current;

                var moving = pinned[currentIndex];
                pinned.RemoveAt(currentIndex);
                pinned.Insert(newIndex, moving);

                var reordered = pinned
                    .Select((candidate, index) => candidate with { PinnedOrder = index })
                    .ToDictionary(candidate => candidate.Path, PathComparer);
                var updated = current.RecentRepositories.Select(
                    candidate => candidate.IsPinned ? reordered[candidate.Path] : candidate);
                return current with { RecentRepositories = NormalizeRecentRepositories(updated) };
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
            var recentRepositories = NormalizeRecentRepositories(document.RecentRepositories);

            return new SettingsState(
                themeMode,
                commitTimeDisplayMode,
                document.LoggingEnabled,
                logLevel,
                gitConsoleAutoOpenMode,
                document.ShowReflog,
                document.AutoSetupRemoteOnPush,
                document.ShowAuthorAvatars ?? true,
                document.OnlineAvatarLookupEnabled ?? true,
                document.HistoryPerformanceDiagnosticsEnabled,
                historyRenderingMode,
                recentRepositories);
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
            ShowAuthorAvatars = state.ShowAuthorAvatars,
            OnlineAvatarLookupEnabled = state.OnlineAvatarLookupEnabled,
            HistoryPerformanceDiagnosticsEnabled = state.HistoryPerformanceDiagnosticsEnabled,
            HistoryRenderingMode = state.HistoryRenderingMode,
            RecentRepositories = state.RecentRepositories.ToList()
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

    private static IReadOnlyList<RecentRepositorySettings> NormalizeRecentRepositories(
        IEnumerable<RecentRepositorySettings>? entries)
    {
        if (entries is null) return [];

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
            .Select(group => group.First())
            .ToArray();

        var pinned = deduplicated
            .Where(candidate => candidate.IsPinned)
            .OrderBy(candidate => candidate.PinnedOrder.HasValue ? 0 : 1)
            .ThenBy(candidate => candidate.PinnedOrder ?? int.MaxValue)
            .ThenByDescending(candidate => candidate.LastOpenedUtc)
            .ThenBy(candidate => candidate.Path, PathComparer)
            .Select((candidate, index) => candidate with
            {
                IsPinned = true,
                PinnedOrder = index
            });

        var unpinned = deduplicated
            .Where(candidate => !candidate.IsPinned)
            .OrderByDescending(candidate => candidate.LastOpenedUtc)
            .ThenBy(candidate => candidate.Path, PathComparer)
            .Take(MaxUnpinnedRecentRepositories)
            .Select(candidate => candidate with
            {
                IsPinned = false,
                PinnedOrder = null
            });

        return Array.AsReadOnly(pinned.Concat(unpinned).ToArray());
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
            PinnedOrder = pinnedOrder
        };
    }

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string GetDisplayName(string path)
    {
        var name = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static string GetDefaultFilePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
        return Path.Combine(root, "CSharpGit.Presentation", "settings.json");
    }

    private sealed record SettingsState(
        ApplicationThemeMode ThemeMode,
        CommitTimeDisplayMode CommitTimeDisplayMode,
        bool LoggingEnabled,
        ApplicationLogLevel LogLevel,
        GitConsoleAutoOpenMode GitConsoleAutoOpenMode,
        bool ShowReflog,
        bool AutoSetupRemoteOnPush,
        bool ShowAuthorAvatars,
        bool OnlineAvatarLookupEnabled,
        bool HistoryPerformanceDiagnosticsEnabled,
        HistoryRenderingMode HistoryRenderingMode,
        IReadOnlyList<RecentRepositorySettings> RecentRepositories)
    {
        public static SettingsState Default { get; } = new(
            ApplicationThemeMode.System,
            CommitTimeDisplayMode.Smart,
            false,
            ApplicationLogLevel.Information,
            GitConsoleAutoOpenMode.OnErrors,
            false,
            false,
            true,
            true,
            false,
            HistoryRenderingMode.Full,
            Array.AsReadOnly(Array.Empty<RecentRepositorySettings>()));
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
        public bool? ShowAuthorAvatars { get; init; }
        public bool? OnlineAvatarLookupEnabled { get; init; }
        public bool HistoryPerformanceDiagnosticsEnabled { get; init; }
        public HistoryRenderingMode? HistoryRenderingMode { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? HistorySimplifiedRenderingEnabled { get; init; }
        public List<RecentRepositorySettings>? RecentRepositories { get; init; }
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
