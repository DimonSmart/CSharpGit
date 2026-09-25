using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CSharpGit.Application.Abstractions;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation.Diagnostics;

internal enum HistoryPerformanceStopReason
{
    Manual,
    RepositoryClosed,
    RepositoryChanged,
    DiagnosticsDisabled,
    ApplicationShutdown,
    Timeout
}

internal enum HistoryScanKind
{
    HistoryGlobalScan,
    HistoryIndexLookup,
    CommitLookup,
    ParentLookup,
    RefLookup
}

internal enum HistoryGeometryUpdateReason
{
    Unknown,
    Loaded,
    DataContextChanged,
    GraphChanged,
    SizeChanged,
    ThemeChanged,
    MetricsChanged,
    PresentationContextChanged
}

internal sealed record HistoryPerformanceStartContext(
    string HistorySource,
    int HistoryItems,
    int LoadedRows,
    int PageSize,
    string SortMode,
    bool HasMore,
    bool ShowReflog,
    bool HasTextFilter,
    int? LocalBranches,
    int? RemoteBranches,
    int? Tags,
    int? Refs,
    double GraphWidth,
    int ObservedMaxLaneCount,
    bool AvatarsEnabled,
    bool OnlineAvatarLookupEnabled,
    double WindowWidth,
    double WindowHeight,
    double? RasterizationScale);

internal readonly record struct HistoryPerformanceCaptureFiles(
    string JsonlPath,
    string SummaryPath,
    HistoryPerformanceStopReason StopReason);

internal readonly record struct HistorySlowOperation(
    long ElapsedTicks,
    string Operation,
    long DurationTicks,
    int LaneCount,
    string? Reason,
    int? HistoryItems);

internal sealed record HistoryDurationStatistics(
    long Count,
    double TotalMs,
    double AverageMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs);

internal sealed class HistoryAtomicDurationHistogram
{
    private static readonly double[] BucketUpperMs =
    [
        0.25, 0.5, 1, 2, 4, 8, 16, 33, 50, 100, 250, 500, 1000, double.PositiveInfinity
    ];

    private readonly long[] _buckets = new long[BucketUpperMs.Length];
    private long _count;
    private long _totalTicks;
    private long _maxTicks;

    internal void ObserveTicks(long ticks)
    {
        if (ticks < 0) return;

        Interlocked.Increment(ref _count);
        Interlocked.Add(ref _totalTicks, ticks);
        UpdateMax(ref _maxTicks, ticks);

        var milliseconds = ticks * 1000d / Stopwatch.Frequency;
        var bucket = 0;
        while (bucket < BucketUpperMs.Length - 1 && milliseconds >= BucketUpperMs[bucket])
            bucket++;
        Interlocked.Increment(ref _buckets[bucket]);
    }

    internal HistoryDurationStatistics Snapshot()
    {
        var count = Volatile.Read(ref _count);
        var totalTicks = Volatile.Read(ref _totalTicks);
        var maxTicks = Volatile.Read(ref _maxTicks);
        var buckets = new long[_buckets.Length];
        for (var index = 0; index < buckets.Length; index++)
            buckets[index] = Volatile.Read(ref _buckets[index]);

        var totalMs = TicksToMilliseconds(totalTicks);
        return new HistoryDurationStatistics(
            count,
            totalMs,
            count == 0 ? 0 : totalMs / count,
            Percentile(buckets, count, 0.50),
            Percentile(buckets, count, 0.95),
            Percentile(buckets, count, 0.99),
            TicksToMilliseconds(maxTicks));
    }

    private static double Percentile(long[] buckets, long count, double percentile)
    {
        if (count <= 0) return 0;
        var target = Math.Max(1, (long)Math.Ceiling(count * percentile));
        long cumulative = 0;
        for (var index = 0; index < buckets.Length; index++)
        {
            cumulative += buckets[index];
            if (cumulative < target) continue;
            var upper = BucketUpperMs[index];
            return double.IsPositiveInfinity(upper) ? 1000 : upper;
        }

        return 0;
    }

    internal static double TicksToMilliseconds(long ticks) =>
        ticks * 1000d / Stopwatch.Frequency;

    private static void UpdateMax(ref long target, long value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current) return;
            current = observed;
        }
    }
}

internal sealed class HistoryPerformanceSession
{
    private const int SlowOperationCapacity = 2048;
    private const int WriterCapacity = 128;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly HistoryPerformanceStartContext _context;
    private readonly Channel<string> _writerChannel;
    private readonly CancellationTokenSource _snapshotCts = new();
    private readonly HistorySlowOperation[] _slowOperations = new HistorySlowOperation[SlowOperationCapacity];
    private readonly long[] _laneCountHistogram = new long[65];
    private readonly TaskCompletionSource _writerReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Task _writerTask;
    private Task? _snapshotTask;
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;
    private readonly long _allocatedBaseline = GC.GetTotalAllocatedBytes(false);
    private readonly int _gen0Baseline = GC.CollectionCount(0);
    private readonly int _gen1Baseline = GC.CollectionCount(1);
    private readonly int _gen2Baseline = GC.CollectionCount(2);
    private readonly long _heapBaseline = GC.GetGCMemoryInfo().HeapSizeBytes;
    private readonly long _workingSetBaseline;
    private readonly TimeSpan _processorBaseline;

    private readonly HistoryAtomicDurationHistogram _renderIntervals = new();
    private readonly HistoryAtomicDurationHistogram _graphMeasure = new();
    private readonly HistoryAtomicDurationHistogram _graphArrange = new();
    private readonly HistoryAtomicDurationHistogram _geometryRebuild = new();
    private readonly HistoryAtomicDurationHistogram _geometryBuilder = new();
    private readonly HistoryAtomicDurationHistogram _geometryMaterialization = new();
    private readonly HistoryAtomicDurationHistogram _topologyConverter = new();
    private readonly HistoryAtomicDurationHistogram _layoutInitialize = new();
    private readonly HistoryAtomicDurationHistogram _layoutUpdate = new();
    private readonly HistoryAtomicDurationHistogram _layoutApply = new();
    private readonly HistoryAtomicDurationHistogram _presentationPublish = new();
    private readonly HistoryAtomicDurationHistogram _presentationDelivery = new();
    private readonly HistoryAtomicDurationHistogram _avatarResolve = new();
    private readonly HistoryAtomicDurationHistogram _historyGlobalScan = new();
    private readonly HistoryAtomicDurationHistogram _historyIndexLookup = new();
    private readonly HistoryAtomicDurationHistogram _commitLookup = new();
    private readonly HistoryAtomicDurationHistogram _parentLookup = new();
    private readonly HistoryAtomicDurationHistogram _refLookup = new();

    private readonly long[] _geometryRebuildReasons =
        new long[Enum.GetValues<HistoryGeometryUpdateReason>().Length];

    private long _lastRenderTimestamp;
    private long _lastViewChangedTimestamp;
    private long _maxViewChangedIntervalTicks;
    private int _slowOperationCount;
    private int _stopped;
    private Exception? _writerFailure;

