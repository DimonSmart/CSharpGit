using System.Collections;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace CSharpGit.Presentation.Controls;

public sealed class RepositoryTreeGuides : Canvas
{
    private const double GuideStrokeThickness = 1.5d;
    private const double ExpanderGlyphLength = 6d;
    private readonly TranslateTransform _translation = new();

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments),
        typeof(object),
        typeof(RepositoryTreeGuides),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty TotalIndentationProperty = DependencyProperty.Register(
        nameof(TotalIndentation),
        typeof(double),
        typeof(RepositoryTreeGuides),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    public static readonly DependencyProperty RowHeightProperty = DependencyProperty.Register(
        nameof(RowHeight),
        typeof(double),
        typeof(RepositoryTreeGuides),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush),
        typeof(Brush),
        typeof(RepositoryTreeGuides),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty NodeFillBrushProperty = DependencyProperty.Register(
        nameof(NodeFillBrush),
        typeof(Brush),
        typeof(RepositoryTreeGuides),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
        nameof(GlyphBrush),
        typeof(Brush),
        typeof(RepositoryTreeGuides),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty ExpandTargetProperty = DependencyProperty.Register(
        nameof(ExpandTarget),
        typeof(TreeViewItem),
        typeof(RepositoryTreeGuides),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded),
        typeof(bool),
        typeof(RepositoryTreeGuides),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    public RepositoryTreeGuides()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        RenderTransform = _translation;
    }

    public object? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double TotalIndentation
    {
        get => (double)GetValue(TotalIndentationProperty);
        set => SetValue(TotalIndentationProperty, value);
    }

    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public Brush? LineBrush
    {
        get => (Brush?)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush? NodeFillBrush
    {
        get => (Brush?)GetValue(NodeFillBrushProperty);
        set => SetValue(NodeFillBrushProperty, value);
    }

    public Brush? GlyphBrush
    {
        get => (Brush?)GetValue(GlyphBrushProperty);
        set => SetValue(GlyphBrushProperty, value);
    }

    public TreeViewItem? ExpandTarget
    {
        get => (TreeViewItem?)GetValue(ExpandTargetProperty);
        set => SetValue(ExpandTargetProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((RepositoryTreeGuides)dependencyObject).Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        Height = Math.Max(0, RowHeight);

        var segments = Segments switch
        {
            IReadOnlyList<RepositoryTreeGuideSegmentKind> list => list,
            IEnumerable<RepositoryTreeGuideSegmentKind> enumerable => enumerable.ToArray(),
            _ => Array.Empty<RepositoryTreeGuideSegmentKind>()
        };
        var hasChildren = HasItems(ExpandTarget?.ItemsSource);
        var guideWidth = segments.Count > 0
            ? RepositoryTreeGuideLayout.ResolveIndentation(segments.Count, TotalIndentation)
            : hasChildren
                ? RepositoryTreeGuideLayout.ResolveExpanderSurfaceWidth(0, TotalIndentation)
                : 0d;
        Width = guideWidth;
        _translation.X = -guideWidth;

        foreach (var guideLine in RepositoryTreeGuideLayout.BuildLines(segments, TotalIndentation, RowHeight))
            Children.Add(CreateGuideLine(guideLine));

        if (hasChildren && RowHeight > 0)
            AddExpander(segments.Count, guideWidth);
    }

    private Line CreateGuideLine(RepositoryTreeGuideLine guideLine) => new()
    {
        X1 = guideLine.X1,
        Y1 = guideLine.Y1,
        X2 = guideLine.X2,
        Y2 = guideLine.Y2,
        Stroke = LineBrush,
        StrokeThickness = GuideStrokeThickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        IsHitTestVisible = false
    };

    private void AddExpander(int segmentCount, double guideWidth)
    {
        var centerX = segmentCount > 0
            ? RepositoryTreeGuideLayout.ResolveExpanderCenterX(segmentCount, TotalIndentation)
            : guideWidth / 2d;
        var centerY = RowHeight / 2d;
        var boxSize = RepositoryTreeGuideLayout.ExpanderBoxSize;
        var glyphBrush = GlyphBrush ?? LineBrush;

        var glyph = new Grid { IsHitTestVisible = false };
        glyph.Children.Add(new Rectangle
        {
            Width = ExpanderGlyphLength,
            Height = GuideStrokeThickness,
            Fill = glyphBrush,
            RadiusX = GuideStrokeThickness / 2d,
            RadiusY = GuideStrokeThickness / 2d,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        if (!(ExpandTarget?.IsExpanded ?? IsExpanded))
        {
            glyph.Children.Add(new Rectangle
            {
                Width = GuideStrokeThickness,
                Height = ExpanderGlyphLength,
                Fill = glyphBrush,
                RadiusX = GuideStrokeThickness / 2d,
                RadiusY = GuideStrokeThickness / 2d,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            });
        }

        var expander = new Border
        {
            Width = boxSize,
            Height = boxSize,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(GuideStrokeThickness),
            Background = NodeFillBrush,
            CornerRadius = new CornerRadius(1.5),
            Child = glyph
        };
        expander.Tapped += Expander_Tapped;
        SetLeft(expander, centerX - (boxSize / 2d));
        SetTop(expander, centerY - (boxSize / 2d));
        Children.Add(expander);
    }

    private void Expander_Tapped(object sender, TappedRoutedEventArgs args)
    {
        if (ExpandTarget is null) return;
        ExpandTarget.IsExpanded = !ExpandTarget.IsExpanded;
        args.Handled = true;
    }

    private static bool HasItems(object? itemsSource)
    {
        if (itemsSource is null) return false;
        if (itemsSource is ICollection collection) return collection.Count > 0;
        if (itemsSource is not IEnumerable enumerable) return false;

        var enumerator = enumerable.GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }
}
