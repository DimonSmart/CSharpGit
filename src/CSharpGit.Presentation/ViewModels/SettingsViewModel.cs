using System.ComponentModel;
using System.Runtime.CompilerServices;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Threading;

namespace CSharpGit.Presentation.ViewModels;

public sealed record ApplicationThemeOption(
    ApplicationThemeMode Mode,
    string Label);

public sealed record CommitTimeModeOption(
    CommitTimeDisplayMode Mode,
    string Label,
    string Description,
    string Example);

public sealed record ApplicationLogLevelOption(
    ApplicationLogLevel Level,
    string Label,
    string Description);

public sealed record GitConsoleAutoOpenOption(
    GitConsoleAutoOpenMode Mode,
    string Label,
    string Description);

public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAppSettingsService _settings;
    private readonly IUiDispatcher _uiDispatcher;
    private ApplicationThemeOption _selectedThemeMode;
    private CommitTimeModeOption _selectedCommitTimeMode;
    private bool _loggingEnabled;
    private ApplicationLogLevelOption _selectedLogLevel;
    private GitConsoleAutoOpenOption _selectedGitConsoleAutoOpenMode;
    private bool _autoSetupRemoteOnPush;
    private bool _showAuthorAvatars;
    private bool _onlineAvatarLookupEnabled;
    private bool _historyPerformanceDiagnosticsEnabled;
    private bool _historySimplifiedRenderingEnabled;
    private int _disposed;
    private int _synchronizingFromSettings;

    internal SettingsViewModel(
        IAppSettingsService settings,
        IUiDispatcher uiDispatcher)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));

        ThemeModes =
        [
            new(ApplicationThemeMode.System, "System"),
            new(ApplicationThemeMode.Light, "Light"),
            new(ApplicationThemeMode.Dark, "Dark")
        ];

        CommitTimeModes =
        [
            new(
                CommitTimeDisplayMode.Smart,
                "Smart (recommended)",
                "Recent commits use relative time. After 30 days, calendar dates are used.",
                "3 hours ago  →  Sep 1  →  Aug 17, 2025"),
            new(
                CommitTimeDisplayMode.Relative,
                "Relative",
                "Always show the age of the commit.",
                "3 hours ago  ·  12 days ago  ·  8 months ago"),
            new(
                CommitTimeDisplayMode.Absolute,
                "Absolute",
                "Always show local date and time.",
                "2026-09-09 12:08")
        ];

        LogLevels =
        [
            new(ApplicationLogLevel.Trace, "Trace", "Maximum detail for diagnosing difficult problems."),
            new(ApplicationLogLevel.Debug, "Debug", "Detailed diagnostic information."),
            new(ApplicationLogLevel.Information, "Information", "Normal application activity. Recommended default."),
            new(ApplicationLogLevel.Warning, "Warning", "Warnings and errors only."),
            new(ApplicationLogLevel.Error, "Error", "Errors and critical failures only."),
            new(ApplicationLogLevel.Critical, "Critical", "Only critical failures.")
        ];

        GitConsoleAutoOpenModes =
        [
            new(GitConsoleAutoOpenMode.OnErrors, "On errors", "Open when a user Git command fails."),
            new(GitConsoleAutoOpenMode.Always, "Always", "Open when a user Git command starts."),
            new(GitConsoleAutoOpenMode.Never, "Never", "Open only when requested manually.")
        ];

        _selectedThemeMode = FindThemeMode(_settings.ThemeMode);
        _selectedCommitTimeMode = FindCommitTimeMode(_settings.CommitTimeDisplayMode);
        _loggingEnabled = _settings.LoggingEnabled;
        _selectedLogLevel = FindLogLevel(_settings.LogLevel);
        _selectedGitConsoleAutoOpenMode = FindGitConsoleMode(_settings.GitConsoleAutoOpenMode);
        _autoSetupRemoteOnPush = _settings.AutoSetupRemoteOnPush;
        _showAuthorAvatars = _settings.ShowAuthorAvatars;
        _onlineAvatarLookupEnabled = _settings.OnlineAvatarLookupEnabled;
        _historyPerformanceDiagnosticsEnabled = _settings.HistoryPerformanceDiagnosticsEnabled;
        _historySimplifiedRenderingEnabled = _settings.HistorySimplifiedRenderingEnabled;

        _settings.Changed += Settings_Changed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ApplicationThemeOption> ThemeModes { get; }

    public ApplicationThemeOption SelectedThemeMode
    {
        get => _selectedThemeMode;
        set
        {
            if (Equals(_selectedThemeMode, value)) return;
            _selectedThemeMode = value;
            Notify();
        }
    }

    public IReadOnlyList<CommitTimeModeOption> CommitTimeModes { get; }

    public CommitTimeModeOption SelectedCommitTimeMode
    {
        get => _selectedCommitTimeMode;
        set
        {
            if (Equals(_selectedCommitTimeMode, value)) return;
            _selectedCommitTimeMode = value;
            Notify();
        }
    }

    public IReadOnlyList<ApplicationLogLevelOption> LogLevels { get; }

    public bool LoggingEnabled
    {
        get => _loggingEnabled;
        set
        {
            if (_loggingEnabled == value) return;
            _loggingEnabled = value;
            Notify();
        }
    }

    public ApplicationLogLevelOption SelectedLogLevel
    {
        get => _selectedLogLevel;
        set
        {
            if (Equals(_selectedLogLevel, value)) return;
            _selectedLogLevel = value;
            Notify();
        }
    }

    public IReadOnlyList<GitConsoleAutoOpenOption> GitConsoleAutoOpenModes { get; }

    public GitConsoleAutoOpenOption SelectedGitConsoleAutoOpenMode
    {
        get => _selectedGitConsoleAutoOpenMode;
        set
        {
            if (Equals(_selectedGitConsoleAutoOpenMode, value)) return;
            _selectedGitConsoleAutoOpenMode = value;
            Notify();
        }
    }

    public bool AutoSetupRemoteOnPush
    {
        get => _autoSetupRemoteOnPush;
        set
        {
            if (_autoSetupRemoteOnPush == value) return;
            _autoSetupRemoteOnPush = value;
            Notify();
        }
    }

    public bool ShowAuthorAvatars
    {
        get => _showAuthorAvatars;
        set
        {
            if (_showAuthorAvatars == value) return;
            _showAuthorAvatars = value;
            Notify();
        }
    }

    public bool OnlineAvatarLookupEnabled
    {
        get => _onlineAvatarLookupEnabled;
        set
        {
            if (_onlineAvatarLookupEnabled == value) return;
            _onlineAvatarLookupEnabled = value;
            Notify();
        }
    }

    public bool HistoryPerformanceDiagnosticsEnabled
    {
        get => _historyPerformanceDiagnosticsEnabled;
        set
        {
            if (_historyPerformanceDiagnosticsEnabled == value) return;
            _historyPerformanceDiagnosticsEnabled = value;
            Notify();
        }
    }

    public bool HistorySimplifiedRenderingEnabled
    {
        get => _historySimplifiedRenderingEnabled;
        set
        {
            if (_historySimplifiedRenderingEnabled == value) return;
            _historySimplifiedRenderingEnabled = value;
            Notify();
        }
    }

    internal bool IsSynchronizingFromSettings =>
        Volatile.Read(ref _synchronizingFromSettings) != 0;

    public async Task ApplyThemeModeAsync(
        ApplicationThemeOption option,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        SelectedThemeMode = option;
        try
        {
            await _settings.SetThemeModeAsync(option.Mode, cancellationToken);
        }
        catch
        {
            await SyncFromSettingsAfterFailureAsync();
            throw;
        }
    }

    public async Task ApplyCommitTimeModeAsync(
        CommitTimeModeOption option,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        SelectedCommitTimeMode = option;
        try
        {
            await _settings.SetCommitTimeDisplayModeAsync(option.Mode, cancellationToken);
        }
        catch
        {
            await SyncFromSettingsAfterFailureAsync();
            throw;
        }
    }

    public async Task ApplyLoggingSettingsAsync(
        bool enabled,
        ApplicationLogLevelOption option,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        LoggingEnabled = enabled;
        SelectedLogLevel = option;
        try
        {
            await _settings.SetLoggingSettingsAsync(enabled, option.Level, cancellationToken);
        }
        catch
        {
            await SyncFromSettingsAfterFailureAsync();
            throw;
        }
    }

    public async Task ApplyGitConsoleAutoOpenModeAsync(
        GitConsoleAutoOpenOption option,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        SelectedGitConsoleAutoOpenMode = option;
        try
        {
            await _settings.SetGitConsoleAutoOpenModeAsync(option.Mode, cancellationToken);
        }
        catch
        {
            await SyncFromSettingsAfterFailureAsync();
            throw;
        }
    }

    public async Task ApplyAutoSetupRemoteOnPushAsync(
        bool value,
        CancellationToken cancellationToken = default)
    {
        AutoSetupRemoteOnPush = value;
        try
        {
            await _settings.SetAutoSetupRemoteOnPushAsync(value, cancellationToken);
        }
        catch
        {
            await SyncFromSettingsAfterFailureAsync();
            throw;
        }
    }

    public async Task ApplyShowAuthorAvatarsAsync(
        bool value,
        CancellationToken cancellationToken = default)
    {
        ShowAuthorAvatars = value;
        try
        {
            await _settings.SetShowAuthorAvatarsAsync(value, cancellationToken);
        }
        catch
        {
            await SyncFromSettingsAfterFailureAsync();
            throw;
        }
    }

    public async Task ApplyOnlineAvatarLookupEnabledAsync(
        bool value,
        CancellationToken cancellationToken = default)
    {
        OnlineAvatarLookupEnabled = value;
        try
        {
            await _settings.SetOnlineAvatarLookupEnabledAsync(value, cancellationToken);
        }
        catch
        {
            await SyncFromSettingsAfterFailureAsync();
            throw;
        }
    }

    public async Task ApplyHistoryPerformanceDiagnosticsEnabledAsync(
        bool value,
        CancellationToken cancellationToken = default)
    {
        HistoryPerformanceDiagnosticsEnabled = value;
        try
        {
            await _settings.SetHistoryPerformanceDiagnosticsEnabledAsync(value, cancellationToken);
        }
        catch
        {
            await SyncFromSettingsAfterFailureAsync();
            throw;
        }
    }

    public async Task ApplyHistorySimplifiedRenderingEnabledAsync(
        bool value,
        CancellationToken cancellationToken = default)
    {
        HistorySimplifiedRenderingEnabled = value;
        try
        {
            await _settings.SetHistorySimplifiedRenderingEnabledAsync(value, cancellationToken);
        }
        catch
        {
            await SyncFromSettingsAfterFailureAsync();
            throw;
        }
    }

    private void Settings_Changed(object? sender, EventArgs e)
    {
        if (IsDisposed) return;

        if (_uiDispatcher.HasThreadAccess)
        {
            SyncFromSettings();
            return;
        }

        _uiDispatcher.TryEnqueue(() =>
        {
            if (!IsDisposed)
                SyncFromSettings();
        });
    }

    private void SyncFromSettings()
    {
        if (IsDisposed) return;

        Interlocked.Increment(ref _synchronizingFromSettings);
        try
        {
            SelectedThemeMode = FindThemeMode(_settings.ThemeMode);
            SelectedCommitTimeMode = FindCommitTimeMode(_settings.CommitTimeDisplayMode);
            LoggingEnabled = _settings.LoggingEnabled;
            SelectedLogLevel = FindLogLevel(_settings.LogLevel);
            SelectedGitConsoleAutoOpenMode = FindGitConsoleMode(_settings.GitConsoleAutoOpenMode);
            AutoSetupRemoteOnPush = _settings.AutoSetupRemoteOnPush;
            ShowAuthorAvatars = _settings.ShowAuthorAvatars;
            OnlineAvatarLookupEnabled = _settings.OnlineAvatarLookupEnabled;
            HistoryPerformanceDiagnosticsEnabled = _settings.HistoryPerformanceDiagnosticsEnabled;
            HistorySimplifiedRenderingEnabled = _settings.HistorySimplifiedRenderingEnabled;
        }
        finally
        {
            Interlocked.Decrement(ref _synchronizingFromSettings);
        }
    }

    private async Task SyncFromSettingsAfterFailureAsync()
    {
        if (IsDisposed) return;

        if (_uiDispatcher.HasThreadAccess)
        {
            SyncFromSettings();
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_uiDispatcher.TryEnqueue(() =>
            {
                try
                {
                    if (!IsDisposed)
                        SyncFromSettings();
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }))
            return;

        await completion.Task;
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private ApplicationThemeOption FindThemeMode(ApplicationThemeMode mode) =>
        ThemeModes.First(option => option.Mode == mode);

    private CommitTimeModeOption FindCommitTimeMode(CommitTimeDisplayMode mode) =>
        CommitTimeModes.First(option => option.Mode == mode);

    private ApplicationLogLevelOption FindLogLevel(ApplicationLogLevel level) =>
        LogLevels.First(option => option.Level == level);

    private GitConsoleAutoOpenOption FindGitConsoleMode(GitConsoleAutoOpenMode mode) =>
        GitConsoleAutoOpenModes.First(option => option.Mode == mode);

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _settings.Changed -= Settings_Changed;
    }
}
