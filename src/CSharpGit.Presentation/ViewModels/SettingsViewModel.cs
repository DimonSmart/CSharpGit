using System.ComponentModel;
using System.Runtime.CompilerServices;
using CSharpGit.Application.Abstractions;

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

public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAppSettingsService _settings;
    private ApplicationThemeOption _selectedThemeMode;
    private bool _disposed;

    internal SettingsViewModel(IAppSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;

        ThemeModes =
        [
            new(ApplicationThemeMode.System, "System"),
            new(ApplicationThemeMode.Light, "Light"),
            new(ApplicationThemeMode.Dark, "Dark")
        ];
        _selectedThemeMode = FindThemeMode(_settings.ThemeMode);

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
        SelectedCommitTimeMode = CommitTimeModes.First(option => option.Mode == _settings.CommitTimeDisplayMode);

        LogLevels =
        [
            new(ApplicationLogLevel.Trace, "Trace", "Maximum detail for diagnosing difficult problems."),
            new(ApplicationLogLevel.Debug, "Debug", "Detailed diagnostic information."),
            new(ApplicationLogLevel.Information, "Information", "Normal application activity. Recommended default."),
            new(ApplicationLogLevel.Warning, "Warning", "Warnings and errors only."),
            new(ApplicationLogLevel.Error, "Error", "Errors and critical failures only."),
            new(ApplicationLogLevel.Critical, "Critical", "Only critical failures.")
        ];
        LoggingEnabled = _settings.LoggingEnabled;
        SelectedLogLevel = LogLevels.First(option => option.Level == _settings.LogLevel);

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

    public CommitTimeModeOption SelectedCommitTimeMode { get; set; }

    public IReadOnlyList<ApplicationLogLevelOption> LogLevels { get; }

    public bool LoggingEnabled { get; set; }

    public ApplicationLogLevelOption SelectedLogLevel { get; set; }

    public Task ApplyThemeModeAsync(
        ApplicationThemeOption option,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        return _settings.SetThemeModeAsync(option.Mode, cancellationToken);
    }

    public async Task ApplyCommitTimeModeAsync(
        CommitTimeModeOption option,
        CancellationToken cancellationToken = default)
    {
        SelectedCommitTimeMode = option;
        await _settings.SetCommitTimeDisplayModeAsync(option.Mode, cancellationToken);
    }

    public async Task ApplyLoggingSettingsAsync(
        bool enabled,
        ApplicationLogLevelOption option,
        CancellationToken cancellationToken = default)
    {
        LoggingEnabled = enabled;
        SelectedLogLevel = option;
        await _settings.SetLoggingSettingsAsync(enabled, option.Level, cancellationToken);
    }

    private void Settings_Changed(object? sender, EventArgs e)
    {
        var selectedThemeMode = FindThemeMode(_settings.ThemeMode);
        if (Equals(_selectedThemeMode, selectedThemeMode)) return;

        _selectedThemeMode = selectedThemeMode;
        Notify(nameof(SelectedThemeMode));
    }

    private ApplicationThemeOption FindThemeMode(ApplicationThemeMode mode) =>
        ThemeModes.First(option => option.Mode == mode);

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.Changed -= Settings_Changed;
    }
}
