namespace CSharpGit.Presentation.Core.History;

/// <summary>
/// Pixel measurements come from the actual UI controls. This planner has no XAML
/// dependencies and never truncates reference strings or changes reference order.
/// </summary>
public static class HistoryReferenceBadgeLayout
{
    public readonly record struct Metrics(
        double Minimum,
        double WithCounters,
        double Compact,
        double Full,
        double MinimumName,
        double FullName)
    {
        public bool HasCounters => WithCounters > Minimum + 0.01;
        public bool HasUpstream => Full > Compact + 0.01;
    }

    public readonly record struct Allocation(
        double Width,
        double NameWidth,
        bool ShowCounters,
        bool ShowUpstream);

    public sealed record Plan(IReadOnlyList<Allocation> Visible, int HiddenCount, double OverflowWidth)
    {
        public static Plan Empty { get; } = new([], 0, 0);
    }

    /// <summary>
    /// The callback returns the measured width of the exact +N badge, including
    /// border, padding and margins. A non-fitting overflow is never drawn.
    /// </summary>
    public static Plan Calculate(
        IReadOnlyList<Metrics> badges,
        double availableWidth,
        Func<int, double> measureOverflow)
    {
        if (badges.Count == 0)
            return Plan.Empty;

        var available = double.IsNaN(availableWidth)
            ? 0
            : Math.Max(0, availableWidth);
        if (!double.IsFinite(available))
            available = double.MaxValue / 4;

        for (var count = badges.Count; count >= 0; count--)
        {
            var hidden = badges.Count - count;
            var overflow = hidden == 0 ? 0 : measureOverflow(hidden);
            if (overflow < 0 || !double.IsFinite(overflow) || overflow > available)
                continue;

            var required = overflow;
            // Existing visible names must not become shorter as the panel
            // grows enough to introduce another badge. All but the last
            // visible name therefore have their full compact width.
            for (var i = 0; i < count; i++)
                required += i < count - 1 ? badges[i].Compact : badges[i].Minimum;
            if (required > available + 0.01)
                continue;

            var remaining = Math.Max(0, available - required);
            var widths = new double[count];
            var names = new double[count];
            var counters = new bool[count];
            var upstreams = new bool[count];
            for (var i = 0; i < count; i++)
            {
                var established = i < count - 1;
                widths[i] = established ? badges[i].Compact : badges[i].Minimum;
                names[i] = established ? badges[i].FullName : badges[i].MinimumName;
                counters[i] = established && badges[i].HasCounters;
            }

            // Tracking counts take precedence over growing names. The upstream
            // name is lower priority than either of them.
            for (var i = Math.Max(0, count - 1); i < count; i++)
            {
                var addition = Math.Max(0, badges[i].WithCounters - badges[i].Minimum);
                if (addition > remaining + 0.01)
                    continue;
                remaining -= addition;
                widths[i] += addition;
                counters[i] = badges[i].HasCounters;
            }

            // WinUI performs character ellipsis on the allocated name width;
            // never split a grapheme or manufacture an abbreviated Git name.
            for (var i = Math.Max(0, count - 1); i < count; i++)
            {
                var capacity = Math.Max(0, badges[i].FullName - badges[i].MinimumName);
                var addition = Math.Min(remaining, capacity);
                remaining -= addition;
                widths[i] += addition;
                names[i] += addition;
            }

            for (var i = 0; i < count; i++)
            {
                // Measure the optional annotation relative to the preceding
                // level so column spacing is accounted for as well.
                if (names[i] < badges[i].FullName - 0.01 || (badges[i].HasCounters && !counters[i]))
                    continue;
                var addition = Math.Max(0, badges[i].Full - badges[i].Compact);
                if (addition > remaining + 0.01)
                    continue;
                remaining -= addition;
                widths[i] += addition;
                upstreams[i] = badges[i].HasUpstream;
            }

            var allocations = new Allocation[count];
            for (var i = 0; i < count; i++)
                allocations[i] = new Allocation(widths[i], names[i], counters[i], upstreams[i]);

            return new Plan(allocations, hidden, overflow);
        }

        // Even +N does not fit: nothing is drawn outside the presenter.
        return new Plan([], badges.Count, 0);
    }
}
