using CSharpGit.Presentation.Controls.CommitGraph;
using CSharpGit.Presentation.Diagnostics;
using Microsoft.UI.Xaml;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

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

    private static long _nextControlId;
    private readonly long _controlId = Interlocked.Increment(ref _nextControlId);
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
        CommitGraphDiagnostics.Trace("ControlCreated", $"control={_controlId} renderer=SKCanvasElement");

        Loaded += (_, _) =>
        {
            Log("Loaded", "control entered visual tree");
            Invalidate();
        };
        Unloaded += (_, _) => Log("Unloaded", "control leaving visual tree");
        DataContextChanged += (_, _) =>
        {
            Log("DataContextChanged", "data context changed");
            Invalidate();
        };
        SizeChanged += (_, _) =>
        {
            Log("SizeChanged", "actual size changed");
            Invalidate();
        };
        ActualThemeChanged += (_, _) =>
        {
            Log("ThemeChanged", $"theme={ActualTheme}");
            Invalidate();
        };
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var metrics = CommitGraphMetrics.Default;
        var desiredWidth = CommitGraphGeometryBuilder.CalculateWidth(Graph?.LaneCount ?? 0, metrics);
        var desiredHeight = double.IsFinite(Height) && Height >= 0
            ? Height
            : metrics.DefaultRowHeight;
        var desired = new Size(desiredWidth, desiredHeight);

        CommitGraphDiagnostics.Trace(
            "Measure",
            $"control={_controlId} renderer=SKCanvasElement available={availableSize.Width:0.##}x{availableSize.Height:0.##} "
            + $"desired={desired.Width:0.##}x{desired.Height:0.##} {CommitGraphDiagnostics.DescribeContext(DataContext)} "
            + CommitGraphDiagnostics.DescribeGraph(Graph));
        return desired;
    }

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var graph = Graph;
        canvas.Clear(SKColors.Transparent);

        if (graph is null)
        {
            _renderedGraph = null;
            _renderedHeight = area.Height;
            Interlocked.Increment(ref _renderCount);
            CommitGraphDiagnostics.Trace(
                "SkiaRender",
                $"control={_controlId} graph=null area={area.Width:0.##}x{area.Height:0.##} "
                + CommitGraphDiagnostics.DescribeContext(DataContext));
            return;
        }

        var metrics = CommitGraphMetrics.Default;
        var height = double.IsFinite(area.Height) && area.Height > 0
            ? area.Height
            : metrics.DefaultRowHeight;
        var geometry = CommitGraphGeometryBuilder.Build(graph, height, metrics);

        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)metrics.LineThickness,
        };

        foreach (var bezier in geometry.Beziers)
        {
            stroke.Color = GetTrackColor(bezier.TrackId);
            using var path = new SKPath();
            path.MoveTo((float)bezier.Start.X, (float)bezier.Start.Y);
            path.CubicTo(
                (float)bezier.Control1.X,
                (float)bezier.Control1.Y,
                (float)bezier.Control2.X,
                (float)bezier.Control2.Y,
                (float)bezier.End.X,
                (float)bezier.End.Y);
            canvas.DrawPath(path, stroke);
        }

        foreach (var line in geometry.Lines)
        {
            stroke.Color = GetTrackColor(line.TrackId);
            canvas.DrawLine(
                (float)line.Start.X,
                (float)line.Start.Y,
                (float)line.End.X,
                (float)line.End.Y,
                stroke);
        }

        if (geometry.Node is { } node)
        {
            using var fill = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = GetTrackColor(node.TrackId),
            };
            canvas.DrawCircle(
                (float)node.Center.X,
                (float)node.Center.Y,
                (float)node.Radius,
                fill);
        }

        _renderedGraph = graph;
        _renderedHeight = height;
        var renderCount = Interlocked.Increment(ref _renderCount);

        CommitGraphDiagnostics.Trace(
            "SkiaRender",
            $"control={_controlId} renderCount={renderCount} area={area.Width:0.##}x{area.Height:0.##} "
            + $"renderedHeight={height:0.##} {CommitGraphDiagnostics.DescribeContext(DataContext)} "
            + $"{CommitGraphDiagnostics.DescribeGraph(graph)} {CommitGraphDiagnostics.DescribeGeometry(geometry)}");
    }

    private static void OnGraphChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not CommitGraphControl control)
        {
            return;
        }

        CommitGraphDiagnostics.Trace(
            "GraphChanged",
            $"control={control._controlId} renderer=SKCanvasElement {CommitGraphDiagnostics.DescribeContext(control.DataContext)} "
            + $"old=[{CommitGraphDiagnostics.DescribeGraph(args.OldValue as CommitGraphRowVisual)}] "
            + $"new=[{CommitGraphDiagnostics.DescribeGraph(args.NewValue as CommitGraphRowVisual)}]");
        control.InvalidateMeasure();
        control.Invalidate();
    }

    internal bool HasCurrentRenderForCheck()
    {
        if (!ReferenceEquals(_renderedGraph, Graph) || Volatile.Read(ref _renderCount) == 0)
        {
            return false;
        }

        if (Graph is null)
        {
            return true;
        }

        var metrics = CommitGraphMetrics.Default;
        var height = double.IsFinite(ActualHeight) && ActualHeight > 0
            ? ActualHeight
            : metrics.DefaultRowHeight;
        return Math.Abs(_renderedHeight - height) <= 0.01;
    }

    private void Log(string eventName, string details)
        => CommitGraphDiagnostics.Trace(
            eventName,
            $"control={_controlId} renderer=SKCanvasElement {details} actual={ActualWidth:0.##}x{ActualHeight:0.##} "
            + $"renderCount={Volatile.Read(ref _renderCount)} {CommitGraphDiagnostics.DescribeContext(DataContext)} "
            + CommitGraphDiagnostics.DescribeGraph(Graph));

    private SKColor GetTrackColor(int trackId)
    {
        var palette = ActualTheme == ElementTheme.Dark ? DarkPalette : LightPalette;
        return palette[CommitGraphGeometryBuilder.GetPaletteIndex(trackId, palette.Length)];
    }

    private static SKColor Color(byte red, byte green, byte blue) => new(red, green, blue, 0xFF);
}
