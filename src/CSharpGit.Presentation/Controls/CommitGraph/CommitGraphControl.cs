using CSharpGit.Presentation.Controls.CommitGraph;
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
        SizeChanged += (_, _) => RenderGraph();
        ActualThemeChanged += (_, _) => RenderGraph();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _ = base.MeasureOverride(availableSize);

        var metrics = CommitGraphMetrics.Default;
        var desiredWidth = CommitGraphGeometryBuilder.CalculateWidth(Graph?.LaneCount ?? 0, metrics);
        var desiredHeight = double.IsFinite(Height) && Height >= 0
            ? Height
            : metrics.DefaultRowHeight;

        return new Size(desiredWidth, desiredHeight);
    }

    private static void OnGraphChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not CommitGraphControl control)
        {
            return;
        }

        control.InvalidateMeasure();
        control.RenderGraph();
    }

    private void RenderGraph()
    {
        Children.Clear();

        if (Graph is null)
        {
            return;
        }

        var metrics = CommitGraphMetrics.Default;
        var height = double.IsFinite(ActualHeight) && ActualHeight > 0
            ? ActualHeight
            : metrics.DefaultRowHeight;
        var geometry = CommitGraphGeometryBuilder.Build(Graph, height, metrics);

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
    }

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