    internal long GraphControlsCreated;
    internal long GraphLoaded;
    internal long GraphUnloaded;
    internal long GraphDataContextChanged;
    internal long GraphPropertyChanged;
    internal long GraphPresentationContextChanged;
    internal long GraphMetricsChanged;
    internal long GraphSizeChanged;
    internal long GraphThemeChanged;
    internal long GraphMeasureCalls;
    internal long GraphArrangeCalls;
    internal long GeometryAttempts;
    internal long GeometrySkippedInvalidHeight;
    internal long GeometryCacheHits;
    internal long GeometryRebuilds;
    internal long GeometrySegmentCount;
    internal long TopologyConversions;
    internal long TopologyExact;
    internal long TopologyFallback;
    internal long TopologyLaneTotal;
    internal long TopologyIncomingTotal;
    internal long TopologyOutgoingTotal;
    internal long LayoutInitializeCalls;
    internal long LayoutInitializeRowsExamined;
    internal long LayoutUpdateCalls;
    internal long LayoutResetCount;
    internal long LayoutNewRowsExamined;
    internal long LayoutChangedCalls;
    internal long LayoutQueueCalls;
    internal long LayoutApplyCalls;
    internal long PresentationPublishes;
    internal long PresentationDeliveries;
    internal long ViewChangedCount;
    internal long ViewChangedIntermediateCount;
    internal long ViewChangedFinalCount;
    internal long LoadMoreThresholdCount;
    internal long PageLoadsStarted;
    internal long ContainerChanges;
    internal long ContainerRealizations;
    internal long ContainerRecycles;
    internal long SelectionChanges;
    internal long ItemsSourceChanges;
    internal long HistoryCollectionResets;
    internal long HistoryRowsAppended;
    internal long AvatarControlsCreated;
    internal long AvatarLoaded;
    internal long AvatarUnloaded;
    internal long AvatarIdentityChanges;
    internal long AvatarRefreshCalls;
    internal long AvatarRequestsStarted;
    internal long AvatarRequestsCancelled;
    internal long AvatarResultsApplied;
    internal long AvatarStaleResultsIgnored;
    internal long AvatarImmediateResolveCompletions;
    internal long AvatarAsyncResolveCompletions;
    internal long AvatarMemoryCacheHits;
    internal long AvatarMemoryCacheMisses;
    internal long AvatarDiskCacheHits;
    internal long AvatarDiskCacheMisses;
    internal long AvatarDiskCacheWrites;
    internal long AvatarDiskBytesWritten;
    internal long AvatarRemoteRequests;
    internal long AvatarRemoteBytesRead;
    internal long GitCommands;
    internal long GitUserCommands;
    internal long GitInternalCommands;
    internal long GitNetworkCommands;
    internal long GitStatusCommands;
    internal long GitLogCommands;
    internal long GitForEachRefCommands;
    internal long GitFetchCommands;
    internal long GitPullCommands;
    internal long GitPushCommands;
    internal long GitCloneCommands;
    internal long GitLsRemoteCommands;
    internal long GitOtherCommands;
    internal long HistoryGlobalScans;
    internal long HistoryIndexLookups;
    internal long CommitLookups;
    internal long ParentLookups;
    internal long RefLookups;
    internal long HistoryItemsExamined;
    internal long CommitItemsExamined;
    internal long ParentItemsExamined;
    internal long RefItemsExamined;
    internal long RenderCallbacks;
    internal long RenderIntervalsOver16;
    internal long RenderIntervalsOver33;
    internal long RenderIntervalsOver50;
    internal long RenderIntervalsOver100;
    internal long DroppedDiagnosticRecords;
    internal long DroppedSlowEvents;
    internal int VisitedMinIndex = int.MaxValue;
    internal int VisitedMaxIndex = int.MinValue;
    internal int HistoryMutatedDuringCapture;
    internal int PageLoadedDuringCapture;
    internal int RepositoryChangedDuringCapture;
    internal int WindowResizedDuringCapture;
    internal int GraphLayoutPublishedDuringCapture;
    internal int GitCommandExecutedDuringCapture;
    internal int SettingsChangedDuringCapture;
    internal int AvatarOnlineRequestDuringCapture;

