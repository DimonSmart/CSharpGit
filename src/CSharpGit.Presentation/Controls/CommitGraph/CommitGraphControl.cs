using CSharpGit.Presentation.Controls.CommitGraph;
using CSharpGit.Presentation.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

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

    private static long _nextControlId;
    private readonly long _controlId = Interlocked.Increment(ref _nextControlId);
    private readonly Path[] _trackPaths = new Path[8];
    private readonly Path _nodePath;
    private CommitGraphRowVisual? _renderedGraph;
    private double _renderedHeight;
    private long _renderCount;

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

    public CommitGraphControl()
    {
        IsHitTestVisible = false;

        var metrics = CommitGraphMetrics.Default;
        for (var paletteIndex = 0; paletteIndex < _trackPaths.Length; paletteIndex++)
        {
            var path = new Path
            {
                StrokeThickness = metrics.LineThickness,
                IsHitTestVisible = false,
            };
            _trackPaths[paletteIndex] = path;
            Children.Add(path);
        }

        _nodePath = new Path { IsHitTestVisible = false };
        Children.Add(_nodePath);
        UpdateBrushes();

        CommitGraphDiagnostics.Trace(
            "ControlCreated",
            $"control={_controlId} renderer=StableXamlPaths fixedChildren={Children.Count}");

        Loaded += (_, _) =>
        {
            Log("Loaded", "control entered visual tree");
            UpdateGeometry("Loaded");
        };
        Unloaded += (_, _) => Log("Unloaded", "control leaving visual tree");
        DataContextChanged += (_, _) =>
        {
            Log("DataContextChanged", "data context changed");
            UpdateGeometry("DataContextChanged");
        };
        SizeChanged += (_, _) =>
        {
            Log("SizeChanged", "actual size changed");
            UpdateGeometry("SizeChanged");
        };
        ActualThemeChanged += (_, _) =>
        {
            Log("ThemeChanged", $"theme={ActualTheme}");
            UpdateBrushes();
            UpdateGeometry("ActualThemeChanged");
        };
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _ = base.MeasureOverride(availableSize);

        var metrics = CommitGraphMetrics.Default;
        var desiredWidth = CommitGraphGeometryBuilder.CalculateWidth(Graph?.LaneCount ?? 0, metrics);
        var desiredHeight = double.IsFinite(Height) && Height >= 0
            ? Height
            : metrics.DefaultRowHeight;
        var desired = new Size(desiredWidth, desiredHeight);

        CommitGraphDiagnostics.Trace(
            "Measure",
            $"control={_controlId} renderer=StableXamlPaths available={availableSize.Width:0.##}x{availableSize.Height:0.##} "
            + $"desired={desired.Width:0.##}x{desired.Height:0.##} {CommitGraphDiagnostics.DescribeContext(DataContext)} "
            + CommitGraphDiagnostics.DescribeGraph(Graph));
        return desired;
    }

    private static void OnGraphChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not CommitGraphControl control)
        {
            return;
        }

        CommitGraphDiagnostics.Trace(
            "GraphChanged",
            $"control={control._controlId} renderer=StableXamlPaths {CommitGraphDiagnostics.DescribeContext(control.DataContext)} "
            + $"old=[{CommitGraphDiagnostics.DescribeGraph(args.OldValue as CommitGraphRowVisual)}] "
            + $"new=[{CommitGraphDiagnostics.DescribeGraph(args.NewValue as CommitGraphRowVisual)}]");
        control.InvalidateMeasure();
        control.UpdateGeometry("GraphChanged");
    }

    private void UpdateGeometry(string reason)
    {
        var graph = Graph;
        var metrics = CommitGraphMetrics.Default;
        var height = double.IsFinite(ActualHeight) && ActualHeight > 0
            ? ActualHeight
            : metrics.DefaultRowHeight;

        foreach (var path in _trackPaths)
            path.Data = null;
        _nodePath.Data = null;
        _nodePath.Fill = null;

        _renderedGraph = graph;
        _renderedHeight = height;
        var renderCount = Interlocked.Increment(ref _renderCount);

        if (graph is null)
        {
            CommitGraphDiagnostics.Trace(
                "XamlRender",
                $"control={_controlId} reason={reason} renderCount={renderCount} graph=null actual={ActualWidth:0.##}x{ActualHeight:0.##} "
                + CommitGraphDiagnostics.DescribeContext(DataContext));
            return;
        }

        var geometry = CommitGraphGeometryBuilder.Build(graph, height, metrics);
        var pathGeometries = Enumerable.Range(0, _trackPaths.Length)
            .Select(_ => new PathGeometry())
            .ToArray();

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
            pathGeometries[PaletteIndex(bezier.TrackId)].Figures.Add(figure);
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
            pathGeometries[PaletteIndex(line.TrackId)].Figures.Add(figure);
        }

        for (var paletteIndex = 0; paletteIndex < _trackPaths.Length; paletteIndex++)
        {
            if (pathGeometries[paletteIndex].Figures.Count > 0)
                _trackPaths[paletteIndex].Data = pathGeometries[paletteIndex];
        }

        if (geometry.Node is { } node)
        {
            _nodePath.Data = new EllipseGeometry
            {
                Center = ToPoint(node.Center),
                RadiusX = node.Radius,
                RadiusY = node.Radius,
            };
            _nodePath.Fill = GetTrackBrush(node.TrackId);
        }

        CommitGraphDiagnostics.Trace(
            "XamlRender",
            $"control={_controlId} reason={reason} renderCount={renderCount} fixedChildren={Children.Count} "
            + $"actual={ActualWidth:0.##}x{ActualHeight:0.##} renderedHeight={height:0.##} "
            + $"{CommitGraphDiagnostics.DescribeContext(DataContext)} {CommitGraphDiagnostics.DescribeGraph(graph)} "
            + CommitGraphDiagnostics.DescribeGeometry(geometry));
    }

    internal bool HasCurrentRenderForCheck()
    {
        if (!ReferenceEquals(_renderedGraph, Graph) || Volatile.Read(ref _renderCount) == 0)
        {
            return false;
        }

        var metrics = CommitGraphMetrics.Default;
        var height = double.IsFinite(ActualHeight) && ActualHeight > 0
            ? ActualHeight
            : metrics.DefaultRowHeight;
        if (Math.Abs(_renderedHeight - height) > 0.01)
        {
            return false;
        }

        if (Graph is null)
        {
            return _trackPaths.All(path => path.Data is null) && _nodePath.Data is null;
        }

        var geometry = CommitGraphGeometryBuilder.Build(Graph, height, metrics);
        var expectedPaletteIndexes = geometry.Lines.Select(line => PaletteIndex(line.TrackId))
            .Concat(geometry.Beziers.Select(bezier => PaletteIndex(bezier.TrackId)))
            .ToHashSet();

        for (var paletteIndex = 0; paletteIndex < _trackPaths.Length; paletteIndex++)
        {
            if ((_trackPaths[paletteIndex].Data is not null) != expectedPaletteIndexes.Contains(paletteIndex))
                return false;
        }

        return (_nodePath.Data is not null) == (geometry.Node is not null);
    }

    private void UpdateBrushes()
    {
        var palette = ActualTheme == ElementTheme.Dark ? DarkPalette : LightPalette;
        for (var paletteIndex = 0; paletteIndex < _trackPaths.Length; paletteIndex++)
            _trackPaths[paletteIndex].Stroke = palette[paletteIndex];
    }

    private SolidColorBrush GetTrackBrush(int trackId)
    {
        var palette = ActualTheme == ElementTheme.Dark ? DarkPalette : LightPalette;
        return palette[PaletteIndex(trackId)];
    }

    private void Log(string eventName, string details)
        => CommitGraphDiagnostics.Trace(
            eventName,
            $"control={_controlId} renderer=StableXamlPaths {details} actual={ActualWidth:0.##}x{ActualHeight:0.##} "
            + $"renderCount={Volatile.Read(ref _renderCount)} fixedChildren={Children.Count} "
            + $"{CommitGraphDiagnostics.DescribeContext(DataContext)} {CommitGraphDiagnostics.DescribeGraph(Graph)}");

    private static int PaletteIndex(int trackId)
        => CommitGraphGeometryBuilder.GetPaletteIndex(trackId, LightPalette.Length);

    private static Point ToPoint(GraphPoint point) => new(point.X, point.Y);

    private static SolidColorBrush Brush(byte red, byte green, byte blue)
        => new(ColorHelper.FromArgb(0xFF, red, green, blue));
}
