using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.ViewModels;

public sealed record CommitTimeModeOption(
    CommitTimeDisplayMode Mode,
    string Label,
    string Description,
    string Example);

public sealed record ApplicationLogLevelOption(
    ApplicationLogLevel Level,
    string Label,
    string Description);

public sealed class SettingsViewModel
{
    private readonly IAppSettingsService _settings = AppSettingsContext.Current;

    public SettingsViewModel()
    {
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
    }

    public IReadOnlyList<CommitTimeModeOption> CommitTimeModes { get; }

    public CommitTimeModeOption SelectedCommitTimeMode { get; set; }

    public IReadOnlyList<ApplicationLogLevelOption> LogLevels { get; }

    public bool LoggingEnabled { get; set; }

    public ApplicationLogLevelOption SelectedLogLevel { get; set; }

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
}
