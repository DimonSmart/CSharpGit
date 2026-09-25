using System.Collections.Specialized;
using CSharpGit.Presentation.Diagnostics;

namespace CSharpGit.Presentation.Controls;

internal readonly record struct HistoryRenderDiagnosticSnapshot(
    long GraphControlsCreated,
    long GeometryUpdateAttempts,
    long GeometryRebuilds,
    long AuthorAvatarsCreated,
    long ExplicitAvatarConfigurations,
    long RecursiveGraphLayoutTraversals,
    long RecursiveAvatarConfigurationTraversals);

internal static class HistoryRenderDiagnostics
{
    private static int _enabled =
        string.Equals(
            Environment.GetEnvironmentVariable("CSHARPGIT_HISTORY_RENDER_DIAGNOSTICS"),
            "1",
            StringComparison.Ordinal)
            ? 1
            : 0;

    private static long _graphControlsCreated;
    private static long _geometryUpdateAttempts;
    private static long _geometryRebuilds;
    private static long _authorAvatarsCreated;
    private static long _explicitAvatarConfigurations;
    private static long _recursiveGraphLayoutTraversals;
    private static long _recursiveAvatarConfigurationTraversals;

    private static bool Enabled => Volatile.Read(ref _enabled) != 0;
    private static HistoryPerformanceSession? Session => HistoryPerformanceDiagnostics.ActiveSession;

    internal static void EnableForCheck() => Volatile.Write(ref _enabled, 1);

    internal static void Reset()
    {
        Interlocked.Exchange(ref _graphControlsCreated, 0);
        Interlocked.Exchange(ref _geometryUpdateAttempts, 0);
        Interlocked.Exchange(ref _geometryRebuilds, 0);
        Interlocked.Exchange(ref _authorAvatarsCreated, 0);
        Interlocked.Exchange(ref _explicitAvatarConfigurations, 0);
        Interlocked.Exchange(ref _recursiveGraphLayoutTraversals, 0);
        Interlocked.Exchange(ref _recursiveAvatarConfigurationTraversals, 0);
    }

    internal static HistoryRenderDiagnosticSnapshot Snapshot() => new(
        Volatile.Read(ref _graphControlsCreated),
        Volatile.Read(ref _geometryUpdateAttempts),
        Volatile.Read(ref _geometryRebuilds),
        Volatile.Read(ref _authorAvatarsCreated),
        Volatile.Read(ref _explicitAvatarConfigurations),
        Volatile.Read(ref _recursiveGraphLayoutTraversals),
        Volatile.Read(ref _recursiveAvatarConfigurationTraversals));

    internal static long TimestampIfPerformanceCaptureActive() =>
        HistoryPerformanceDiagnostics.TimestampIfActive();

    internal static void GraphControlCreated()
    {
        if (Enabled) Interlocked.Increment(ref _graphControlsCreated);
        if (Session is { } session) Interlocked.Increment(ref session.GraphControlsCreated);
    }

    internal static void GraphLoaded()
    {
        if (Session is { } session) Interlocked.Increment(ref session.GraphLoaded);
    }

    internal static void GraphUnloaded()
    {
        if (Session is { } session) Interlocked.Increment(ref session.GraphUnloaded);
    }

    internal static void GraphDataContextChanged()
    {
        if (Session is { } session) Interlocked.Increment(ref session.GraphDataContextChanged);
    }

    internal static void GraphChanged()
    {
        if (Session is { } session) Interlocked.Increment(ref session.GraphPropertyChanged);
    }

    internal static void GraphPresentationContextChanged()
    {
        if (Session is { } session) Interlocked.Increment(ref session.GraphPresentationContextChanged);
    }

    internal static void GraphMetricsChanged()
    {
        if (Session is { } session) Interlocked.Increment(ref session.GraphMetricsChanged);
    }

    internal static void GraphSizeChanged()
    {
        if (Session is { } session) Interlocked.Increment(ref session.GraphSizeChanged);
    }

    internal static void GraphThemeChanged()
    {
        if (Session is { } session) Interlocked.Increment(ref session.GraphThemeChanged);
    }

    internal static void GraphMeasureCompleted(long startedAt)
    {
        Session?.RecordGraphMeasure(startedAt);
    }

    internal static void GraphArrangeCompleted(long startedAt)
    {
        Session?.RecordGraphArrange(startedAt);
    }

    internal static void GeometryUpdateAttempted(HistoryGeometryUpdateReason reason = HistoryGeometryUpdateReason.Unknown)
    {
        if (Enabled) Interlocked.Increment(ref _geometryUpdateAttempts);
        if (Session is { } session) Interlocked.Increment(ref session.GeometryAttempts);
    }

    internal static void GeometrySkippedInvalidHeight()
    {
        if (Session is { } session) Interlocked.Increment(ref session.GeometrySkippedInvalidHeight);
    }

    internal static void GeometryCacheHit()
    {
        if (Session is { } session) Interlocked.Increment(ref session.GeometryCacheHits);
    }

    internal static void GeometryRebuilt(
        HistoryGeometryUpdateReason reason = HistoryGeometryUpdateReason.Unknown,
        long startedAt = 0,
        int laneCount = 0,
        int segmentCount = 0)
    {
        if (Enabled) Interlocked.Increment(ref _geometryRebuilds);
        Session?.RecordGeometryRebuild(reason, startedAt, laneCount, segmentCount);
    }

    internal static void GeometryBuilderCompleted(long startedAt) =>
        Session?.RecordGeometryBuilder(startedAt);

    internal static void GeometryMaterializationCompleted(long startedAt) =>
        Session?.RecordGeometryMaterialization(startedAt);

