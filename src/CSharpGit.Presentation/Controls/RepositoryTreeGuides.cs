using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace CSharpGit.Presentation.Controls;

public sealed class RepositoryTreeGuides : Canvas
{
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

    public RepositoryTreeGuides()
    {
        Width = 0;
        IsHitTestVisible = false;
        Opacity = 0.72;
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

        foreach (var guideLine in RepositoryTreeGuideLayout.BuildLines(segments, TotalIndentation, RowHeight))
        {
            Children.Add(new Line
            {
                X1 = guideLine.X1,
                Y1 = guideLine.Y1,
                X2 = guideLine.X2,
                Y2 = guideLine.Y2,
                Stroke = LineBrush,
                StrokeThickness = 1,
                IsHitTestVisible = false
            });
        }
    }
}
