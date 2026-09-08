using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation.Controls;

public enum GridResizeDirection { Columns, Rows }
public enum GridResizeBehavior { PreviousAndNext, CurrentAndNext }

public sealed class GridSplitter : Control
{
    private Windows.Foundation.Point? _lastPosition;

    public static readonly DependencyProperty ResizeDirectionProperty = Register(nameof(ResizeDirection), typeof(GridResizeDirection), GridResizeDirection.Columns, OnDirectionChanged);
    public static readonly DependencyProperty ResizeBehaviorProperty = Register(nameof(ResizeBehavior), typeof(GridResizeBehavior), GridResizeBehavior.PreviousAndNext);
    public static readonly DependencyProperty MinimumFirstProperty = Register(nameof(MinimumFirst), typeof(double), 80d);
    public static readonly DependencyProperty MinimumSecondProperty = Register(nameof(MinimumSecond), typeof(double), 80d);
    public static readonly DependencyProperty MaximumFirstProperty = Register(nameof(MaximumFirst), typeof(double), double.MaxValue);
    public static readonly DependencyProperty MaximumSecondProperty = Register(nameof(MaximumSecond), typeof(double), double.MaxValue);

    public GridResizeDirection ResizeDirection { get => (GridResizeDirection)GetValue(ResizeDirectionProperty); set => SetValue(ResizeDirectionProperty, value); }
    public GridResizeBehavior ResizeBehavior { get => (GridResizeBehavior)GetValue(ResizeBehaviorProperty); set => SetValue(ResizeBehaviorProperty, value); }
    public double MinimumFirst { get => (double)GetValue(MinimumFirstProperty); set => SetValue(MinimumFirstProperty, value); }
    public double MinimumSecond { get => (double)GetValue(MinimumSecondProperty); set => SetValue(MinimumSecondProperty, value); }
    public double MaximumFirst { get => (double)GetValue(MaximumFirstProperty); set => SetValue(MaximumFirstProperty, value); }
    public double MaximumSecond { get => (double)GetValue(MaximumSecondProperty); set => SetValue(MaximumSecondProperty, value); }

    public GridSplitter()
    {
        MinWidth = MinHeight = 6;
        Opacity = 0.22;
        PointerPressed += BeginResize;
        PointerMoved += ContinueResize;
        PointerReleased += EndResize;
        PointerCaptureLost += (_, _) => _lastPosition = null;
        PointerEntered += (_, _) => Opacity = 0.62;
        PointerExited += (_, _) => { if (_lastPosition is null) Opacity = 0.22; };
        UpdateCursor();
    }

    private static DependencyProperty Register(string name, Type type, object defaultValue, PropertyChangedCallback? callback = null) =>
        DependencyProperty.Register(name, type, typeof(GridSplitter), new PropertyMetadata(defaultValue, callback));

    private static void OnDirectionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs _) => ((GridSplitter)sender).UpdateCursor();

    private void UpdateCursor() => ProtectedCursor = InputSystemCursor.Create(
        ResizeDirection == GridResizeDirection.Columns ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth);

    private void BeginResize(object sender, PointerRoutedEventArgs args)
    {
        _lastPosition = Position(args);
        CapturePointer(args.Pointer);
        Opacity = 0.78;
        args.Handled = true;
    }

    private void ContinueResize(object sender, PointerRoutedEventArgs args)
    {
        if (_lastPosition is not { } previous || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Parent is not Grid grid) return;
        var current = Position(args);
        var delta = ResizeDirection == GridResizeDirection.Columns ? current.X - previous.X : current.Y - previous.Y;
        _lastPosition = current;
        Resize(grid, delta);
        args.Handled = true;
    }

    private void EndResize(object sender, PointerRoutedEventArgs args)
    {
        _lastPosition = null;
        ReleasePointerCapture(args.Pointer);
        Opacity = 0.62;
        args.Handled = true;
    }

    private Windows.Foundation.Point Position(PointerRoutedEventArgs args) =>
        Parent is UIElement parent ? args.GetCurrentPoint(parent).Position : args.GetCurrentPoint(this).Position;

    internal void ResizeForCheck(double delta)
    {
        if (Parent is Grid grid) Resize(grid, delta);
    }

    private void Resize(Grid grid, double delta)
    {
        var splitterIndex = ResizeDirection == GridResizeDirection.Columns ? Grid.GetColumn(this) : Grid.GetRow(this);
        var first = ResizeBehavior == GridResizeBehavior.PreviousAndNext ? splitterIndex - 1 : splitterIndex;
        var second = splitterIndex + 1;

        if (ResizeDirection == GridResizeDirection.Columns)
        {
            if (first < 0 || second >= grid.ColumnDefinitions.Count) return;
            var a = grid.ColumnDefinitions[first];
            var b = grid.ColumnDefinitions[second];
            ResizePair(a.ActualWidth, b.ActualWidth, delta,
                Math.Max(MinimumFirst, a.MinWidth), Math.Max(MinimumSecond, b.MinWidth),
                Math.Min(MaximumFirst, a.MaxWidth), Math.Min(MaximumSecond, b.MaxWidth),
                (x, y) => ApplyWidths(a, b, x, y));
        }
        else
        {
            if (first < 0 || second >= grid.RowDefinitions.Count) return;
            var a = grid.RowDefinitions[first];
            var b = grid.RowDefinitions[second];
            ResizePair(a.ActualHeight, b.ActualHeight, delta,
                Math.Max(MinimumFirst, a.MinHeight), Math.Max(MinimumSecond, b.MinHeight),
                Math.Min(MaximumFirst, a.MaxHeight), Math.Min(MaximumSecond, b.MaxHeight),
                (x, y) => ApplyHeights(a, b, x, y));
        }
    }

    private static void ResizePair(double first, double second, double delta, double minFirst, double minSecond, double maxFirst, double maxSecond, Action<double, double> apply)
    {
        var total = first + second;
        if (total <= 0) return;
        var lower = Math.Max(minFirst, total - maxSecond);
        var upper = Math.Min(maxFirst, total - minSecond);
        if (upper < lower) return;
        var firstSize = Math.Clamp(first + delta, lower, upper);
        apply(firstSize, total - firstSize);
    }

    private static void ApplyWidths(ColumnDefinition first, ColumnDefinition second, double firstSize, double secondSize)
    {
        var firstStar = first.Width.IsStar;
        var secondStar = second.Width.IsStar;
        if (firstStar && secondStar)
        {
            first.Width = new GridLength(Math.Max(1, firstSize), GridUnitType.Star);
            second.Width = new GridLength(Math.Max(1, secondSize), GridUnitType.Star);
        }
        else if (firstStar) second.Width = new GridLength(secondSize);
        else if (secondStar) first.Width = new GridLength(firstSize);
        else
        {
            first.Width = new GridLength(firstSize);
            second.Width = new GridLength(secondSize);
        }
    }

    private static void ApplyHeights(RowDefinition first, RowDefinition second, double firstSize, double secondSize)
    {
        var firstStar = first.Height.IsStar;
        var secondStar = second.Height.IsStar;
        if (firstStar && secondStar)
        {
            first.Height = new GridLength(Math.Max(1, firstSize), GridUnitType.Star);
            second.Height = new GridLength(Math.Max(1, secondSize), GridUnitType.Star);
        }
        else if (firstStar) second.Height = new GridLength(secondSize);
        else if (secondStar) first.Height = new GridLength(firstSize);
        else
        {
            first.Height = new GridLength(firstSize);
            second.Height = new GridLength(secondSize);
        }
    }
}
