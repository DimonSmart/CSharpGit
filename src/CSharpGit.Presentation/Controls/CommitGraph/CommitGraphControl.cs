using CSharpGit.Presentation.Controls.CommitGraph;
using CSharpGit.Presentation.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;

namespace CSharpGit.Presentation.Controls;

public sealed class CommitGraphControl : Canvas
{
    private static readonly SolidColorBrush[] LightPalette =
    [
        Brush(0x00, 0x5A, 0x9E),
        Brush(0xC2, 0x39, 0x74),
        Brush(0x10, 0x7C, 0x10),
        Brush(0xCA, 0x50, 0x10),
        Brush(0x74, 0x4D, 0xA9),
        Brush(0x03, 0x83, 0x87),
        Brush(0xA4, 0x26, 0x2C),
        Brush(0x6B, 0x69, 0x00),
    ];

    private static readonly SolidColorBrush[] DarkPalette =
    [
        Brush(0x60, 0xCD, 0xFF),
        Brush(0xFF, 0x99, 0xA4),
        Brush(0x6C, 0xCB, 0x5F),
        Brush(0xFF, 0xB9, 0x00),
        Brush(0xB4, 0xA0, 0xFF),
        Brush(0x4C, 0xE4, 0xE4),
        Brush(0xFF, 0x8A, 0x80),
        Brush(0xD7, 0xD0, 0x6B),
    ];

    private readonly XamlPath[] _trackPaths = new XamlPath[8];
    private readonly PathGeometry[] _pathGeometries = new PathGeometry[8];
    private readonly XamlPath _nodePath;
    private readonly EllipseGeometry _nodeGeometry = new();
    private readonly RectangleGeometry _clipGeometry = new();
    private CommitGraphGeometryCache _geometryCache =
        CommitGraphPresentationContext.Current.GeometryCache;
    private CommitGraphMetrics _metrics = CommitGraphMetrics.Default;
    private CommitGraphGeometryKey? _renderedGeometryKey;
    private int? _renderedNodeTrackId;
    private long _renderCount;
    private bool _hasRenderedState;
    private bool _presentationContextSubscribed;

    public static readonly DependencyProperty GraphProperty = DependencyProperty.Register(
        nameof(Graph),
        typeof(CommitGraphRowVisual),
        typeof(CommitGraphControl),
        new PropertyMetadata(null, OnGraphChanged));