    internal static void TopologyConverted(
        long startedAt,
        bool exact,
        int laneCount,
        int incomingCount,
        int outgoingCount) =>
        Session?.RecordTopologyConversion(
            startedAt,
            exact,
            laneCount,
            incomingCount,
            outgoingCount);

    internal static void GraphLayoutInitialized(long startedAt, int rowsExamined) =>
        Session?.RecordLayoutInitialize(startedAt, rowsExamined);

    internal static void GraphLayoutUpdated(
        long startedAt,
        bool reset,
        int newRowsExamined,
        bool changed) =>
        Session?.RecordLayoutUpdate(startedAt, reset, newRowsExamined, changed);

    internal static void GraphLayoutQueued()
    {
        if (Session is { } session) Interlocked.Increment(ref session.LayoutQueueCalls);
    }

    internal static void GraphLayoutApplied(long startedAt) =>
        Session?.RecordLayoutApply(startedAt);

    internal static void PresentationPublished(long startedAt) =>
        Session?.RecordPresentationPublish(startedAt);

    internal static void PresentationDelivered(long startedAt) =>
        Session?.RecordPresentationDelivery(startedAt);

    internal static void HistoryViewChanged(long timestamp) =>
        Session?.RecordViewChanged(timestamp);

    internal static void LoadMoreThresholdReached()
    {
        if (Session is { } session) Interlocked.Increment(ref session.LoadMoreThresholdCount);
    }

    internal static void PageLoadStarted()
    {
        if (Session is not { } session) return;
        Interlocked.Increment(ref session.PageLoadsStarted);
        Volatile.Write(ref session.PageLoadedDuringCapture, 1);
    }

    internal static void ContainerContentChanged(int itemIndex, bool inRecycleQueue) =>
        Session?.RecordVisitedIndex(itemIndex, inRecycleQueue);

    internal static void SelectionChanged()
    {
        if (Session is { } session) Interlocked.Increment(ref session.SelectionChanges);
    }

    internal static void ItemsSourceChanged()
    {
        if (Session is { } session) Interlocked.Increment(ref session.ItemsSourceChanges);
    }

    internal static void HistoryCollectionChanged(
        NotifyCollectionChangedAction action,
        int appendedRows)
    {
        if (Session is not { } session) return;
        Volatile.Write(ref session.HistoryMutatedDuringCapture, 1);
        if (action == NotifyCollectionChangedAction.Reset)
            Interlocked.Increment(ref session.HistoryCollectionResets);
        if (appendedRows > 0)
            Interlocked.Add(ref session.HistoryRowsAppended, appendedRows);
    }

    internal static void WindowResized()
    {
        if (Session is { } session) Volatile.Write(ref session.WindowResizedDuringCapture, 1);
    }

    internal static void RepositoryChanged()
    {
        if (Session is { } session) Volatile.Write(ref session.RepositoryChangedDuringCapture, 1);
    }

    internal static void SettingsChanged()
    {
        if (Session is { } session) Volatile.Write(ref session.SettingsChangedDuringCapture, 1);
    }

    internal static void AuthorAvatarCreated()
    {
        if (Enabled) Interlocked.Increment(ref _authorAvatarsCreated);
        if (Session is { } session) Interlocked.Increment(ref session.AvatarControlsCreated);
    }

    internal static void AvatarLoaded()
    {
        if (Session is { } session) Interlocked.Increment(ref session.AvatarLoaded);
    }

    internal static void AvatarUnloaded()
    {
        if (Session is { } session) Interlocked.Increment(ref session.AvatarUnloaded);
    }

    internal static void AvatarIdentityChanged()
    {
        if (Session is { } session) Interlocked.Increment(ref session.AvatarIdentityChanges);
    }

    internal static void AvatarRefresh()
    {
        if (Session is { } session) Interlocked.Increment(ref session.AvatarRefreshCalls);
    }

    internal static void AvatarRequestStarted()
    {
        if (Session is not { } session) return;
        Interlocked.Increment(ref session.AvatarRequestsStarted);
        Volatile.Write(ref session.AvatarOnlineRequestDuringCapture, 1);
    }

    internal static void AvatarRequestCancelled()
    {
        if (Session is { } session) Interlocked.Increment(ref session.AvatarRequestsCancelled);
    }

    internal static void AvatarResultApplied()
    {
        if (Session is { } session) Interlocked.Increment(ref session.AvatarResultsApplied);
    }

    internal static void AvatarStaleResultIgnored()
    {
        if (Session is { } session) Interlocked.Increment(ref session.AvatarStaleResultsIgnored);
    }

    internal static void AvatarResolveCompleted(long startedAt, bool completedSynchronously) =>
        Session?.RecordAvatarResolve(startedAt, completedSynchronously);

    internal static void HistoryGlobalScanCompleted(long startedAt, int? historyItems = null) =>
        Session?.RecordHistoryGlobalScan(startedAt, historyItems);

    internal static void HistoryIndexLookupCompleted(long startedAt, int? historyItems = null) =>
        Session?.RecordHistoryIndexLookup(startedAt, historyItems);

    internal static void ExplicitAvatarConfigured()
    {
        if (Enabled) Interlocked.Increment(ref _explicitAvatarConfigurations);
    }

    internal static void RecursiveGraphLayoutTraversal()
    {
        if (Enabled) Interlocked.Increment(ref _recursiveGraphLayoutTraversals);
    }

    internal static void RecursiveAvatarConfigurationTraversal()
    {
        if (Enabled) Interlocked.Increment(ref _recursiveAvatarConfigurationTraversals);
    }
}
