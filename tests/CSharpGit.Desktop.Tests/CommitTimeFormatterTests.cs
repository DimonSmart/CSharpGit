using System.Globalization;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;

namespace CSharpGit.Desktop.Tests;

public sealed class CommitTimeFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void RelativeShowsRecentAge()
    {
        var value = Now.AddHours(-3);

        var result = CommitTimeFormatter.Format(value, CommitTimeDisplayMode.Relative, Now, CultureInfo.InvariantCulture);

        Assert.Equal("3 hours ago", result);
    }

    [Fact]
    public void SmartUsesRelativeForRecentCommitsAndDateForOlderCommits()
    {
        var recent = CommitTimeFormatter.Format(Now.AddDays(-12), CommitTimeDisplayMode.Smart, Now, CultureInfo.InvariantCulture);
        var older = CommitTimeFormatter.Format(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.FromHours(2)), CommitTimeDisplayMode.Smart, Now, CultureInfo.InvariantCulture);
        var previousYear = CommitTimeFormatter.Format(new DateTimeOffset(2025, 12, 24, 10, 0, 0, TimeSpan.FromHours(2)), CommitTimeDisplayMode.Smart, Now, CultureInfo.InvariantCulture);

        Assert.Equal("12 days ago", recent);
        Assert.Equal("Jul 1", older);
        Assert.Equal("Dec 24, 2025", previousYear);
    }

    [Fact]
    public void AbsoluteUsesStableLocalDateAndTimeShape()
    {
        var value = new DateTimeOffset(2026, 9, 9, 9, 15, 0, TimeSpan.FromHours(2));

        var result = CommitTimeFormatter.Format(value, CommitTimeDisplayMode.Absolute, Now, CultureInfo.InvariantCulture);

        Assert.Equal(value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), result);
    }

    [Fact]
    public void RelativeHandlesFutureClockSkew()
    {
        var result = CommitTimeFormatter.Format(Now.AddMinutes(5), CommitTimeDisplayMode.Relative, Now, CultureInfo.InvariantCulture);

        Assert.Equal("in 5 minutes", result);
    }
}