    public CommitGraphRowVisual? Graph
    {
        get => (CommitGraphRowVisual?)GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public CommitGraphMetrics Metrics
    {
        get => _metrics;
        set => ApplyMetrics(
            value,
            HistoryGeometryUpdateReason.MetricsChanged,
            requestGeometryUpdate: true);
    }

    public CommitGraphControl()
    {
        HistoryRenderDiagnostics.GraphControlCreated();
        IsHitTestVisible = false;
        Clip = _clipGeometry;

        for (var paletteIndex = 0; paletteIndex < _trackPaths.Length; paletteIndex++)
        {
            _pathGeometries[paletteIndex] = new PathGeometry();
            var path = new XamlPath
            {
                StrokeThickness = Metrics.LineThickness,
                IsHitTestVisible = false,
            };
            _trackPaths[paletteIndex] = path;
            Children.Add(path);
        }

        _nodePath = new XamlPath { IsHitTestVisible = false };
        Children.Add(_nodePath);
        UpdateBrushes();

        Loaded += CommitGraphControl_Loaded;
        Unloaded += CommitGraphControl_Unloaded;
        DataContextChanged += (_, _) => HandleDataContextChanged();
        SizeChanged += (_, args) => HandleSizeChanged(args);
        ActualThemeChanged += (_, _) => HandleThemeChanged();
    }

    private void CommitGraphControl_Loaded(object sender, RoutedEventArgs args)
    {
        HistoryRenderDiagnostics.GraphLoaded();
        if (!_presentationContextSubscribed)
        {
            CommitGraphPresentationContext.Changed += CommitGraphPresentationContext_Changed;
            _presentationContextSubscribed = true;
        }

        ApplyPresentationLayout();
        UpdateClip();
        UpdateGeometry(HistoryGeometryUpdateReason.Loaded);
    }

    private void CommitGraphControl_Unloaded(object sender, RoutedEventArgs args)
    {
        HistoryRenderDiagnostics.GraphUnloaded();
        if (!_presentationContextSubscribed)
            return;

        CommitGraphPresentationContext.Changed -= CommitGraphPresentationContext_Changed;
        _presentationContextSubscribed = false;
    }

    private void HandleDataContextChanged()
    {
        HistoryRenderDiagnostics.GraphDataContextChanged();
    }

    private void HandleThemeChanged()
    {
        HistoryRenderDiagnostics.GraphThemeChanged();
        UpdateBrushes();
    }

    private void HandleSizeChanged(SizeChangedEventArgs args)
    {
        HistoryRenderDiagnostics.GraphSizeChanged();
        UpdateClip();

        var previousHeight = args.PreviousSize.Height;
        var currentHeight = args.NewSize.Height;
        var previousValid = IsValidHeight(previousHeight);
        var currentValid = IsValidHeight(currentHeight);

        if (!previousValid && currentValid)
        {
            HistoryRenderDiagnostics.GraphSizeChangedFirstValidHeight();
            UpdateGeometry(HistoryGeometryUpdateReason.SizeChanged);
            return;
        }

        if (previousValid && !currentValid)
        {
            HistoryRenderDiagnostics.GraphSizeChangedHeightChanged();
            return;
        }

        if (previousValid && currentValid)
        {
            var heightDelta = Math.Abs(currentHeight - previousHeight);
            if (heightDelta > CommitGraphGeometryKey.GeometryEpsilon)
            {
                HistoryRenderDiagnostics.GraphSizeChangedHeightChanged();
                UpdateGeometry(HistoryGeometryUpdateReason.SizeChanged);
                return;
            }

            if (heightDelta > 0)
            {
                HistoryRenderDiagnostics.GraphSizeChangedInsignificant();
                return;
            }
        }

        if (Math.Abs(args.NewSize.Width - args.PreviousSize.Width)
            > CommitGraphGeometryKey.GeometryEpsilon)
        {
            HistoryRenderDiagnostics.GraphSizeChangedWidthOnly();
            return;
        }

        HistoryRenderDiagnostics.GraphSizeChangedInsignificant();
    }

    private void CommitGraphPresentationContext_Changed(object? sender, EventArgs args)
    {
        var startedAt = HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
        HistoryRenderDiagnostics.GraphPresentationContextChanged();
        ApplyPresentationLayout();
        UpdateClip();
        UpdateGeometry(HistoryGeometryUpdateReason.PresentationContextChanged);
        HistoryRenderDiagnostics.PresentationDelivered(startedAt);
    }

    private void ApplyPresentationLayout()
    {
        var layout = CommitGraphPresentationContext.Current;
        _geometryCache = layout.GeometryCache;
        HistoryRenderDiagnostics.GeometrySharedCacheStateChanged(
            _geometryCache.Count,
            evicted: false);

        if (!double.IsFinite(Width)
            || Math.Abs(Width - layout.GraphWidth) > CommitGraphGeometryKey.GeometryEpsilon)
        {
            Width = layout.GraphWidth;
        }

        ApplyMetrics(
            layout.Metrics,
            HistoryGeometryUpdateReason.PresentationContextChanged,
            requestGeometryUpdate: false);
    }

    private void ApplyMetrics(
        CommitGraphMetrics value,
        HistoryGeometryUpdateReason reason,
        bool requestGeometryUpdate)
    {
        if (_metrics == value)
            return;

        HistoryRenderDiagnostics.GraphMetricsChanged();
        var geometryChanged = !CommitGraphGeometryKey.GeometryMetricsEqual(_metrics, value);
        var lineThicknessChanged = !_metrics.LineThickness.Equals(value.LineThickness);
        _metrics = value;

        if (lineThicknessChanged)
        {
            foreach (var path in _trackPaths)
                path.StrokeThickness = value.LineThickness;
        }

        if (requestGeometryUpdate && geometryChanged)
            UpdateGeometry(reason);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var startedAt = HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
        var measured = base.MeasureOverride(availableSize);
        var width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : 0;
        var result = new Size(width, measured.Height);
        HistoryRenderDiagnostics.GraphMeasureCompleted(startedAt);
        return result;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var startedAt = HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
        var result = base.ArrangeOverride(finalSize);
        HistoryRenderDiagnostics.GraphArrangeCompleted(startedAt);
        return result;
    }

    private static void OnGraphChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not CommitGraphControl control)
            return;

