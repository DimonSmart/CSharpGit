using CSharpGit.Presentation.Controls.CommitGraph;
using CSharpGit.Presentation.Diagnostics;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;

namespace CSharpGit.Presentation.Controls;

public sealed class CommitGraphControl : SKCanvasElement
{
    private static readonly SKColor[] LightPalette =
    [
        Color(0x00, 0x5A, 0x9E),
        Color(0xC2, 0x39, 0x74),
        Color(0x10, 0x7C, 0x10),
        Color(0xCA, 0x50, 0x10),
        Color(0x74, 0x4D, 0xA9),
        Color(0x03, 0x83, 0x87),
        Color(0xA4, 0x26, 0x2C),
        Color(0x6B, 0x69, 0x00),
    ];

    private static readonly SKColor[] DarkPalette =
    [
        Color(0x60, 0xCD, 0xFF),
        Color(0xFF, 0x99, 0xA4),
        Color(0x6C, 0xCB, 0x5F),
        Color(0xFF, 0xB9, 0x00),
        Color(0xB4, 0xA0, 0xFF),
        Color(0x4C, 0xE4, 0xE4),
        Color(0xFF, 0x8A, 0x80),
        Color(0xD7, 0xD0, 0x6B),
    ];

    private static readonly SKColor LightReflogColor = Color(0x7A, 0x7A, 0x7A);
    private static readonly SKColor DarkReflogColor = Color(0x9A, 0x9A, 0x9A);

    private CommitGraphGeometry? _geometry;
    private CommitGraphGeometryCache _geometryCache =
        CommitGraphPresentationContext.Current.GeometryCache;
    private CommitGraphMetrics _metrics = CommitGraphMetrics.Default;
    private CommitGraphGeometryKey? _renderedGeometryKey;
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

    public static readonly DependencyProperty IsReflogOnlyProperty = DependencyProperty.Register(
        nameof(IsReflogOnly),
        typeof(bool),
        typeof(CommitGraphControl),
        new PropertyMetadata(false, OnIsReflogOnlyChanged));

    public bool IsReflogOnly
    {
        get => (bool)GetValue(IsReflogOnlyProperty);
        set => SetValue(IsReflogOnlyProperty, value);
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
        Invalidate();
    }

    private void HandleSizeChanged(SizeChangedEventArgs args)
    {
        HistoryRenderDiagnostics.GraphSizeChanged();
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
            Invalidate();
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

    private static void OnIsReflogOnlyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is CommitGraphControl control)
            control.Invalidate();
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

            _geometry = null;
            Invalidate();
            _renderedGeometryKey = null;
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
        _geometry = geometry;
        Invalidate();

        _renderedGeometryKey = requestedKey;
        _hasRenderedState = true;
        Interlocked.Increment(ref _renderCount);

        HistoryRenderDiagnostics.GeometryRebuilt(
            reason,
            rebuildStartedAt,
            laneCount,
            segmentCount);
    }

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        // SKCanvasVisual already clips to its arranged bounds. Do not clear this
        // compositor canvas: a transparent clear erases the row hover/selection.
        if (_geometry is not { } geometry) return;
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Metrics.LineThickness,
        };
        using var path = new SKPath();
        foreach (var curve in geometry.Beziers)
        {
            paint.Color = GetTrackColor(curve.TrackId);
            path.Rewind();
            path.MoveTo((float)curve.Start.X, (float)curve.Start.Y);
            path.CubicTo((float)curve.Control1.X, (float)curve.Control1.Y,
                (float)curve.Control2.X, (float)curve.Control2.Y,
                (float)curve.End.X, (float)curve.End.Y);
            canvas.DrawPath(path, paint);
        }
        foreach (var line in geometry.Lines)
        {
            paint.Color = GetTrackColor(line.TrackId);
            canvas.DrawLine((float)line.Start.X, (float)line.Start.Y,
                (float)line.End.X, (float)line.End.Y, paint);
        }
        if (geometry.Node is { } node)
        {
            paint.Style = SKPaintStyle.Fill;
            paint.Color = GetTrackColor(node.TrackId);
            canvas.DrawCircle((float)node.Center.X, (float)node.Center.Y, (float)node.Radius, paint);
        }
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
            return _renderedGeometryKey is null && _geometry is null;
        }

        if (_renderedGeometryKey is not { } renderedKey
            || !CommitGraphGeometryKey.TryCreate(Graph, height, Metrics, out var currentKey))
        {
            return false;
        }

        currentKey = currentKey.ReuseHeightIfEquivalent(renderedKey);
        if (currentKey != renderedKey)
            return false;

        return _geometry is not null;
    }

    internal bool HasCurrentClipForCheck()
    {
        // SKCanvasVisual clips RenderOverride to the arranged element size.
        return double.IsFinite(ActualWidth) && ActualWidth > 0
            && IsValidHeight(ActualHeight);
    }

    private SKColor GetTrackColor(int trackId)
    {
        var graph = Graph;
        if (graph is not null
            && CommitGraphTrackPresentation.ShouldUseMutedStyle(
                IsReflogOnly,
                graph.NodeTrackId,
                trackId))
        {
            return ActualTheme == ElementTheme.Dark
                ? DarkReflogColor
                : LightReflogColor;
        }

        return GetPalette()[PaletteIndex(trackId)];
    }

    private SKColor[] GetPalette() =>
        ActualTheme == ElementTheme.Dark ? DarkPalette : LightPalette;

    private static bool IsValidHeight(double height) =>
        double.IsFinite(height) && height > 0;

    private static int PaletteIndex(int trackId) =>
        CommitGraphGeometryBuilder.GetPaletteIndex(trackId, LightPalette.Length);

    private static SKColor Color(byte red, byte green, byte blue) => new(red, green, blue);
}