    internal HistoryPerformanceSession(
        HistoryPerformanceStartContext context,
        string directory)
    {
        _context = context;
        _workingSetBaseline = _process.WorkingSet64;
        _processorBaseline = _process.TotalProcessorTime;

        Directory.CreateDirectory(directory);
        var stamp = _startedUtc.ToString("yyyyMMdd-HHmmss");
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var baseName = $"history-perf-{stamp}-p{Environment.ProcessId}-{sessionId}";
        JsonlPath = Path.Combine(directory, baseName + ".jsonl");
        SummaryPath = Path.Combine(directory, baseName + ".summary.txt");

        _writerChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(WriterCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _writerTask = Task.Run(WriterLoopAsync);
    }

    internal string JsonlPath { get; }
    internal string SummaryPath { get; }
    internal long StartTimestamp => _startTimestamp;
    internal TimeSpan Elapsed => Stopwatch.GetElapsedTime(_startTimestamp);

    internal async Task StartAsync()
    {
        await _writerReady.Task.ConfigureAwait(false);
        await WriteRequiredAsync(Serialize(new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["type"] = "sessionStart",
            ["elapsedMs"] = 0,
            ["startedUtc"] = _startedUtc,
            ["appVersion"] = typeof(HistoryPerformanceSession).Assembly.GetName().Version?.ToString(),
#if DEBUG
            ["buildConfiguration"] = "Debug",
#else
            ["buildConfiguration"] = "Release",
#endif
            ["os"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["uiBackend"] = "Uno/WinUI desktop",
            ["processId"] = Environment.ProcessId,
            ["windowClientWidth"] = _context.WindowWidth,
            ["windowClientHeight"] = _context.WindowHeight,
            ["displayScale"] = _context.RasterizationScale,
            ["historySource"] = _context.HistorySource,
            ["historyItems"] = _context.HistoryItems,
            ["loadedRows"] = _context.LoadedRows,
            ["pageSize"] = _context.PageSize,
            ["sortMode"] = _context.SortMode,
            ["hasMore"] = _context.HasMore,
            ["showReflog"] = _context.ShowReflog,
            ["hasTextFilter"] = _context.HasTextFilter,
            ["localBranches"] = _context.LocalBranches,
            ["remoteBranches"] = _context.RemoteBranches,
            ["tags"] = _context.Tags,
            ["refs"] = _context.Refs,
            ["graphWidth"] = _context.GraphWidth,
            ["observedMaxLaneCount"] = _context.ObservedMaxLaneCount,
            ["avatarsEnabled"] = _context.AvatarsEnabled,
            ["onlineAvatarLookupEnabled"] = _context.OnlineAvatarLookupEnabled
        })).ConfigureAwait(false);

        _snapshotTask = Task.Run(SnapshotLoopAsync);
    }

    internal void RecordRenderingCallback(long timestamp)
    {
        Interlocked.Increment(ref RenderCallbacks);
        var previous = Interlocked.Exchange(ref _lastRenderTimestamp, timestamp);
        if (previous == 0 || timestamp <= previous) return;

        var interval = timestamp - previous;
        _renderIntervals.ObserveTicks(interval);
        var milliseconds = HistoryAtomicDurationHistogram.TicksToMilliseconds(interval);
        if (milliseconds > 16.7) Interlocked.Increment(ref RenderIntervalsOver16);
        if (milliseconds > 33) Interlocked.Increment(ref RenderIntervalsOver33);
        if (milliseconds > 50) Interlocked.Increment(ref RenderIntervalsOver50);
        if (milliseconds > 100) Interlocked.Increment(ref RenderIntervalsOver100);
    }

    internal void RecordViewChanged(long timestamp, bool isIntermediate)
    {
        Interlocked.Increment(ref ViewChangedCount);
        if (isIntermediate)
            Interlocked.Increment(ref ViewChangedIntermediateCount);
        else
            Interlocked.Increment(ref ViewChangedFinalCount);
        var previous = Interlocked.Exchange(ref _lastViewChangedTimestamp, timestamp);
        if (previous <= 0 || timestamp <= previous) return;
        UpdateMax(ref _maxViewChangedIntervalTicks, timestamp - previous);
    }

    internal void RecordVisitedIndex(int index, bool recycled)
    {
        Interlocked.Increment(ref ContainerChanges);
        if (recycled)
            Interlocked.Increment(ref ContainerRecycles);
        else
            Interlocked.Increment(ref ContainerRealizations);

        UpdateMin(ref VisitedMinIndex, index);
        UpdateMax(ref VisitedMaxIndex, index);
    }

    internal void RecordGraphMeasure(long startTicks)
    {
        if (startTicks <= 0) return;
        Interlocked.Increment(ref GraphMeasureCalls);
        _graphMeasure.ObserveTicks(Stopwatch.GetTimestamp() - startTicks);
    }

    internal void RecordGraphArrange(long startTicks)
    {
        if (startTicks <= 0) return;
        Interlocked.Increment(ref GraphArrangeCalls);
        _graphArrange.ObserveTicks(Stopwatch.GetTimestamp() - startTicks);
    }

    internal void RecordGeometryRebuild(
        HistoryGeometryUpdateReason reason,
        long startTicks,
        int laneCount,
        int segmentCount)
    {
        Interlocked.Increment(ref GeometryRebuilds);
        Interlocked.Add(ref GeometrySegmentCount, Math.Max(0, segmentCount));
        Interlocked.Increment(ref _geometryRebuildReasons[(int)reason]);
        var laneBucket = Math.Clamp(laneCount, 0, _laneCountHistogram.Length - 1);
        Interlocked.Increment(ref _laneCountHistogram[laneBucket]);
        if (startTicks <= 0) return;

        var duration = Stopwatch.GetTimestamp() - startTicks;
        _geometryRebuild.ObserveTicks(duration);
        RecordSlowOperation("CommitGraph.GeometryRebuild", duration, laneCount, reason.ToString(), null);
    }

    internal void RecordGeometryBuilder(long startTicks)
    {
        if (startTicks <= 0) return;
        _geometryBuilder.ObserveTicks(Stopwatch.GetTimestamp() - startTicks);
    }

    internal void RecordGeometryMaterialization(long startTicks)
    {
        if (startTicks <= 0) return;
        _geometryMaterialization.ObserveTicks(Stopwatch.GetTimestamp() - startTicks);
    }

    internal void RecordTopologyConversion(
        long startTicks,
        bool exact,
        int lanes,
        int incoming,
        int outgoing)
    {
        Interlocked.Increment(ref TopologyConversions);
        if (exact)
            Interlocked.Increment(ref TopologyExact);
        else
            Interlocked.Increment(ref TopologyFallback);
        Interlocked.Add(ref TopologyLaneTotal, lanes);
        Interlocked.Add(ref TopologyIncomingTotal, incoming);
        Interlocked.Add(ref TopologyOutgoingTotal, outgoing);
        if (startTicks <= 0) return;

        var duration = Stopwatch.GetTimestamp() - startTicks;
        _topologyConverter.ObserveTicks(duration);
        RecordSlowOperation("TopologyConverter", duration, lanes, exact ? "Exact" : "Fallback", null);
    }

    internal void RecordLayoutInitialize(long startTicks, int rowsExamined)
    {
        Interlocked.Increment(ref LayoutInitializeCalls);
        Interlocked.Add(ref LayoutInitializeRowsExamined, rowsExamined);
        if (startTicks > 0)
            _layoutInitialize.ObserveTicks(Stopwatch.GetTimestamp() - startTicks);
    }

    internal void RecordLayoutUpdate(long startTicks, bool reset, int newRowsExamined, bool changed)
    {
        Interlocked.Increment(ref LayoutUpdateCalls);
        if (reset) Interlocked.Increment(ref LayoutResetCount);
        Interlocked.Add(ref LayoutNewRowsExamined, newRowsExamined);
        if (changed) Interlocked.Increment(ref LayoutChangedCalls);
        if (startTicks > 0)
            _layoutUpdate.ObserveTicks(Stopwatch.GetTimestamp() - startTicks);
    }

    internal void RecordLayoutApply(long startTicks)
    {
        Interlocked.Increment(ref LayoutApplyCalls);
        if (startTicks > 0)
            _layoutApply.ObserveTicks(Stopwatch.GetTimestamp() - startTicks);
    }

    internal void RecordPresentationPublish(long startTicks)
    {
        Interlocked.Increment(ref PresentationPublishes);
        Volatile.Write(ref GraphLayoutPublishedDuringCapture, 1);
        if (startTicks > 0)
            _presentationPublish.ObserveTicks(Stopwatch.GetTimestamp() - startTicks);
    }

    internal void RecordPresentationDelivery(long startTicks)
    {
        Interlocked.Increment(ref PresentationDeliveries);
        if (startTicks > 0)
            _presentationDelivery.ObserveTicks(Stopwatch.GetTimestamp() - startTicks);
    }

    internal void RecordAvatarResolve(long startTicks, bool completedSynchronously)
    {
        if (startTicks > 0)
            _avatarResolve.ObserveTicks(Stopwatch.GetTimestamp() - startTicks);
        if (completedSynchronously)
            Interlocked.Increment(ref AvatarImmediateResolveCompletions);
        else
            Interlocked.Increment(ref AvatarAsyncResolveCompletions);
    }

    internal void RecordHistoryScan(
        HistoryScanKind kind,
        long startTicks,
        int itemsExamined = 0)
    {
        HistoryAtomicDurationHistogram histogram;
        string operation;
        switch (kind)
        {
            case HistoryScanKind.HistoryGlobalScan:
                Interlocked.Increment(ref HistoryGlobalScans);
                Interlocked.Add(ref HistoryItemsExamined, Math.Max(0, itemsExamined));
                histogram = _historyGlobalScan;
                operation = "HistoryGlobalScan";
                break;
            case HistoryScanKind.HistoryIndexLookup:
                Interlocked.Increment(ref HistoryIndexLookups);
                histogram = _historyIndexLookup;
                operation = "HistoryIndexLookup";
                break;
            case HistoryScanKind.CommitLookup:
                Interlocked.Increment(ref CommitLookups);
                Interlocked.Add(ref CommitItemsExamined, Math.Max(0, itemsExamined));
                histogram = _commitLookup;
                operation = "CommitLookup";
                break;
            case HistoryScanKind.ParentLookup:
                Interlocked.Increment(ref ParentLookups);
                Interlocked.Add(ref ParentItemsExamined, Math.Max(0, itemsExamined));
                histogram = _parentLookup;
                operation = "ParentLookup";
                break;
            case HistoryScanKind.RefLookup:
                Interlocked.Increment(ref RefLookups);
                Interlocked.Add(ref RefItemsExamined, Math.Max(0, itemsExamined));
                histogram = _refLookup;
                operation = "RefLookup";
                break;
            default:
                return;
        }

        if (startTicks <= 0) return;
        var duration = Stopwatch.GetTimestamp() - startTicks;
        histogram.ObserveTicks(duration);
        RecordSlowOperation(operation, duration, 0, null, itemsExamined > 0 ? itemsExamined : null);
    }

    internal void RecordHistoryGlobalScan(long startTicks, int? historyItems = null) =>
        RecordHistoryScan(HistoryScanKind.HistoryGlobalScan, startTicks, historyItems ?? 0);

    internal void RecordHistoryIndexLookup(long startTicks, int? historyItems = null) =>
        RecordHistoryScan(HistoryScanKind.HistoryIndexLookup, startTicks, historyItems ?? 0);

    internal void RecordAvatarDiagnosticActivity(AuthorAvatarDiagnosticActivityEventArgs activity)
    {
        switch (activity.Kind)
        {
            case AuthorAvatarDiagnosticActivityKind.MemoryCacheHit:
                Interlocked.Increment(ref AvatarMemoryCacheHits);
                break;
            case AuthorAvatarDiagnosticActivityKind.MemoryCacheMiss:
                Interlocked.Increment(ref AvatarMemoryCacheMisses);
                break;
            case AuthorAvatarDiagnosticActivityKind.DiskCacheHit:
                Interlocked.Increment(ref AvatarDiskCacheHits);
                break;
            case AuthorAvatarDiagnosticActivityKind.DiskCacheMiss:
                Interlocked.Increment(ref AvatarDiskCacheMisses);
                break;
            case AuthorAvatarDiagnosticActivityKind.DiskCacheWrite:
                Interlocked.Increment(ref AvatarDiskCacheWrites);
                Interlocked.Add(ref AvatarDiskBytesWritten, Math.Max(0, activity.Bytes));
                break;
            case AuthorAvatarDiagnosticActivityKind.RemoteRequest:
                Interlocked.Increment(ref AvatarRemoteRequests);
                Volatile.Write(ref AvatarOnlineRequestDuringCapture, 1);
                break;
            case AuthorAvatarDiagnosticActivityKind.RemoteBytesRead:
                Interlocked.Add(ref AvatarRemoteBytesRead, Math.Max(0, activity.Bytes));
                break;
        }
    }

    internal void RecordGitCommand(GitCommandActivity activity)
    {
        Interlocked.Increment(ref GitCommands);
        Volatile.Write(ref GitCommandExecutedDuringCapture, 1);
        if (activity.CommandKind == GitCommandKind.User)
            Interlocked.Increment(ref GitUserCommands);
        else
            Interlocked.Increment(ref GitInternalCommands);

        var verb = GetGitVerb(activity.Arguments);
        if (IsNetworkVerb(verb))
            Interlocked.Increment(ref GitNetworkCommands);

        switch (verb)
        {
            case "status": Interlocked.Increment(ref GitStatusCommands); break;
            case "log": Interlocked.Increment(ref GitLogCommands); break;
            case "for-each-ref": Interlocked.Increment(ref GitForEachRefCommands); break;
            case "fetch": Interlocked.Increment(ref GitFetchCommands); break;
            case "pull": Interlocked.Increment(ref GitPullCommands); break;
            case "push": Interlocked.Increment(ref GitPushCommands); break;
            case "clone": Interlocked.Increment(ref GitCloneCommands); break;
            case "ls-remote": Interlocked.Increment(ref GitLsRemoteCommands); break;
            default: Interlocked.Increment(ref GitOtherCommands); break;
        }
    }

    internal void RecordSlowOperation(
        string operation,
        long durationTicks,
        int laneCount,
        string? reason,
        int? historyItems)
    {
        if (HistoryAtomicDurationHistogram.TicksToMilliseconds(durationTicks) < 8) return;

        var index = Interlocked.Increment(ref _slowOperationCount) - 1;
        if ((uint)index >= (uint)_slowOperations.Length)
        {
            Interlocked.Increment(ref DroppedSlowEvents);
            return;
        }

        _slowOperations[index] = new HistorySlowOperation(
            Stopwatch.GetTimestamp() - _startTimestamp,
            operation,
            durationTicks,
            laneCount,
            reason,
            historyItems);
    }

    internal async Task<HistoryPerformanceCaptureFiles> StopAsync(
        HistoryPerformanceStopReason reason)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return new HistoryPerformanceCaptureFiles(JsonlPath, SummaryPath, reason);

        _snapshotCts.Cancel();
        if (_snapshotTask is not null)
        {
            try { await _snapshotTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        var slowCount = Math.Min(Volatile.Read(ref _slowOperationCount), _slowOperations.Length);
        for (var index = 0; index < slowCount; index++)
        {
            var item = _slowOperations[index];
            var durationMs = HistoryAtomicDurationHistogram.TicksToMilliseconds(item.DurationTicks);
            await WriteRequiredAsync(Serialize(new Dictionary<string, object?>
            {
                ["schemaVersion"] = 1,
                ["type"] = "slowOperation",
                ["elapsedMs"] = HistoryAtomicDurationHistogram.TicksToMilliseconds(item.ElapsedTicks),
                ["operation"] = item.Operation,
                ["durationMs"] = durationMs,
                ["severity"] = durationMs >= 50 ? "critical" : durationMs >= 16 ? "verySlow" : "slow",
                ["laneCount"] = item.LaneCount == 0 ? null : item.LaneCount,
                ["reason"] = item.Reason,
                ["historyItems"] = item.HistoryItems
            })).ConfigureAwait(false);
        }

        var summaryRecord = CreateSummaryRecord(reason);
        await WriteRequiredAsync(Serialize(summaryRecord)).ConfigureAwait(false);
        _writerChannel.Writer.TryComplete();
        try { await _writerTask.ConfigureAwait(false); }
        catch { }

        try
        {
            await File.WriteAllTextAsync(
                    SummaryPath,
                    CreateTextSummary(reason),
                    Encoding.UTF8)
                .ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics must never take down the application.
        }

        return new HistoryPerformanceCaptureFiles(JsonlPath, SummaryPath, reason);
    }

    private async Task SnapshotLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_snapshotCts.Token).ConfigureAwait(false))
            {
                var line = Serialize(CreateSnapshotRecord());
                if (!_writerChannel.Writer.TryWrite(line))
                    Interlocked.Increment(ref DroppedDiagnosticRecords);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Dictionary<string, object?> CreateSnapshotRecord() => new()
    {
        ["schemaVersion"] = 1,
        ["type"] = "snapshot",
        ["elapsedMs"] = Elapsed.TotalMilliseconds,
        ["historyItems"] = _context.HistoryItems,
        ["visitedMinIndex"] = NormalizeMinIndex(),
        ["visitedMaxIndex"] = NormalizeMaxIndex(),
        ["viewChanged"] = Volatile.Read(ref ViewChangedCount),
        ["viewChangedIntermediate"] = Volatile.Read(ref ViewChangedIntermediateCount),
        ["viewChangedFinal"] = Volatile.Read(ref ViewChangedFinalCount),
        ["renderCallbacks"] = Volatile.Read(ref RenderCallbacks),
        ["renderIntervalsOver33ms"] = Volatile.Read(ref RenderIntervalsOver33),
        ["graphMeasureCalls"] = Volatile.Read(ref GraphMeasureCalls),
        ["geometryAttempts"] = Volatile.Read(ref GeometryAttempts),
        ["geometryRebuilds"] = Volatile.Read(ref GeometryRebuilds),
        ["topologyConversions"] = Volatile.Read(ref TopologyConversions),
        ["layoutPublishes"] = Volatile.Read(ref PresentationPublishes),
        ["gitCommands"] = Volatile.Read(ref GitCommands)
    };

    private Dictionary<string, object?> CreateSummaryRecord(HistoryPerformanceStopReason reason)
    {
        var runtime = CaptureRuntimeDelta();
        return new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["type"] = "sessionSummary",
            ["elapsedMs"] = Elapsed.TotalMilliseconds,
            ["stopReason"] = reason.ToString(),
            ["historyItems"] = _context.HistoryItems,
            ["visitedMinIndex"] = NormalizeMinIndex(),
            ["visitedMaxIndex"] = NormalizeMaxIndex(),
            ["viewChanged"] = Volatile.Read(ref ViewChangedCount),
            ["viewChangedIntermediate"] = Volatile.Read(ref ViewChangedIntermediateCount),
            ["viewChangedFinal"] = Volatile.Read(ref ViewChangedFinalCount),
            ["maxViewChangedIntervalMs"] = HistoryAtomicDurationHistogram.TicksToMilliseconds(Volatile.Read(ref _maxViewChangedIntervalTicks)),
            ["loadMoreThresholdCount"] = Volatile.Read(ref LoadMoreThresholdCount),
            ["pageLoadsDuringCapture"] = Volatile.Read(ref PageLoadsStarted),
            ["rendering"] = _renderIntervals.Snapshot(),
            ["renderIntervalsOver16_7ms"] = Volatile.Read(ref RenderIntervalsOver16),
            ["renderIntervalsOver33ms"] = Volatile.Read(ref RenderIntervalsOver33),
            ["renderIntervalsOver50ms"] = Volatile.Read(ref RenderIntervalsOver50),
            ["renderIntervalsOver100ms"] = Volatile.Read(ref RenderIntervalsOver100),
            ["containerChanges"] = Volatile.Read(ref ContainerChanges),
            ["containerRealizations"] = Volatile.Read(ref ContainerRealizations),
            ["containerRecycles"] = Volatile.Read(ref ContainerRecycles),
                    ["selectionChanges"] = Volatile.Read(ref SelectionChanges),
            ["itemsSourceChanges"] = Volatile.Read(ref ItemsSourceChanges),
            ["historyCollectionResets"] = Volatile.Read(ref HistoryCollectionResets),
            ["historyRowsAppended"] = Volatile.Read(ref HistoryRowsAppended),
            ["graphControlsCreated"] = Volatile.Read(ref GraphControlsCreated),
            ["graphLoaded"] = Volatile.Read(ref GraphLoaded),
            ["graphUnloaded"] = Volatile.Read(ref GraphUnloaded),
            ["graphDataContextChanged"] = Volatile.Read(ref GraphDataContextChanged),
            ["graphPropertyChanged"] = Volatile.Read(ref GraphPropertyChanged),
            ["graphPresentationContextChanged"] = Volatile.Read(ref GraphPresentationContextChanged),
            ["graphMetricsChanged"] = Volatile.Read(ref GraphMetricsChanged),
            ["graphSizeChanged"] = Volatile.Read(ref GraphSizeChanged),
            ["graphThemeChanged"] = Volatile.Read(ref GraphThemeChanged),
            ["graphMeasure"] = _graphMeasure.Snapshot(),
            ["graphArrange"] = _graphArrange.Snapshot(),
            ["geometryAttempts"] = Volatile.Read(ref GeometryAttempts),
            ["geometrySkippedInvalidHeight"] = Volatile.Read(ref GeometrySkippedInvalidHeight),
            ["geometryCacheHits"] = Volatile.Read(ref GeometryCacheHits),
            ["geometryRebuilds"] = Volatile.Read(ref GeometryRebuilds),
            ["geometryRebuildDuration"] = _geometryRebuild.Snapshot(),
            ["geometryBuilder"] = _geometryBuilder.Snapshot(),
            ["geometryMaterialization"] = _geometryMaterialization.Snapshot(),
            ["geometrySegmentCount"] = Volatile.Read(ref GeometrySegmentCount),
            ["geometryRebuildReasons"] = GeometryReasonSnapshot(),
            ["laneCountHistogram"] = LaneCountHistogramSnapshot(),
            ["topologyConversions"] = Volatile.Read(ref TopologyConversions),
            ["topologyExact"] = Volatile.Read(ref TopologyExact),
            ["topologyFallback"] = Volatile.Read(ref TopologyFallback),
            ["topologyConverter"] = _topologyConverter.Snapshot(),
            ["topologyLaneTotal"] = Volatile.Read(ref TopologyLaneTotal),
            ["topologyIncomingTotal"] = Volatile.Read(ref TopologyIncomingTotal),
            ["topologyOutgoingTotal"] = Volatile.Read(ref TopologyOutgoingTotal),
            ["layoutInitializeCalls"] = Volatile.Read(ref LayoutInitializeCalls),
            ["layoutInitializeRowsExamined"] = Volatile.Read(ref LayoutInitializeRowsExamined),
            ["layoutInitializeDuration"] = _layoutInitialize.Snapshot(),
            ["layoutUpdateCalls"] = Volatile.Read(ref LayoutUpdateCalls),
            ["layoutResetCount"] = Volatile.Read(ref LayoutResetCount),
            ["layoutNewRowsExamined"] = Volatile.Read(ref LayoutNewRowsExamined),
            ["layoutChangedCalls"] = Volatile.Read(ref LayoutChangedCalls),
            ["layoutQueueCalls"] = Volatile.Read(ref LayoutQueueCalls),
            ["layoutApplyCalls"] = Volatile.Read(ref LayoutApplyCalls),
            ["layoutApplyDuration"] = _layoutApply.Snapshot(),
            ["presentationPublishes"] = Volatile.Read(ref PresentationPublishes),
            ["presentationPublishDuration"] = _presentationPublish.Snapshot(),
            ["presentationDeliveries"] = Volatile.Read(ref PresentationDeliveries),
            ["presentationDeliveryDuration"] = _presentationDelivery.Snapshot(),
            ["historyGlobalScans"] = Volatile.Read(ref HistoryGlobalScans),
            ["historyGlobalScanDuration"] = _historyGlobalScan.Snapshot(),
            ["historyIndexLookups"] = Volatile.Read(ref HistoryIndexLookups),
            ["historyIndexLookupDuration"] = _historyIndexLookup.Snapshot(),
            ["historyScanCategories"] = new Dictionary<string, object?>
            {
                ["historyGlobalScan"] = new { Calls = Volatile.Read(ref HistoryGlobalScans), ItemsExamined = Volatile.Read(ref HistoryItemsExamined), Duration = _historyGlobalScan.Snapshot() },
                ["historyIndexLookup"] = new { Calls = Volatile.Read(ref HistoryIndexLookups), Duration = _historyIndexLookup.Snapshot() },
                ["commitLookup"] = new { Calls = Volatile.Read(ref CommitLookups), ItemsExamined = Volatile.Read(ref CommitItemsExamined), Duration = _commitLookup.Snapshot() },
                ["parentLookup"] = new { Calls = Volatile.Read(ref ParentLookups), ItemsExamined = Volatile.Read(ref ParentItemsExamined), Duration = _parentLookup.Snapshot() },
                ["refLookup"] = new { Calls = Volatile.Read(ref RefLookups), ItemsExamined = Volatile.Read(ref RefItemsExamined), Duration = _refLookup.Snapshot() }
            },
            ["avatarControlsCreated"] = Volatile.Read(ref AvatarControlsCreated),
            ["avatarLoaded"] = Volatile.Read(ref AvatarLoaded),
            ["avatarUnloaded"] = Volatile.Read(ref AvatarUnloaded),
            ["avatarIdentityChanges"] = Volatile.Read(ref AvatarIdentityChanges),
            ["avatarRefreshCalls"] = Volatile.Read(ref AvatarRefreshCalls),
            ["avatarRequestsStarted"] = Volatile.Read(ref AvatarRequestsStarted),
            ["avatarRequestsCancelled"] = Volatile.Read(ref AvatarRequestsCancelled),
            ["avatarResultsApplied"] = Volatile.Read(ref AvatarResultsApplied),
            ["avatarStaleResultsIgnored"] = Volatile.Read(ref AvatarStaleResultsIgnored),
            ["avatarImmediateResolveCompletions"] = Volatile.Read(ref AvatarImmediateResolveCompletions),
            ["avatarAsyncResolveCompletions"] = Volatile.Read(ref AvatarAsyncResolveCompletions),
            ["avatarResolveDuration"] = _avatarResolve.Snapshot(),
            ["avatarCacheActivity"] = new Dictionary<string, long>
            {
                ["memoryHits"] = Volatile.Read(ref AvatarMemoryCacheHits),
                ["memoryMisses"] = Volatile.Read(ref AvatarMemoryCacheMisses),
                ["diskHits"] = Volatile.Read(ref AvatarDiskCacheHits),
                ["diskMisses"] = Volatile.Read(ref AvatarDiskCacheMisses),
                ["diskWrites"] = Volatile.Read(ref AvatarDiskCacheWrites),
                ["diskBytesWritten"] = Volatile.Read(ref AvatarDiskBytesWritten),
                ["remoteRequests"] = Volatile.Read(ref AvatarRemoteRequests),
                ["remoteBytesRead"] = Volatile.Read(ref AvatarRemoteBytesRead)
            },
            ["gitCommands"] = Volatile.Read(ref GitCommands),
            ["gitUserCommands"] = Volatile.Read(ref GitUserCommands),
            ["gitInternalCommands"] = Volatile.Read(ref GitInternalCommands),
            ["gitNetworkCommands"] = Volatile.Read(ref GitNetworkCommands),
            ["gitCommandCategories"] = new Dictionary<string, long>
            {
                ["status"] = Volatile.Read(ref GitStatusCommands),
                ["log"] = Volatile.Read(ref GitLogCommands),
                ["for-each-ref"] = Volatile.Read(ref GitForEachRefCommands),
                ["fetch"] = Volatile.Read(ref GitFetchCommands),
                ["pull"] = Volatile.Read(ref GitPullCommands),
                ["push"] = Volatile.Read(ref GitPushCommands),
                ["clone"] = Volatile.Read(ref GitCloneCommands),
                ["ls-remote"] = Volatile.Read(ref GitLsRemoteCommands),
                ["other"] = Volatile.Read(ref GitOtherCommands)
            },
            ["runtime"] = runtime,
            ["droppedDiagnosticRecords"] = Volatile.Read(ref DroppedDiagnosticRecords),
            ["droppedSlowEvents"] = Volatile.Read(ref DroppedSlowEvents),
            ["writerFailure"] = _writerFailure?.GetType().Name,
            ["captureIntegrity"] = CaptureIntegritySnapshot(),
            ["topSlowOperationCategories"] = TopSlowOperationCategories()
        };
    }

