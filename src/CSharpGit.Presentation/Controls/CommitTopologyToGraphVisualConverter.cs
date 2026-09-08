using CSharpGit.Domain;
using CSharpGit.Presentation.Controls.CommitGraph;
using Microsoft.UI.Xaml.Data;

namespace CSharpGit.Presentation.Controls;

public sealed class CommitTopologyToGraphVisualConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not CommitTopology topology)
        {
            return null;
        }

        var laneCount = Math.Max(
            topology.Lane + 1,
            topology.Edges
                .SelectMany(edge => new[] { edge.FromLane, edge.ToLane })
                .DefaultIfEmpty(0)
                .Max() + 1);

        var incoming = Enumerable.Range(0, laneCount)
            .Select(lane => new CommitGraphSegment(lane, lane, lane))
            .ToList();

        var outgoing = Enumerable.Range(0, laneCount)
            .Where(lane => lane != topology.Lane)
            .Select(lane => new CommitGraphSegment(lane, lane, lane))
            .ToList();

        foreach (var edge in topology.Edges)
        {
            outgoing.Add(new CommitGraphSegment(edge.FromLane, edge.ToLane, edge.FromLane));
        }

        return new CommitGraphRowVisual(
            topology.Lane,
            topology.Lane,
            laneCount,
            incoming,
            outgoing);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
