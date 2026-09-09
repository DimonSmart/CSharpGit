using CSharpGit.Presentation.Controls.CommitGraph;
using CSharpGit.Presentation.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
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

    private static long _nextControlId;
    private readonly long _controlId = Interlocked.Increment(ref _nextControlId);
    private long _renderVersion;
    private bool _isLoaded;
    private CommitGraphRowVisual? _renderedGraph;
    private double _renderedHeight;

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
        Log("ControlCreated", "constructor");
        Loaded += (_, _) =>
        {
            _isLoaded = true;
            Log("Loaded", "control entered visual tree");
            ScheduleRender("Loaded");
        };
        Unloaded += (_, _) =>
        {
            Log("Unloaded", "control leaving visual tree");
            _isLoaded = false;
            Interlocked.Increment(ref _renderVersion);
            _renderedGraph = null;
            Children.Clear();
        };
        DataContextChanged += (_, _) =>
        {
            Log("DataContextChanged", "data context changed");
            ScheduleRender("DataContextChanged");
        };
        SizeChanged += (_, _) =>
        {
            Log("SizeChanged", "actual size changed");
            ScheduleRender("SizeChanged");
        };
        ActualThemeChanged += (_, _) =>
        {
            Log("ThemeChanged", $"theme={ActualTheme}");
            ScheduleRender("ActualThemeChanged");
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
            $"control={_controlId} available={availableSize.Width:0.##}x{availableSize.Height:0.##} "
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
            $"control={control._controlId} {CommitGraphDiagnostics.DescribeContext(control.DataContext)} "
            + $"old=[{CommitGraphDiagnostics.DescribeGraph(args.OldValue as CommitGraphRowVisual)}] "
            + $"new=[{CommitGraphDiagnostics.DescribeGraph(args.NewValue as CommitGraphRowVisual)}]");
        control.InvalidateMeasure();
        control.ScheduleRender("GraphChanged");
    }

    private void ScheduleRender(string reason)
    {
        var version = Interlocked.Increment(ref _renderVersion);
        CommitGraphDiagnostics.Trace(
            "RenderScheduled",
            $"control={_controlId} reason={reason} version={version} loaded={_isLoaded} "
            + $"actual={ActualWidth:0.##}x{ActualHeight:0.##} {CommitGraphDiagnostics.DescribeContext(DataContext)} "
            + CommitGraphDiagnostics.DescribeGraph(Graph));
        if (!_isLoaded)
        {
            CommitGraphDiagnostics.Trace("RenderDeferred", $"control={_controlId} reason={reason} version={version} not-loaded");
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            var currentVersion = Volatile.Read(ref _renderVersion);
            if (!_isLoaded || version != currentVersion)
            {
                CommitGraphDiagnostics.Trace(
                    "RenderSkipped",
                    $"control={_controlId} reason={reason} queuedVersion={version} currentVersion={currentVersion} loaded={_isLoaded} "
                    + CommitGraphDiagnostics.DescribeContext(DataContext));
                return;
            }

            RenderGraph(reason, version);
        });
    }

    private void RenderGraph(string reason, long version)
    {
        Children.Clear();
        _renderedGraph = Graph;

        if (Graph is null)
        {
            _renderedHeight = 0;
            CommitGraphDiagnostics.Trace(
                "Render",
                $"control={_controlId} reason={reason} version={version} graph=null children=0 "
                + $"actual={ActualWidth:0.##}x{ActualHeight:0.##} {CommitGraphDiagnostics.DescribeContext(DataContext)}");
            return;
        }

        var metrics = CommitGraphMetrics.Default;
        var height = double.IsFinite(ActualHeight) && ActualHeight > 0
            ? ActualHeight
            : metrics.DefaultRowHeight;
        var geometry = CommitGraphGeometryBuilder.Build(Graph, height, metrics);
        _renderedHeight = height;

        foreach (var bezier in geometry.Beziers)
        {
            AddBezier(bezier, metrics.LineThickness);
        }

        foreach (var line in geometry.Lines)
        {
            AddLine(line, metrics.LineThickness);
        }

        if (geometry.Node is { } node)
        {
            AddNode(node);
        }

        CommitGraphDiagnostics.Trace(
            "Render",
            $"control={_controlId} reason={reason} version={version} children={Children.Count} "
            + $"actual={ActualWidth:0.##}x{ActualHeight:0.##} renderedHeight={_renderedHeight:0.##} "
            + $"{CommitGraphDiagnostics.DescribeContext(DataContext)} {CommitGraphDiagnostics.DescribeGraph(Graph)} "
            + CommitGraphDiagnostics.DescribeGeometry(geometry));
    }

    internal bool HasCurrentRenderForCheck()
    {
        if (!_isLoaded)
        {
            return true;
        }

        if (!ReferenceEquals(_renderedGraph, Graph))
        {
            return false;
        }

        if (Graph is null)
        {
            return Children.Count == 0;
        }

        var metrics = CommitGraphMetrics.Default;
        var height = double.IsFinite(ActualHeight) && ActualHeight > 0
            ? ActualHeight
            : metrics.DefaultRowHeight;
        if (Math.Abs(_renderedHeight - height) > 0.01)
        {
            return false;
        }

        var geometry = CommitGraphGeometryBuilder.Build(Graph, height, metrics);
        var expectedChildren = geometry.Beziers.Count + geometry.Lines.Count + (geometry.Node is null ? 0 : 1);
        return Children.Count == expectedChildren;
    }

    private void Log(string eventName, string details)
        => CommitGraphDiagnostics.Trace(
            eventName,
            $"control={_controlId} {details} loaded={_isLoaded} version={Volatile.Read(ref _renderVersion)} "
            + $"actual={ActualWidth:0.##}x{ActualHeight:0.##} children={Children.Count} "
            + $"{CommitGraphDiagnostics.DescribeContext(DataContext)} {CommitGraphDiagnostics.DescribeGraph(Graph)}");

    private void AddBezier(GraphBezierPrimitive primitive, double thickness)
    {
        var figure = new PathFigure
        {
            StartPoint = ToPoint(primitive.Start),
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new BezierSegment
        {
            Point1 = ToPoint(primitive.Control1),
            Point2 = ToPoint(primitive.Control2),
            Point3 = ToPoint(primitive.End),
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        Children.Add(new XamlPath
        {
            Data = geometry,
            Stroke = GetTrackBrush(primitive.TrackId),
            StrokeThickness = thickness,
            IsHitTestVisible = false,
        });
    }

    private void AddLine(GraphLinePrimitive primitive, double thickness)
    {
        Children.Add(new Line
        {
            X1 = primitive.Start.X,
            Y1 = primitive.Start.Y,
            X2 = primitive.End.X,
            Y2 = primitive.End.Y,
            Stroke = GetTrackBrush(primitive.TrackId),
            StrokeThickness = thickness,
            IsHitTestVisible = false,
        });
    }

    private void AddNode(GraphNodePrimitive primitive)
    {
        var diameter = primitive.Radius * 2;
        var node = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = GetTrackBrush(primitive.TrackId),
            IsHitTestVisible = false,
        };

        SetLeft(node, primitive.Center.X - primitive.Radius);
        SetTop(node, primitive.Center.Y - primitive.Radius);
        Children.Add(node);
    }

    private SolidColorBrush GetTrackBrush(int trackId)
    {
        var palette = ActualTheme == ElementTheme.Dark ? DarkPalette : LightPalette;
        return palette[CommitGraphGeometryBuilder.GetPaletteIndex(trackId, palette.Length)];
    }

    private static Point ToPoint(GraphPoint point) => new(point.X, point.Y);

    private static SolidColorBrush Brush(byte red, byte green, byte blue)
        => new(ColorHelper.FromArgb(0xFF, red, green, blue));
}