    private Dictionary<string, object?> CreateTextMetrics()
    {
        var rendering = _renderIntervals.Snapshot();
        var graphMeasure = _graphMeasure.Snapshot();
        var geometry = _geometryRebuild.Snapshot();
        var topology = _topologyConverter.Snapshot();
        var globalScan = _historyGlobalScan.Snapshot();
        var runtime = CaptureRuntimeDelta();
        return new Dictionary<string, object?>
        {
            ["rendering"] = rendering,
            ["graphMeasure"] = graphMeasure,
            ["geometry"] = geometry,
            ["topology"] = topology,
            ["globalScan"] = globalScan,
            ["runtime"] = runtime
        };
    }

    private string CreateTextSummary(HistoryPerformanceStopReason reason)
    {
        var metrics = CreateTextMetrics();
        var rendering = (HistoryDurationStatistics)metrics["rendering"]!;
        var graphMeasure = (HistoryDurationStatistics)metrics["graphMeasure"]!;
        var geometry = (HistoryDurationStatistics)metrics["geometry"]!;
        var topology = (HistoryDurationStatistics)metrics["topology"]!;
        var globalScan = (HistoryDurationStatistics)metrics["globalScan"]!;
        var runtime = (Dictionary<string, object?>)metrics["runtime"]!;
        var reasons = GeometryReasonSnapshot();
        var builder = new StringBuilder();

        builder.AppendLine("CSharpGit History Performance Capture");
        builder.AppendLine();
        builder.AppendLine($"Duration:                         {Elapsed.TotalSeconds:F1} s");
        builder.AppendLine($"Stop reason:                      {reason}");
        builder.AppendLine();
        builder.AppendLine($"History items:                    {_context.HistoryItems}");
        builder.AppendLine($"Visited indexes:                  {FormatVisitedRange()}");
        builder.AppendLine($"ViewChanged events:               {Volatile.Read(ref ViewChangedCount)}");
        builder.AppendLine($"Intermediate / final:             {Volatile.Read(ref ViewChangedIntermediateCount)} / {Volatile.Read(ref ViewChangedFinalCount)}");
        builder.AppendLine($"Page loads:                       {Volatile.Read(ref PageLoadsStarted)}");
        builder.AppendLine();
        builder.AppendLine("Rendering");
        builder.AppendLine("---------");
        builder.AppendLine($"Callbacks:                        {Volatile.Read(ref RenderCallbacks)}");
        builder.AppendLine($"Interval p50:                     {rendering.P50Ms:F2} ms");
        builder.AppendLine($"Interval p95:                     {rendering.P95Ms:F2} ms");
        builder.AppendLine($"Interval p99:                     {rendering.P99Ms:F2} ms");
        builder.AppendLine($"Max interval:                     {rendering.MaxMs:F2} ms");
        builder.AppendLine($"Intervals >33ms:                  {Volatile.Read(ref RenderIntervalsOver33)}");
        builder.AppendLine($"Intervals >50ms:                  {Volatile.Read(ref RenderIntervalsOver50)}");
        builder.AppendLine($"Intervals >100ms:                 {Volatile.Read(ref RenderIntervalsOver100)}");
        builder.AppendLine();
        builder.AppendLine("Virtualization");
        builder.AppendLine("--------------");
        builder.AppendLine($"Container changes:                {Volatile.Read(ref ContainerChanges)}");
        builder.AppendLine($"Realization events:               {Volatile.Read(ref ContainerRealizations)}");
        builder.AppendLine($"Recycle events:                   {Volatile.Read(ref ContainerRecycles)}");
        builder.AppendLine();
        builder.AppendLine("Commit graph");
        builder.AppendLine("------------");
        builder.AppendLine($"Controls created:                 {Volatile.Read(ref GraphControlsCreated)}");
        builder.AppendLine($"Loaded:                           {Volatile.Read(ref GraphLoaded)}");
        builder.AppendLine($"Unloaded:                         {Volatile.Read(ref GraphUnloaded)}");
        builder.AppendLine($"Measure calls:                    {graphMeasure.Count}");
        builder.AppendLine($"Measure p95:                      {graphMeasure.P95Ms:F2} ms");
        builder.AppendLine($"Geometry attempts:                {Volatile.Read(ref GeometryAttempts)}");
        builder.AppendLine($"Geometry cache hits:              {Volatile.Read(ref GeometryCacheHits)}");
        builder.AppendLine($"Geometry rebuilds:                {Volatile.Read(ref GeometryRebuilds)}");
        builder.AppendLine($"Geometry rebuild p95:             {geometry.P95Ms:F2} ms");
        builder.AppendLine();
        builder.AppendLine("Geometry rebuild reasons:");
        foreach (var pair in reasons)
            builder.AppendLine($"{pair.Key,-32} {pair.Value}");
        builder.AppendLine("Lane count histogram (geometry rebuilds):");
        foreach (var pair in LaneCountHistogramSnapshot())
            builder.AppendLine($"{pair.Key,-32} {pair.Value}");
        builder.AppendLine();
        builder.AppendLine("Topology converter");
        builder.AppendLine("------------------");
        builder.AppendLine($"Calls:                            {Volatile.Read(ref TopologyConversions)}");
        builder.AppendLine($"Total:                            {topology.TotalMs:F2} ms");
        builder.AppendLine($"p95:                              {topology.P95Ms:F2} ms");
        builder.AppendLine($"Max:                              {topology.MaxMs:F2} ms");
        builder.AppendLine();
        builder.AppendLine("Graph layout");
        builder.AppendLine("------------");
        builder.AppendLine($"InitializeGraphLayout:            {Volatile.Read(ref LayoutInitializeCalls)}");
        builder.AppendLine($"UpdateGraphLayout:                {Volatile.Read(ref LayoutUpdateCalls)}");
        builder.AppendLine($"Presentation publishes:           {Volatile.Read(ref PresentationPublishes)}");
        builder.AppendLine($"Presentation deliveries:          {Volatile.Read(ref PresentationDeliveries)}");
        builder.AppendLine();
        builder.AppendLine("History scans");
        builder.AppendLine("-------------");
        builder.AppendLine($"Global scan calls:                {Volatile.Read(ref HistoryGlobalScans)}");
        builder.AppendLine($"Global scan total:                {globalScan.TotalMs:F2} ms");
        builder.AppendLine($"Global scan p95:                  {globalScan.P95Ms:F2} ms");
        builder.AppendLine($"Global scan max:                  {globalScan.MaxMs:F2} ms");
        builder.AppendLine($"Global scan items examined:       {Volatile.Read(ref HistoryItemsExamined)}");
        builder.AppendLine($"Index lookup calls:               {Volatile.Read(ref HistoryIndexLookups)}");
        builder.AppendLine($"Commit lookup calls/items:        {Volatile.Read(ref CommitLookups)} / {Volatile.Read(ref CommitItemsExamined)}");
        builder.AppendLine($"Parent lookup calls/items:        {Volatile.Read(ref ParentLookups)} / {Volatile.Read(ref ParentItemsExamined)}");
        builder.AppendLine($"Ref lookup calls/items:           {Volatile.Read(ref RefLookups)} / {Volatile.Read(ref RefItemsExamined)}");
        builder.AppendLine();
        builder.AppendLine("Avatars");
        builder.AppendLine("-------");
        builder.AppendLine($"Refresh calls:                    {Volatile.Read(ref AvatarRefreshCalls)}");
        builder.AppendLine($"Resolve requests:                 {Volatile.Read(ref AvatarRequestsStarted)}");
        builder.AppendLine($"Cancelled:                        {Volatile.Read(ref AvatarRequestsCancelled)}");
        builder.AppendLine($"Async completions:                {Volatile.Read(ref AvatarAsyncResolveCompletions)}");
        builder.AppendLine($"Memory cache hits/misses:         {Volatile.Read(ref AvatarMemoryCacheHits)} / {Volatile.Read(ref AvatarMemoryCacheMisses)}");
        builder.AppendLine($"Disk cache hits/misses:           {Volatile.Read(ref AvatarDiskCacheHits)} / {Volatile.Read(ref AvatarDiskCacheMisses)}");
        builder.AppendLine($"Remote requests:                  {Volatile.Read(ref AvatarRemoteRequests)}");
        builder.AppendLine();
        builder.AppendLine("Owned disk activity");
        builder.AppendLine("-------------------");
        builder.AppendLine($"Avatar cache writes:              {Volatile.Read(ref AvatarDiskCacheWrites)}");
        builder.AppendLine($"Avatar cache bytes written:       {FormatBytes(Volatile.Read(ref AvatarDiskBytesWritten))}");
        builder.AppendLine($"Avatar remote bytes read:         {FormatBytes(Volatile.Read(ref AvatarRemoteBytesRead))}");
        builder.AppendLine();
        builder.AppendLine("Runtime");
        builder.AppendLine("-------");
        builder.AppendLine($"Allocated:                        {FormatBytes((long)runtime["allocatedBytes"]!)}");
        builder.AppendLine($"Gen0 GC:                          {runtime["gen0Collections"]}");
        builder.AppendLine($"Gen1 GC:                          {runtime["gen1Collections"]}");
        builder.AppendLine($"Gen2 GC:                          {runtime["gen2Collections"]}");
        builder.AppendLine($"Process CPU:                      {(double)runtime["processCpuMs"]!:F1} ms");
        builder.AppendLine($"CPU / wall:                       {(double)runtime["cpuToWallRatio"]!:F3}");
        builder.AppendLine();
        builder.AppendLine("Git");
        builder.AppendLine("---");
        builder.AppendLine($"Commands:                         {Volatile.Read(ref GitCommands)}");
        builder.AppendLine($"User / internal:                  {Volatile.Read(ref GitUserCommands)} / {Volatile.Read(ref GitInternalCommands)}");
        builder.AppendLine($"Network commands:                 {Volatile.Read(ref GitNetworkCommands)}");
        builder.AppendLine($"status/log/for-each-ref:          {Volatile.Read(ref GitStatusCommands)} / {Volatile.Read(ref GitLogCommands)} / {Volatile.Read(ref GitForEachRefCommands)}");
        builder.AppendLine($"fetch/pull/push/clone/ls-remote:  {Volatile.Read(ref GitFetchCommands)} / {Volatile.Read(ref GitPullCommands)} / {Volatile.Read(ref GitPushCommands)} / {Volatile.Read(ref GitCloneCommands)} / {Volatile.Read(ref GitLsRemoteCommands)}");
        builder.AppendLine();
        builder.AppendLine("Capture integrity");
        builder.AppendLine("-----------------");
        builder.AppendLine($"History mutated:                  {YesNo(HistoryMutatedDuringCapture)}");
        builder.AppendLine($"Page load:                        {YesNo(PageLoadedDuringCapture)}");
        builder.AppendLine($"Repository changed:               {YesNo(RepositoryChangedDuringCapture)}");
        builder.AppendLine($"Window resize:                    {YesNo(WindowResizedDuringCapture)}");
        builder.AppendLine($"Graph layout published:           {YesNo(GraphLayoutPublishedDuringCapture)}");
        builder.AppendLine($"Git commands:                     {YesNo(GitCommandExecutedDuringCapture)}");
        builder.AppendLine($"Settings changed:                 {YesNo(SettingsChangedDuringCapture)}");
        builder.AppendLine($"Avatar online request:            {YesNo(AvatarOnlineRequestDuringCapture)}");
        builder.AppendLine();
        builder.AppendLine($"Dropped diagnostic records:       {Volatile.Read(ref DroppedDiagnosticRecords)}");
        builder.AppendLine($"Dropped slow events:              {Volatile.Read(ref DroppedSlowEvents)}");
        builder.AppendLine();
        builder.AppendLine("Top slow-operation categories");
        builder.AppendLine("------------------------------");
        foreach (var item in TopSlowOperationCategories())
            builder.AppendLine($"{item.Name,-32} {item.TotalMs:F2} ms");
        if (_writerFailure is not null)
            builder.AppendLine($"Writer failure:                    {_writerFailure.GetType().Name}");
        return builder.ToString();
    }

