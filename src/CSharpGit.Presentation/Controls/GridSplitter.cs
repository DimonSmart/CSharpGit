using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation.Controls;

public enum GridResizeDirection { Columns, Rows }
public enum GridResizeBehavior { PreviousAndNext, CurrentAndNext }

public sealed class GridSplitter : Control
{
    private Windows.Foundation.Point? _lastPosition;
    public static readonly DependencyProperty ResizeDirectionProperty = DependencyProperty.Register(
        nameof(ResizeDirection), typeof(GridResizeDirection), typeof(GridSplitter), new PropertyMetadata(GridResizeDirection.Columns));

    public static readonly DependencyProperty ResizeBehaviorProperty = DependencyProperty.Register(
        nameof(ResizeBehavior), typeof(GridResizeBehavior), typeof(GridSplitter), new PropertyMetadata(GridResizeBehavior.PreviousAndNext));

    public GridResizeDirection ResizeDirection
    {
        get => (GridResizeDirection)GetValue(ResizeDirectionProperty);
        set => SetValue(ResizeDirectionProperty, value);
    }

    public GridResizeBehavior ResizeBehavior
    {
        get => (GridResizeBehavior)GetValue(ResizeBehaviorProperty);
        set => SetValue(ResizeBehaviorProperty, value);
    }

    public GridSplitter()
    {
        MinWidth = 6;
        MinHeight = 6;
        PointerPressed += BeginResize;
        PointerMoved += ContinueResize;
        PointerReleased += EndResize;
    }

    private void BeginResize(object sender, PointerRoutedEventArgs args)
    {
        _lastPosition = args.GetCurrentPoint(this).Position;
        CapturePointer(args.Pointer);
        args.Handled = true;
    }

    private void ContinueResize(object sender, PointerRoutedEventArgs args)
    {
        if (_lastPosition is not { } previous || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var currentPosition = args.GetCurrentPoint(this).Position;
        var horizontalChange = currentPosition.X - previous.X;
        var verticalChange = currentPosition.Y - previous.Y;
        _lastPosition = currentPosition;
        if (Parent is not Grid grid) return;
        if (ResizeDirection == GridResizeDirection.Columns)
        {
            var current = Grid.GetColumn(this);
            var first = ResizeBehavior == GridResizeBehavior.PreviousAndNext ? current - 1 : current;
            Resize(grid.ColumnDefinitions, first, first + 1, horizontalChange);
        }
        else
        {
            var current = Grid.GetRow(this);
            var first = ResizeBehavior == GridResizeBehavior.PreviousAndNext ? current - 1 : current;
            Resize(grid.RowDefinitions, first, first + 1, verticalChange);
        }
        args.Handled = true;
    }

    internal void ResizeForCheck(double delta)
    {
        if (Parent is not Grid grid) return;
        if (ResizeDirection == GridResizeDirection.Columns)
        {
            var current = Grid.GetColumn(this);
            var first = ResizeBehavior == GridResizeBehavior.PreviousAndNext ? current - 1 : current;
            Resize(grid.ColumnDefinitions, first, first + 1, delta);
        }
        else
        {
            var current = Grid.GetRow(this);
            var first = ResizeBehavior == GridResizeBehavior.PreviousAndNext ? current - 1 : current;
            Resize(grid.RowDefinitions, first, first + 1, delta);
        }
    }

    private void EndResize(object sender, PointerRoutedEventArgs args)
    {
        _lastPosition = null;
        ReleasePointerCapture(args.Pointer);
        args.Handled = true;
    }

    private static void Resize(IList<ColumnDefinition> definitions, int first, int second, double delta)
    {
        if (first < 0 || second >= definitions.Count) return;
        var total = definitions[first].ActualWidth + definitions[second].ActualWidth;
        var size = Math.Clamp(definitions[first].ActualWidth + delta, 80, Math.Max(80, total - 80));
        definitions[first].Width = new GridLength(size);
        definitions[second].Width = new GridLength(total - size);
    }

    private static void Resize(IList<RowDefinition> definitions, int first, int second, double delta)
    {
        if (first < 0 || second >= definitions.Count) return;
        var total = definitions[first].ActualHeight + definitions[second].ActualHeight;
        var size = Math.Clamp(definitions[first].ActualHeight + delta, 80, Math.Max(80, total - 80));
        definitions[first].Height = new GridLength(size);
        definitions[second].Height = new GridLength(total - size);
    }
}