        HistoryRenderDiagnostics.GraphChanged();
        control.UpdateGeometry(HistoryGeometryUpdateReason.GraphChanged);
    }

    private void UpdateClip()
    {
        var width = double.IsFinite(ActualWidth) && ActualWidth > 0 ? ActualWidth : 0;
        var height = double.IsFinite(ActualHeight) && ActualHeight > 0 ? ActualHeight : 0;
        _clipGeometry.Rect = new Rect(0, 0, width, height);
    }

    private void UpdateGeometry(
        HistoryGeometryUpdateReason reason = HistoryGeometryUpdateReason.Unknown)
    {
        HistoryRenderDiagnostics.GeometryUpdateAttempted(reason);

        var graph = Graph;
        var height = ActualHeight;
        if (!IsValidHeight(height))
        {
            HistoryRenderDiagnostics.GeometrySkippedInvalidHeight();
            return;
        }

        if (graph is null)
        {
            if (_hasRenderedState && _renderedGeometryKey is null)
            {
                HistoryRenderDiagnostics.GeometrySameKeySkipped();
                return;
            }

            ClearMaterializedGeometry();
            _renderedGeometryKey = null;
            _renderedNodeTrackId = null;
            _hasRenderedState = true;
            Interlocked.Increment(ref _renderCount);
            return;
        }

        if (!CommitGraphGeometryKey.TryCreate(graph, height, Metrics, out var requestedKey))
        {
            HistoryRenderDiagnostics.GeometrySkippedInvalidHeight();
            return;
        }

        if (_renderedGeometryKey is { } currentKey)
            requestedKey = requestedKey.ReuseHeightIfEquivalent(currentKey);

        if (_hasRenderedState
            && _renderedGeometryKey is { } renderedKey
            && renderedKey == requestedKey)
        {
            HistoryRenderDiagnostics.GeometrySameKeySkipped();
            return;
        }

        HistoryRenderDiagnostics.GeometryBuildRequested();
        var buildCause = DetermineBuildCause(_renderedGeometryKey, requestedKey);
        var laneCount = graph.LaneCount;
        var segmentCount = (graph.IncomingSegments?.Count ?? 0)
            + (graph.OutgoingSegments?.Count ?? 0);
        CommitGraphGeometry geometry;

        if (_geometryCache.TryGet(requestedKey, out var cached))
        {
            geometry = cached;
            HistoryRenderDiagnostics.GeometrySharedCacheHit();
        }
        else
        {
            HistoryRenderDiagnostics.GeometrySharedCacheMiss();
            var builderStartedAt = HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
            geometry = CommitGraphGeometryBuilder.Build(graph, requestedKey.Height, Metrics);
            HistoryRenderDiagnostics.GeometryBuilderCompleted(builderStartedAt);
            HistoryRenderDiagnostics.GeometryActualBuilt(reason, buildCause);

            var evicted = _geometryCache.Add(requestedKey, geometry);
            HistoryRenderDiagnostics.GeometrySharedCacheStateChanged(
                _geometryCache.Count,
                evicted);
        }

        var rebuildStartedAt = HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
        var materializationStartedAt =
            HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
        MaterializeGeometry(geometry);
        HistoryRenderDiagnostics.GeometryMaterializationCompleted(materializationStartedAt);

        _renderedGeometryKey = requestedKey;
        _renderedNodeTrackId = geometry.Node?.TrackId;
        _hasRenderedState = true;
        Interlocked.Increment(ref _renderCount);

        HistoryRenderDiagnostics.GeometryRebuilt(
            reason,
            rebuildStartedAt,
            laneCount,
            segmentCount);
    }

    private void MaterializeGeometry(CommitGraphGeometry geometry)
    {
        for (var paletteIndex = 0; paletteIndex < _trackPaths.Length; paletteIndex++)
        {
            _trackPaths[paletteIndex].Data = null;
            _pathGeometries[paletteIndex].Figures.Clear();
        }

        foreach (var bezier in geometry.Beziers)
        {
            var figure = new PathFigure
            {
                StartPoint = ToPoint(bezier.Start),
                IsClosed = false,
                IsFilled = false,
            };
            figure.Segments.Add(new BezierSegment
            {
                Point1 = ToPoint(bezier.Control1),
                Point2 = ToPoint(bezier.Control2),
                Point3 = ToPoint(bezier.End),
            });
            _pathGeometries[PaletteIndex(bezier.TrackId)].Figures.Add(figure);
        }

        foreach (var line in geometry.Lines)
        {
            var figure = new PathFigure
            {
                StartPoint = ToPoint(line.Start),
                IsClosed = false,
                IsFilled = false,
            };
            figure.Segments.Add(new LineSegment { Point = ToPoint(line.End) });
            _pathGeometries[PaletteIndex(line.TrackId)].Figures.Add(figure);
        }

        for (var paletteIndex = 0; paletteIndex < _trackPaths.Length; paletteIndex++)
        {
            if (_pathGeometries[paletteIndex].Figures.Count > 0)
                _trackPaths[paletteIndex].Data = _pathGeometries[paletteIndex];
        }

        if (geometry.Node is { } node)
        {
            _nodeGeometry.Center = ToPoint(node.Center);
            _nodeGeometry.RadiusX = node.Radius;
            _nodeGeometry.RadiusY = node.Radius;
            _nodePath.Data = _nodeGeometry;
            _nodePath.Fill = GetTrackBrush(node.TrackId);
        }
        else
        {
            _nodePath.Data = null;
            _nodePath.Fill = null;
        }
    }

    private void ClearMaterializedGeometry()
    {
        for (var paletteIndex = 0; paletteIndex < _trackPaths.Length; paletteIndex++)
        {
            _trackPaths[paletteIndex].Data = null;
            _pathGeometries[paletteIndex].Figures.Clear();
        }

        _nodePath.Data = null;
        _nodePath.Fill = null;
    }

    private static HistoryGeometryBuildCause DetermineBuildCause(
        CommitGraphGeometryKey? renderedKey,
        in CommitGraphGeometryKey requestedKey)
    {
        if (renderedKey is not { } currentKey)
            return HistoryGeometryBuildCause.FirstRender;
        if (!currentKey.HasSameTopology(requestedKey))
            return HistoryGeometryBuildCause.TopologyChanged;
        if (!currentKey.HasSameGeometryMetrics(requestedKey))
            return HistoryGeometryBuildCause.GeometryMetricsChanged;
        if (Math.Abs(currentKey.Height - requestedKey.Height)
            > CommitGraphGeometryKey.GeometryEpsilon)
        {
            return HistoryGeometryBuildCause.HeightChanged;
        }

        return HistoryGeometryBuildCause.CacheMiss;
    }

    internal bool HasCurrentRenderForCheck()
    {
        var height = ActualHeight;
        if (!IsValidHeight(height)
            || !_hasRenderedState
            || Volatile.Read(ref _renderCount) == 0)
        {
            return false;
        }

        if (Graph is null)
        {
            return _renderedGeometryKey is null
                && _trackPaths.All(path => path.Data is null)
                && _nodePath.Data is null;
        }

        if (_renderedGeometryKey is not { } renderedKey
            || !CommitGraphGeometryKey.TryCreate(Graph, height, Metrics, out var currentKey))
        {
            return false;
        }

        currentKey = currentKey.ReuseHeightIfEquivalent(renderedKey);
        if (currentKey != renderedKey)
            return false;

        var geometry = CommitGraphGeometryBuilder.Build(Graph, renderedKey.Height, Metrics);
        var expectedPaletteIndexes = geometry.Lines.Select(line => PaletteIndex(line.TrackId))
            .Concat(geometry.Beziers.Select(bezier => PaletteIndex(bezier.TrackId)))
            .ToHashSet();

        for (var paletteIndex = 0; paletteIndex < _trackPaths.Length; paletteIndex++)
        {
            if ((_trackPaths[paletteIndex].Data is not null)
                != expectedPaletteIndexes.Contains(paletteIndex))
            {
                return false;
            }

            if (Math.Abs(_trackPaths[paletteIndex].StrokeThickness - Metrics.LineThickness)
                > CommitGraphGeometryKey.GeometryEpsilon)
            {
                return false;
            }

            if (!ReferenceEquals(_trackPaths[paletteIndex].Stroke, GetPaletteBrush(paletteIndex)))
                return false;
        }

        if ((_nodePath.Data is not null) != (geometry.Node is not null))
            return false;
        if (geometry.Node is { } node
            && !ReferenceEquals(_nodePath.Fill, GetTrackBrush(node.TrackId)))
        {
            return false;
        }

        return true;
    }

    internal bool HasCurrentClipForCheck()
    {
        var expectedWidth = double.IsFinite(ActualWidth) && ActualWidth > 0 ? ActualWidth : 0;
        var expectedHeight = double.IsFinite(ActualHeight) && ActualHeight > 0 ? ActualHeight : 0;
        var clip = _clipGeometry.Rect;
        return Math.Abs(clip.X) <= CommitGraphGeometryKey.GeometryEpsilon
            && Math.Abs(clip.Y) <= CommitGraphGeometryKey.GeometryEpsilon
            && Math.Abs(clip.Width - expectedWidth) <= CommitGraphGeometryKey.GeometryEpsilon
            && Math.Abs(clip.Height - expectedHeight) <= CommitGraphGeometryKey.GeometryEpsilon;
    }

    private void UpdateBrushes()
    {
        var palette = GetPalette();
        for (var paletteIndex = 0; paletteIndex < _trackPaths.Length; paletteIndex++)
            _trackPaths[paletteIndex].Stroke = palette[paletteIndex];

        _nodePath.Fill = _renderedNodeTrackId is { } trackId
            ? GetTrackBrush(trackId)
            : null;
    }

    private SolidColorBrush GetTrackBrush(int trackId) =>
        GetPalette()[PaletteIndex(trackId)];

    private SolidColorBrush GetPaletteBrush(int paletteIndex) =>
        GetPalette()[paletteIndex];

    private SolidColorBrush[] GetPalette() =>
        ActualTheme == ElementTheme.Dark ? DarkPalette : LightPalette;

    private static bool IsValidHeight(double height) =>
        double.IsFinite(height) && height > 0;

    private static int PaletteIndex(int trackId) =>
        CommitGraphGeometryBuilder.GetPaletteIndex(trackId, LightPalette.Length);

    private static Point ToPoint(GraphPoint point) => new(point.X, point.Y);

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(ColorHelper.FromArgb(0xFF, red, green, blue));
}