    private Dictionary<string, long> GeometryReasonSnapshot()
    {
        var result = new Dictionary<string, long>();
        foreach (var reason in Enum.GetValues<HistoryGeometryUpdateReason>())
            result[reason.ToString()] = Volatile.Read(ref _geometryRebuildReasons[(int)reason]);
        return result;
    }

    private Dictionary<string, long> LaneCountHistogramSnapshot()
    {
        var result = new Dictionary<string, long>();
        for (var laneCount = 0; laneCount < _laneCountHistogram.Length; laneCount++)
        {
            var count = Volatile.Read(ref _laneCountHistogram[laneCount]);
            if (count == 0) continue;
            result[laneCount == _laneCountHistogram.Length - 1 ? "64+" : laneCount.ToString()] = count;
        }
        return result;
    }

    private IReadOnlyList<SlowCategorySummary> TopSlowOperationCategories()
    {
        var categories = new[]
        {
            new SlowCategorySummary("HistoryGlobalScan", _historyGlobalScan.Snapshot().TotalMs),
            new SlowCategorySummary("HistoryIndexLookup", _historyIndexLookup.Snapshot().TotalMs),
            new SlowCategorySummary("CommitLookup", _commitLookup.Snapshot().TotalMs),
            new SlowCategorySummary("ParentLookup", _parentLookup.Snapshot().TotalMs),
            new SlowCategorySummary("RefLookup", _refLookup.Snapshot().TotalMs),
            new SlowCategorySummary("CommitGraph.Measure", _graphMeasure.Snapshot().TotalMs),
            new SlowCategorySummary("CommitGraph.Arrange", _graphArrange.Snapshot().TotalMs),
            new SlowCategorySummary("CommitGraph.GeometryRebuild", _geometryRebuild.Snapshot().TotalMs),
            new SlowCategorySummary("CommitGraph.GeometryBuilder", _geometryBuilder.Snapshot().TotalMs),
            new SlowCategorySummary("CommitGraph.XamlMaterialization", _geometryMaterialization.Snapshot().TotalMs),
            new SlowCategorySummary("TopologyConverter", _topologyConverter.Snapshot().TotalMs),
            new SlowCategorySummary("GraphLayout.Initialize", _layoutInitialize.Snapshot().TotalMs),
            new SlowCategorySummary("GraphLayout.Update", _layoutUpdate.Snapshot().TotalMs),
            new SlowCategorySummary("GraphLayout.Apply", _layoutApply.Snapshot().TotalMs),
            new SlowCategorySummary("Avatar.Resolve", _avatarResolve.Snapshot().TotalMs)
        };
        return categories
            .Where(item => item.TotalMs > 0)
            .OrderByDescending(item => item.TotalMs)
            .Take(5)
            .ToArray();
    }

