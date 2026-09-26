using CSharpGit.Presentation.Controls.CommitGraph;

namespace CSharpGit.Desktop.Tests;

public sealed class CommitGraphGeometryKeyTests
{
    [Fact]
    public void DifferentInstancesWithEqualTopologyProduceEqualKey()
    {
        var first = CreateGraph();
        var second = CreateGraph();

        var firstKey = CreateKey(first);
        var secondKey = CreateKey(second);

        Assert.NotSame(first, second);
        Assert.Equal(firstKey, secondKey);
        Assert.Equal(firstKey.GetHashCode(), secondKey.GetHashCode());
    }

    [Fact]
    public void TopologyFieldsAndSegmentOrderParticipateInIdentity()
    {
        var baseline = CreateKey(CreateGraph());
        Assert.NotEqual(baseline, CreateKey(CreateGraph(nodeLane: 1)));
        Assert.NotEqual(baseline, CreateKey(CreateGraph(nodeTrackId: 7)));
        Assert.NotEqual(
            baseline,
            CreateKey(CreateGraph(incoming:
            [
                new CommitGraphSegment(0, 0, 0),
                new CommitGraphSegment(1, 0, 9),
            ])));
        Assert.NotEqual(
            baseline,
            CreateKey(CreateGraph(incoming:
            [
                new CommitGraphSegment(1, 0, 1),
                new CommitGraphSegment(0, 0, 0),
            ])));
    }

    [Fact]
    public void SegmentFieldChangesParticipateInIdentity()
    {
        var baseline = CreateKey(CreateGraph(incoming: [new CommitGraphSegment(1, 0, 1)]));

        Assert.NotEqual(
            baseline,
            CreateKey(CreateGraph(incoming: [new CommitGraphSegment(0, 0, 1)])));
        Assert.NotEqual(
            baseline,
            CreateKey(CreateGraph(incoming: [new CommitGraphSegment(1, 1, 1)])));
        Assert.NotEqual(
            baseline,
            CreateKey(CreateGraph(incoming: [new CommitGraphSegment(1, 0, 2)])));
    }

    [Fact]
    public void PresentationOnlyLineThicknessDoesNotChangeGeometryIdentity()
    {
        var graph = CreateGraph();
        var first = CreateKey(graph, CommitGraphMetrics.Default);
        var second = CreateKey(
            graph,
            CommitGraphMetrics.Default with { LineThickness = 9 });

        Assert.Equal(first, second);
    }

    [Fact]
    public void GeometryMetricsChangeIdentity()
    {
        var graph = CreateGraph();
        var baseline = CreateKey(graph, CommitGraphMetrics.Default);

        Assert.NotEqual(
            baseline,
            CreateKey(graph, CommitGraphMetrics.Default with { LaneWidth = 12 }));
        Assert.NotEqual(
            baseline,
            CreateKey(graph, CommitGraphMetrics.Default with { HorizontalMargin = 10 }));
        Assert.NotEqual(
            baseline,
            CreateKey(graph, CommitGraphMetrics.Default with { NodeRadius = 5 }));
    }

    [Fact]
    public void HeightEpsilonReusesCurrentEffectiveKey()
    {
        var graph = CreateGraph();
        var baseline = CreateKey(graph, CommitGraphMetrics.Default, 24);
        var insignificant = CreateKey(graph, CommitGraphMetrics.Default, 24.001);
        var significant = CreateKey(graph, CommitGraphMetrics.Default, 24.02);

        Assert.Equal(baseline, insignificant.ReuseHeightIfEquivalent(baseline));
        Assert.NotEqual(baseline, significant.ReuseHeightIfEquivalent(baseline));
    }

    private static CommitGraphGeometryKey CreateKey(
        CommitGraphRowVisual graph,
        CommitGraphMetrics? metrics = null,
        double height = 24)
    {
        Assert.True(CommitGraphGeometryKey.TryCreate(
            graph,
            height,
            metrics ?? CommitGraphMetrics.Default,
            out var key));
        return key;
    }

    private static CommitGraphRowVisual CreateGraph(
        int nodeLane = 0,
        int nodeTrackId = 0,
        IReadOnlyList<CommitGraphSegment>? incoming = null)
    {
        return new CommitGraphRowVisual(
            NodeLane: nodeLane,
            NodeTrackId: nodeTrackId,
            LaneCount: 2,
            IncomingSegments: incoming ??
            [
                new CommitGraphSegment(0, 0, 0),
                new CommitGraphSegment(1, 0, 1),
            ],
            OutgoingSegments:
            [
                new CommitGraphSegment(0, 0, 0),
                new CommitGraphSegment(1, 1, 1),
            ]);
    }
}

public sealed class CommitGraphGeometryCacheTests
{
    [Fact]
    public void StructurallyEqualKeyHitsAcrossDifferentGraphInstances()
    {
        var firstKey = CreateKey(CreateGraph(0));
        var secondKey = CreateKey(CreateGraph(0));
        var geometry = new CommitGraphGeometry(24, 24, [], [], null);
        var cache = new CommitGraphGeometryCache(capacity: 2);

        cache.Add(firstKey, geometry);

        Assert.True(cache.TryGet(secondKey, out var cached));
        Assert.Same(geometry, cached);
    }

    [Fact]
    public void CacheIsBoundedAndEvictsOldestEntry()
    {
        var cache = new CommitGraphGeometryCache(capacity: 2);
        var first = CreateKey(CreateGraph(0));
        var second = CreateKey(CreateGraph(1));
        var third = CreateKey(CreateGraph(2));

        cache.Add(first, new CommitGraphGeometry(24, 24, [], [], null));
        cache.Add(second, new CommitGraphGeometry(24, 24, [], [], null));
        var evicted = cache.Add(third, new CommitGraphGeometry(24, 24, [], [], null));

        Assert.True(evicted);
        Assert.Equal(2, cache.Count);
        Assert.Equal(1, cache.Evictions);
        Assert.False(cache.TryGet(first, out _));
        Assert.True(cache.TryGet(second, out _));
        Assert.True(cache.TryGet(third, out _));
    }

    [Fact]
    public void LayoutResetClearsSessionGeometryCache()
    {
        var state = new CommitGraphLayoutState();
        var key = CreateKey(CreateGraph(0));
        state.GeometryCache.Add(key, new CommitGraphGeometry(24, 24, [], [], null));

        state.Reset();

        Assert.Equal(0, state.GeometryCache.Count);
        Assert.False(state.GeometryCache.TryGet(key, out _));
    }

    private static CommitGraphGeometryKey CreateKey(CommitGraphRowVisual graph)
    {
        Assert.True(CommitGraphGeometryKey.TryCreate(
            graph,
            24,
            CommitGraphMetrics.Default,
            out var key));
        return key;
    }

    private static CommitGraphRowVisual CreateGraph(int trackId) =>
        new(
            NodeLane: 0,
            NodeTrackId: trackId,
            LaneCount: 1,
            IncomingSegments: [new CommitGraphSegment(0, 0, trackId)],
            OutgoingSegments: [new CommitGraphSegment(0, 0, trackId)]);
}
