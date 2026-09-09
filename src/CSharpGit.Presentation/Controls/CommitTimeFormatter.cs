using System.Globalization;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.Controls;

public static class CommitTimeFormatter
{
    public static string Format(
        DateTimeOffset value,
        CommitTimeDisplayMode mode,
        DateTimeOffset now,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return mode switch
        {
            CommitTimeDisplayMode.Relative => FormatRelative(value, now),
            CommitTimeDisplayMode.Absolute => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", culture),
            _ => FormatSmart(value, now, culture)
        };
    }

    public static string FormatExactLocal(DateTimeOffset value, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", culture);
    }

    private static string FormatSmart(DateTimeOffset value, DateTimeOffset now, CultureInfo culture)
    {
        var age = now - value;
        if (age < TimeSpan.Zero || age.TotalDays < 30) return FormatRelative(value, now);

        var localValue = value.ToLocalTime();
        var localNow = now.ToLocalTime();
        return localValue.Year == localNow.Year
            ? localValue.ToString("MMM d", culture)
            : localValue.ToString("MMM d, yyyy", culture);
    }

    private static string FormatRelative(DateTimeOffset value, DateTimeOffset now)
    {
        var difference = now - value;
        var future = difference < TimeSpan.Zero;
        var distance = difference.Duration();

        if (distance.TotalSeconds < 45) return future ? "in a moment" : "just now";
        if (distance.TotalSeconds < 90) return Describe(1, "minute", future);
        if (distance.TotalMinutes < 45) return Describe((int)Math.Floor(distance.TotalMinutes), "minute", future);
        if (distance.TotalMinutes < 90) return Describe(1, "hour", future);
        if (distance.TotalHours < 24) return Describe((int)Math.Floor(distance.TotalHours), "hour", future);
        if (distance.TotalHours < 42) return Describe(1, "day", future);
        if (distance.TotalDays < 30) return Describe((int)Math.Floor(distance.TotalDays), "day", future);
        if (distance.TotalDays < 45) return Describe(1, "month", future);
        if (distance.TotalDays < 365) return Describe(Math.Max(1, (int)Math.Floor(distance.TotalDays / 30.4375)), "month", future);
        if (distance.TotalDays < 545) return Describe(1, "year", future);
        return Describe(Math.Max(1, (int)Math.Floor(distance.TotalDays / 365.2425)), "year", future);
    }

    private static string Describe(int amount, string unit, bool future)
    {
        var value = amount == 1 ? $"1 {unit}" : $"{amount} {unit}s";
        return future ? $"in {value}" : $"{value} ago";
    }
}