    private sealed record SlowCategorySummary(string Name, double TotalMs);

    private Dictionary<string, object?> CaptureRuntimeDelta()
    {
        _process.Refresh();
        var wallMs = Math.Max(0.001, Elapsed.TotalMilliseconds);
        var processCpuMs = Math.Max(0, (_process.TotalProcessorTime - _processorBaseline).TotalMilliseconds);
        return new Dictionary<string, object?>
        {
            ["allocatedBytes"] = Math.Max(0, GC.GetTotalAllocatedBytes(false) - _allocatedBaseline),
            ["gen0Collections"] = GC.CollectionCount(0) - _gen0Baseline,
            ["gen1Collections"] = GC.CollectionCount(1) - _gen1Baseline,
            ["gen2Collections"] = GC.CollectionCount(2) - _gen2Baseline,
            ["heapDeltaBytes"] = GC.GetGCMemoryInfo().HeapSizeBytes - _heapBaseline,
            ["workingSetDeltaBytes"] = _process.WorkingSet64 - _workingSetBaseline,
            ["wallTimeMs"] = wallMs,
            ["processCpuMs"] = processCpuMs,
            ["cpuToWallRatio"] = processCpuMs / wallMs,
            ["normalizedCpuPercent"] = processCpuMs / wallMs / Math.Max(1, Environment.ProcessorCount) * 100d
        };
    }

