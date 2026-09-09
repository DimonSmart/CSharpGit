using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Infrastructure;

public sealed class JsonAppSettingsService : IAppSettingsService
{
    private const int MaxRecentRepositories = 8;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly string _filePath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private CommitTimeDisplayMode _commitTimeDisplayMode;
    private bool _loggingEnabled;
    private ApplicationLogLevel _logLevel;
    private IReadOnlyList<RecentRepositorySettings> _recentRepositories;

    public JsonAppSettingsService()
        : this(GetDefaultFilePath())
    {
    }

    internal JsonAppSettingsService(string filePath)
    {
        _filePath = filePath;
        var state = LoadSettings();
        _commitTimeDisplayMode = state.CommitTimeDisplayMode;
        _loggingEnabled = state.LoggingEnabled;
        _logLevel = state.LogLevel;
        _recentRepositories = state.RecentRepositories;
    }

    public CommitTimeDisplayMode CommitTimeDisplayMode => _commitTimeDisplayMode;

    public bool LoggingEnabled => _loggingEnabled;

    public ApplicationLogLevel LogLevel => _logLevel;

    public IReadOnlyList<RecentRepositorySettings> RecentRepositories => _recentRepositories;

    public event EventHandler? Changed;

    public async Task SetCommitTimeDisplayModeAsync(
        CommitTimeDisplayMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(CommitTimeDisplayMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (_commitTimeDisplayMode == mode) return;

        _commitTimeDisplayMode = mode;
        Changed?.Invoke(this, EventArgs.Empty);
        await PersistAsync(cancellationToken);
    }

    public async Task SetLoggingSettingsAsync(
        bool enabled,
        ApplicationLogLevel level,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(ApplicationLogLevel), level))
            throw new ArgumentOutOfRangeException(nameof(level));
        if (_loggingEnabled == enabled && _logLevel == level) return;

        _loggingEnabled = enabled;
        _logLevel = level;
        Changed?.Invoke(this, EventArgs.Empty);
        await PersistAsync(cancellationToken);
    }

    public async Task RecordRecentRepositoryAsync(
        string path,
        string displayName,
        string? lastBranchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var normalizedPath = NormalizePath(path);
        var entry = new RecentRepositorySettings(
            normalizedPath,
            displayName.Trim(),
            DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(lastBranchName) ? null : lastBranchName.Trim());

        _recentRepositories = new[] { entry }
            .Concat(_recentRepositories.Where(candidate => !PathComparer.Equals(candidate.Path, normalizedPath)))
            .Take(MaxRecentRepositories)
            .ToArray();

        Changed?.Invoke(this, EventArgs.Empty);
        await PersistAsync(cancellationToken);
    }

    public async Task RemoveRecentRepositoryAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedPath = NormalizePath(path);
        var updated = _recentRepositories
            .Where(candidate => !PathComparer.Equals(candidate.Path, normalizedPath))
            .ToArray();
        if (updated.Length == _recentRepositories.Count) return;

        _recentRepositories = updated;
        Changed?.Invoke(this, EventArgs.Empty);
        await PersistAsync(cancellationToken);
    }

    private SettingsState LoadSettings()
    {
        try
        {
            if (!File.Exists(_filePath)) return SettingsState.Default;
            var document = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(_filePath), SerializerOptions);
            if (document is null) return SettingsState.Default;

            var commitTimeDisplayMode = Enum.IsDefined(typeof(CommitTimeDisplayMode), document.CommitTimeDisplayMode)
                ? document.CommitTimeDisplayMode
                : CommitTimeDisplayMode.Smart;
            var logLevel = document.LogLevel is { } configuredLevel
                           && Enum.IsDefined(typeof(ApplicationLogLevel), configuredLevel)
                ? configuredLevel
                : ApplicationLogLevel.Information;
            var recentRepositories = NormalizeRecentRepositories(document.RecentRepositories);

            return new SettingsState(commitTimeDisplayMode, document.LoggingEnabled, logLevel, recentRepositories);
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

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            var document = new SettingsDocument
            {
                CommitTimeDisplayMode = _commitTimeDisplayMode,
                LoggingEnabled = _loggingEnabled,
                LogLevel = _logLevel,
                RecentRepositories = _recentRepositories.ToList()
            };
            var json = JsonSerializer.Serialize(document, SerializerOptions);
            var temporaryPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static IReadOnlyList<RecentRepositorySettings> NormalizeRecentRepositories(
        IEnumerable<RecentRepositorySettings>? entries)
    {
        if (entries is null) return [];

        var result = new List<RecentRepositorySettings>(MaxRecentRepositories);
        foreach (var entry in entries.OrderByDescending(candidate => candidate.LastOpenedUtc))
        {
            if (string.IsNullOrWhiteSpace(entry.Path)) continue;

            string normalizedPath;
            try
            {
                normalizedPath = NormalizePath(entry.Path);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (result.Any(candidate => PathComparer.Equals(candidate.Path, normalizedPath))) continue;

            var displayName = string.IsNullOrWhiteSpace(entry.DisplayName)
                ? GetDisplayName(normalizedPath)
                : entry.DisplayName.Trim();
            result.Add(entry with
            {
                Path = normalizedPath,
                DisplayName = displayName,
                LastOpenedUtc = entry.LastOpenedUtc.ToUniversalTime(),
                LastBranchName = string.IsNullOrWhiteSpace(entry.LastBranchName) ? null : entry.LastBranchName.Trim()
            });
            if (result.Count == MaxRecentRepositories) break;
        }

        return result;
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

    private readonly record struct SettingsState(
        CommitTimeDisplayMode CommitTimeDisplayMode,
        bool LoggingEnabled,
        ApplicationLogLevel LogLevel,
        IReadOnlyList<RecentRepositorySettings> RecentRepositories)
    {
        public static SettingsState Default { get; } = new(
            CommitTimeDisplayMode.Smart,
            false,
            ApplicationLogLevel.Information,
            []);
    }

    private sealed class SettingsDocument
    {
        public CommitTimeDisplayMode CommitTimeDisplayMode { get; init; } = CommitTimeDisplayMode.Smart;
        public bool LoggingEnabled { get; init; }
        public ApplicationLogLevel? LogLevel { get; init; }
        public List<RecentRepositorySettings>? RecentRepositories { get; init; }
    }
}
