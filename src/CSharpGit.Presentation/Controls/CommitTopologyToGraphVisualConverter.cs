using CSharpGit.Domain;
using CSharpGit.Presentation.Controls.CommitGraph;
using CSharpGit.Presentation.Diagnostics;
using Microsoft.UI.Xaml.Data;

namespace CSharpGit.Presentation.Controls;

public sealed class CommitTopologyToGraphVisualConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not CommitTopology topology)
        {
            CommitGraphDiagnostics.Trace("ConvertSkipped", $"valueType={value?.GetType().FullName ?? "null"}");
            return null;
        }

        var exactTopology = topology.HasExactGraphTopology;
        var allEdges = topology.IncomingEdges.Concat(topology.Edges).ToList();
        var laneCount = Math.Max(
            topology.Lane + 1,
            allEdges
                .SelectMany(edge => new[] { edge.FromLane, edge.ToLane })
                .DefaultIfEmpty(0)
                .Max() + 1);

        var incoming = exactTopology
            ? topology.IncomingEdges.Select(ToSegment).ToList()
            : Enumerable.Range(0, laneCount)
                .Select(lane => new CommitGraphSegment(lane, lane, lane))
                .ToList();

        var outgoing = exactTopology
            ? topology.Edges.Select(ToSegment).ToList()
            : Enumerable.Range(0, laneCount)
                .Where(lane => lane != topology.Lane)
                .Select(lane => new CommitGraphSegment(lane, lane, lane))
                .Concat(topology.Edges.Select(edge => new CommitGraphSegment(edge.FromLane, edge.ToLane, edge.FromLane)))
                .ToList();

        var visual = new CommitGraphRowVisual(
            topology.Lane,
            exactTopology ? topology.NodeTrackId : topology.Lane,
            laneCount,
            incoming,
            outgoing);
        CommitGraphDiagnostics.Trace(
            "TopologyConverted",
            $"topology=[{CommitGraphDiagnostics.DescribeTopology(topology)}] visual=[{CommitGraphDiagnostics.DescribeGraph(visual)}]");
        return visual;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static CommitGraphSegment ToSegment(TopologyEdge edge) =>
        new(edge.FromLane, edge.ToLane, edge.TrackId);
}