    private Dictionary<string, bool> CaptureIntegritySnapshot() => new()
    {
        ["historyMutatedDuringCapture"] = Volatile.Read(ref HistoryMutatedDuringCapture) != 0,
        ["pageLoadedDuringCapture"] = Volatile.Read(ref PageLoadedDuringCapture) != 0,
        ["repositoryChanged"] = Volatile.Read(ref RepositoryChangedDuringCapture) != 0,
        ["windowResized"] = Volatile.Read(ref WindowResizedDuringCapture) != 0,
        ["graphLayoutPublished"] = Volatile.Read(ref GraphLayoutPublishedDuringCapture) != 0,
        ["gitCommandExecuted"] = Volatile.Read(ref GitCommandExecutedDuringCapture) != 0,
        ["settingsChanged"] = Volatile.Read(ref SettingsChangedDuringCapture) != 0,
        ["avatarOnlineRequestExecuted"] = Volatile.Read(ref AvatarOnlineRequestDuringCapture) != 0
    };

    private async Task WriterLoopAsync()
    {
        try
        {
            await using var stream = new FileStream(
                JsonlPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            _writerReady.TrySetResult();

            await foreach (var line in _writerChannel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await writer.WriteLineAsync(line).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _writerFailure = exception;
            _writerReady.TrySetException(exception);
            _writerChannel.Writer.TryComplete(exception);
        }
    }

    private async Task WriteRequiredAsync(string line)
    {
        try
        {
            await _writerChannel.Writer.WriteAsync(line).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Increment(ref DroppedDiagnosticRecords);
        }
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private int? NormalizeMinIndex()
    {
        var value = Volatile.Read(ref VisitedMinIndex);
        return value == int.MaxValue ? null : value;
    }

    private int? NormalizeMaxIndex()
    {
        var value = Volatile.Read(ref VisitedMaxIndex);
        return value == int.MinValue ? null : value;
    }

    private string FormatVisitedRange()
    {
        var min = NormalizeMinIndex();
        var max = NormalizeMaxIndex();
        return min is null || max is null ? "n/a" : $"{min}..{max}";
    }

    private static string FormatBytes(long value)
    {
        var megabytes = value / 1024d / 1024d;
        return $"{megabytes:F1} MB";
    }

    private static string YesNo(int value) => Volatile.Read(ref value) == 0 ? "no" : "yes";

    private static string GetGitVerb(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var value = arguments[index];
            if (string.Equals(value, "-c", StringComparison.Ordinal) && index + 1 < arguments.Count)
            {
                index++;
                continue;
            }

            if (value.StartsWith("-", StringComparison.Ordinal))
                continue;

            return value;
        }

        return "unknown";
    }

    private static bool IsNetworkVerb(string verb) =>
        verb is "fetch" or "pull" or "push" or "clone" or "ls-remote";

    private static void UpdateMin(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value < current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current) return;
            current = observed;
        }
    }

