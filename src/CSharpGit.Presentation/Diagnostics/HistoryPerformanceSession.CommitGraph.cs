namespace CSharpGit.Presentation.Diagnostics;

internal enum HistoryGeometryBuildCause
{
    Unknown,
    FirstRender,
    TopologyChanged,
    HeightChanged,
    GeometryMetricsChanged,
    CacheMiss
}

internal sealed partial class HistoryPerformanceSession
{
    private readonly long[] _geometryBuildTriggers =
        new long[Enum.GetValues<HistoryGeometryUpdateReason>().Length];

    private readonly long[] _geometryBuildCauses =
        new long[Enum.GetValues<HistoryGeometryBuildCause>().Length];

    internal long GeometryBuildRequests;
    internal long GeometryActualBuilds;
    internal long GeometrySameKeySkips;
    internal long GeometryLocalCacheHits;
    internal long GeometrySharedCacheHits;
    internal long GeometryCacheMisses;
    internal long GeometryMaterializationCalls;
    internal long GeometrySharedCacheEntries;
    internal long GeometrySharedCacheEvictions;
    internal long GraphSizeChangedWidthOnly;
    internal long GraphSizeChangedHeightChanged;
    internal long GraphSizeChangedInsignificant;
    internal long GraphSizeChangedFirstValidHeight;

    internal void RecordGeometryActualBuild(
        HistoryGeometryUpdateReason trigger,
        HistoryGeometryBuildCause cause)
    {
        Interlocked.Increment(ref GeometryActualBuilds);
        Interlocked.Increment(ref _geometryBuildTriggers[(int)trigger]);
        Interlocked.Increment(ref _geometryBuildCauses[(int)cause]);
    }

    private Dictionary<string, long> GeometryBuildTriggerSnapshot()
    {
        var result = new Dictionary<string, long>();
        foreach (var trigger in Enum.GetValues<HistoryGeometryUpdateReason>())
            result[trigger.ToString()] = Volatile.Read(ref _geometryBuildTriggers[(int)trigger]);
        return result;
    }

    private Dictionary<string, long> GeometryBuildCauseSnapshot()
    {
        var result = new Dictionary<string, long>();
        foreach (var cause in Enum.GetValues<HistoryGeometryBuildCause>())
            result[cause.ToString()] = Volatile.Read(ref _geometryBuildCauses[(int)cause]);
        return result;
    }
}
