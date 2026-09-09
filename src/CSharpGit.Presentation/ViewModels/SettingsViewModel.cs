using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.ViewModels;

public sealed record CommitTimeModeOption(
    CommitTimeDisplayMode Mode,
    string Label,
    string Description,
    string Example);

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
    }

    public IReadOnlyList<CommitTimeModeOption> CommitTimeModes { get; }

    public CommitTimeModeOption SelectedCommitTimeMode { get; set; }

    public async Task ApplyCommitTimeModeAsync(
        CommitTimeModeOption option,
        CancellationToken cancellationToken = default)
    {
        SelectedCommitTimeMode = option;
        await _settings.SetCommitTimeDisplayModeAsync(option.Mode, cancellationToken);
    }
}