    private static void UpdateMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current) return;
            current = observed;
        }
    }

    private static void UpdateMax(ref long target, long value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current) return;
            current = observed;
        }
    }
}

internal static class HistoryPerformanceDiagnostics
{
    private static readonly object SessionGate = new();
    private static HistoryPerformanceSession? _activeSession;
    private static IGitCommandActivitySource? _gitActivitySource;
    private static IAuthorAvatarDiagnosticSource? _avatarDiagnosticSource;

    internal static HistoryPerformanceSession? ActiveSession => Volatile.Read(ref _activeSession);
    internal static bool IsCaptureActive => ActiveSession is not null;

    internal static string DiagnosticsDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
            return Path.Combine(root, "CSharpGit", "Diagnostics", "HistoryPerformance");
        }
    }

    internal static long TimestampIfActive() =>
        IsCaptureActive ? Stopwatch.GetTimestamp() : 0;

    internal static async Task<bool> StartAsync(
        HistoryPerformanceStartContext context,
        IGitCommandActivitySource gitActivitySource,
        IAuthorAvatarService? authorAvatarService = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(gitActivitySource);

        HistoryPerformanceSession session;
        lock (SessionGate)
        {
            if (_activeSession is not null) return false;
            session = new HistoryPerformanceSession(context, DiagnosticsDirectory);
            Volatile.Write(ref _activeSession, session);
        }

        _gitActivitySource = gitActivitySource;
        gitActivitySource.Changed += GitActivitySource_Changed;
        if (authorAvatarService is IAuthorAvatarDiagnosticSource avatarDiagnosticSource)
        {
            _avatarDiagnosticSource = avatarDiagnosticSource;
            avatarDiagnosticSource.DiagnosticActivity += AvatarDiagnosticSource_DiagnosticActivity;
        }
        CompositionTarget.Rendering += CompositionTarget_Rendering;

        try
        {
            await session.StartAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            gitActivitySource.Changed -= GitActivitySource_Changed;
            _gitActivitySource = null;
            if (_avatarDiagnosticSource is { } avatarSource)
                avatarSource.DiagnosticActivity -= AvatarDiagnosticSource_DiagnosticActivity;
            _avatarDiagnosticSource = null;
            lock (SessionGate)
            {
                if (ReferenceEquals(_activeSession, session))
                    Volatile.Write(ref _activeSession, null);
            }
            throw;
        }
    }

    internal static async Task<HistoryPerformanceCaptureFiles?> StopAsync(
        HistoryPerformanceStopReason reason)
    {
        HistoryPerformanceSession? session;
        lock (SessionGate)
        {
            session = _activeSession;
            if (session is null) return null;
            Volatile.Write(ref _activeSession, null);
        }

        CompositionTarget.Rendering -= CompositionTarget_Rendering;
        if (_gitActivitySource is { } source)
            source.Changed -= GitActivitySource_Changed;
        _gitActivitySource = null;
        if (_avatarDiagnosticSource is { } avatarSource)
            avatarSource.DiagnosticActivity -= AvatarDiagnosticSource_DiagnosticActivity;
        _avatarDiagnosticSource = null;

        return await session.StopAsync(reason).ConfigureAwait(false);
    }

    private static void CompositionTarget_Rendering(object? sender, object args) =>
        ActiveSession?.RecordRenderingCallback(Stopwatch.GetTimestamp());

    private static void AvatarDiagnosticSource_DiagnosticActivity(
        object? sender,
        AuthorAvatarDiagnosticActivityEventArgs args) =>
        ActiveSession?.RecordAvatarDiagnosticActivity(args);

    private static void GitActivitySource_Changed(
        object? sender,
        GitCommandActivityChangedEventArgs args)
    {
        if (args.ChangeKind != GitCommandActivityChangeKind.Started)
            return;
        if (args.Activity is { } activity)
            ActiveSession?.RecordGitCommand(activity);
    }
}
