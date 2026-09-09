using System.Runtime.CompilerServices;
using CSharpGit.Domain;
using CSharpGit.Presentation.Controls.CommitGraph;
using Microsoft.Extensions.Logging;

namespace CSharpGit.Presentation.Diagnostics;

internal static class CommitGraphDiagnostics
{
    private static ILogger? _logger;
    private static long _sequence;

    public static void Initialize(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger("CSharpGit.CommitGraph");
        Info("DiagnosticsInitialized", $"log={SessionFileLoggerProvider.CurrentLogPath}");
    }

    public static void Info(string eventName, string details) => Write(LogLevel.Information, eventName, details);

    public static void Trace(string eventName, string details) => Write(LogLevel.Trace, eventName, details);

    public static string DescribeContext(object? context) => context switch
    {
        HistoryRow row => $"history={ShortHash(row.Commit.Hash)} subject={Quote(row.Commit.Subject)}",
        null => "context=null",
        _ => $"contextType={context.GetType().FullName}"
    };

    public static string DescribeTopology(CommitTopology topology)
        => $"lane={topology.Lane} nodeTrack={topology.NodeTrackId} exact={topology.HasExactGraphTopology} "
           + $"incoming=[{DescribeEdges(topology.IncomingEdges)}] outgoing=[{DescribeEdges(topology.Edges)}]";

    public static string DescribeGraph(CommitGraphRowVisual? graph)
    {
        if (graph is null) return "graph=null";
        return $"visualId={RuntimeHelpers.GetHashCode(graph)} nodeLane={graph.NodeLane} nodeTrack={graph.NodeTrackId} lanes={graph.LaneCount} "
               + $"incoming=[{DescribeSegments(graph.IncomingSegments)}] outgoing=[{DescribeSegments(graph.OutgoingSegments)}]";
    }

    public static string DescribeGeometry(CommitGraphGeometry geometry)
    {
        var node = geometry.Node is null
            ? "node=null"
            : $"node=({geometry.Node.Center.X:0.##},{geometry.Node.Center.Y:0.##},r={geometry.Node.Radius:0.##},track={geometry.Node.TrackId})";
        var lines = string.Join(';', geometry.Lines.Select(line =>
            $"({line.Start.X:0.##},{line.Start.Y:0.##})->({line.End.X:0.##},{line.End.Y:0.##})@{line.TrackId}"));
        var curves = string.Join(';', geometry.Beziers.Select(curve =>
            $"({curve.Start.X:0.##},{curve.Start.Y:0.##})->({curve.End.X:0.##},{curve.End.Y:0.##})@{curve.TrackId}"));
        return $"geometry={geometry.Width:0.##}x{geometry.Height:0.##} {node} lines=[{lines}] curves=[{curves}]";
    }

    private static void Write(LogLevel level, string eventName, string details)
    {
        var logger = _logger;
        if (logger is null) return;
        var sequence = Interlocked.Increment(ref _sequence);
        logger.Log(level, new EventId(4100, eventName), "seq={Sequence} {EventName} {Details}", sequence, eventName, details);
    }

    private static string DescribeEdges(IEnumerable<TopologyEdge> edges)
        => string.Join(',', edges.Select(edge => $"{edge.FromLane}>{edge.ToLane}@{edge.TrackId}"));

    private static string DescribeSegments(IEnumerable<CommitGraphSegment> segments)
        => string.Join(',', segments.Select(segment => $"{segment.FromLane}>{segment.ToLane}@{segment.TrackId}"));

    private static string ShortHash(string hash) => hash[..Math.Min(8, hash.Length)];

    private static string Quote(string value) => $"\"{value.Replace("\"", "'", StringComparison.Ordinal)}\"";
}
