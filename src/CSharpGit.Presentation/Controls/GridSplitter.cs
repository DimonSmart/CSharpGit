using System.Text.Json;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation.Controls;

public enum GridResizeDirection { Columns, Rows }
public enum GridResizeBehavior { PreviousAndNext, CurrentAndNext }

public sealed class GridSplitter : ContentControl
{
    private static readonly object SettingsLock = new();
    private readonly Border _indicator;
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

    public event EventHandler? ResizeCompleted;

    public GridSplitter()
    {
        MinWidth = MinHeight = 6;

        var surface = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0))
        };
        _indicator = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128)),
            Opacity = 0.42,
            IsHitTestVisible = false
        };
        surface.Children.Add(_indicator);
        Content = surface;

        PointerPressed += BeginResize;
        PointerMoved += ContinueResize;
        PointerReleased += EndResize;
        PointerCaptureLost += OnPointerCaptureLost;
        PointerEntered += (_, _) => SetIndicatorOpacity(_lastPosition is null ? 0.78 : 1.0);
        PointerExited += (_, _) => { if (_lastPosition is null) SetIndicatorOpacity(0.42); };
        Loaded += (_, _) => DispatcherQueue.TryEnqueue(RestoreSavedSize);
        UpdateDirectionVisuals();
    }

    internal bool HasVisualSurfaceForCheck => Content is Grid { Background: not null } && _indicator.Parent is not null;

    private static DependencyProperty Register(string name, Type type, object defaultValue, PropertyChangedCallback? callback = null) =>
        DependencyProperty.Register(name, type, typeof(GridSplitter), new PropertyMetadata(defaultValue, callback));

    private static void OnDirectionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs _) => ((GridSplitter)sender).UpdateDirectionVisuals();

    private void UpdateDirectionVisuals()
    {
        ProtectedCursor = InputSystemCursor.Create(
            ResizeDirection == GridResizeDirection.Columns ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth);

        if (ResizeDirection == GridResizeDirection.Columns)
        {
            _indicator.Width = 1;
            _indicator.Height = double.NaN;
            _indicator.HorizontalAlignment = HorizontalAlignment.Center;
            _indicator.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            _indicator.Width = double.NaN;
            _indicator.Height = 1;
            _indicator.HorizontalAlignment = HorizontalAlignment.Stretch;
            _indicator.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    private void BeginResize(object sender, PointerRoutedEventArgs args)
    {
        _lastPosition = Position(args);
        CapturePointer(args.Pointer);
        SetIndicatorOpacity(1.0);
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
        if (_lastPosition is null) return;
        _lastPosition = null;
        ReleasePointerCapture(args.Pointer);
        SetIndicatorOpacity(0.78);
        SaveCurrentSize();
        ResizeCompleted?.Invoke(this, EventArgs.Empty);
        args.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        if (_lastPosition is null) return;
        _lastPosition = null;
        SetIndicatorOpacity(0.42);
        SaveCurrentSize();
        ResizeCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void SetIndicatorOpacity(double value) => _indicator.Opacity = value;

    private Windows.Foundation.Point Position(PointerRoutedEventArgs args) =>
        Parent is UIElement parent ? args.GetCurrentPoint(parent).Position : args.GetCurrentPoint(this).Position;

    internal void ResizeForCheck(double delta)
    {
        if (Parent is Grid grid) Resize(grid, delta);
    }

    private void Resize(Grid grid, double delta)
    {
        if (!TryGetPair(grid, out var first, out var second)) return;

        if (ResizeDirection == GridResizeDirection.Columns)
        {
            var a = grid.ColumnDefinitions[first];
            var b = grid.ColumnDefinitions[second];
            ResizePair(a.ActualWidth, b.ActualWidth, delta,
                Math.Max(MinimumFirst, a.MinWidth), Math.Max(MinimumSecond, b.MinWidth),
                Math.Min(MaximumFirst, a.MaxWidth), Math.Min(MaximumSecond, b.MaxWidth),
                (x, y) => ApplyWidths(a, b, x, y));
        }
        else
        {
            var a = grid.RowDefinitions[first];
            var b = grid.RowDefinitions[second];
            ResizePair(a.ActualHeight, b.ActualHeight, delta,
                Math.Max(MinimumFirst, a.MinHeight), Math.Max(MinimumSecond, b.MinHeight),
                Math.Min(MaximumFirst, a.MaxHeight), Math.Min(MaximumSecond, b.MaxHeight),
                (x, y) => ApplyHeights(a, b, x, y));
        }
    }

    private bool TryGetPair(Grid grid, out int first, out int second)
    {
        var splitterIndex = ResizeDirection == GridResizeDirection.Columns ? Grid.GetColumn(this) : Grid.GetRow(this);
        first = ResizeBehavior == GridResizeBehavior.PreviousAndNext ? splitterIndex - 1 : splitterIndex;
        second = splitterIndex + 1;
        var count = ResizeDirection == GridResizeDirection.Columns ? grid.ColumnDefinitions.Count : grid.RowDefinitions.Count;
        return first >= 0 && second < count;
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

    private void SaveCurrentSize()
    {
        if (Parent is not Grid grid || !TryGetPair(grid, out var first, out var second)) return;

        SplitterState state;
        if (ResizeDirection == GridResizeDirection.Columns)
        {
            var a = grid.ColumnDefinitions[first];
            var b = grid.ColumnDefinitions[second];
            state = SplitterState.Create(a.ActualWidth, b.ActualWidth, a.Width.IsStar, b.Width.IsStar);
        }
        else
        {
            var a = grid.RowDefinitions[first];
            var b = grid.RowDefinitions[second];
            state = SplitterState.Create(a.ActualHeight, b.ActualHeight, a.Height.IsStar, b.Height.IsStar);
        }

        lock (SettingsLock)
        {
            var values = LoadSettings();
            values[BuildPersistenceKey()] = state;
            SaveSettings(values);
        }
    }

    private void RestoreSavedSize()
    {
        if (Parent is not Grid grid || !TryGetPair(grid, out var first, out var second)) return;
        SplitterState? state;
        lock (SettingsLock)
            state = LoadSettings().GetValueOrDefault(BuildPersistenceKey());
        if (state is null) return;

        if (ResizeDirection == GridResizeDirection.Columns)
        {
            var a = grid.ColumnDefinitions[first];
            var b = grid.ColumnDefinitions[second];
            RestorePair(state, a.ActualWidth + b.ActualWidth,
                Math.Max(MinimumFirst, a.MinWidth), Math.Max(MinimumSecond, b.MinWidth),
                Math.Min(MaximumFirst, a.MaxWidth), Math.Min(MaximumSecond, b.MaxWidth),
                a.Width.IsStar, b.Width.IsStar,
                (x, y) => ApplyWidths(a, b, x, y));
        }
        else
        {
            var a = grid.RowDefinitions[first];
            var b = grid.RowDefinitions[second];
            RestorePair(state, a.ActualHeight + b.ActualHeight,
                Math.Max(MinimumFirst, a.MinHeight), Math.Max(MinimumSecond, b.MinHeight),
                Math.Min(MaximumFirst, a.MaxHeight), Math.Min(MaximumSecond, b.MaxHeight),
                a.Height.IsStar, b.Height.IsStar,
                (x, y) => ApplyHeights(a, b, x, y));
        }
    }

    private static void RestorePair(SplitterState state, double total, double minFirst, double minSecond, double maxFirst, double maxSecond, bool firstStar, bool secondStar, Action<double, double> apply)
    {
        if (total <= 0) return;
        var requestedFirst = firstStar && secondStar
            ? total * Math.Clamp(state.FirstFraction, 0.05, 0.95)
            : firstStar && !secondStar
                ? total - state.SecondSize
                : state.FirstSize;
        var lower = Math.Max(minFirst, total - maxSecond);
        var upper = Math.Min(maxFirst, total - minSecond);
        if (upper < lower) return;
        var firstSize = Math.Clamp(requestedFirst, lower, upper);
        apply(firstSize, total - firstSize);
    }

    private string BuildPersistenceKey()
    {
        var parts = new Stack<string>();
        DependencyObject? current = this;
        for (var depth = 0; current is FrameworkElement element && depth < 12; depth++)
        {
            if (!string.IsNullOrWhiteSpace(element.Name))
                parts.Push(element.Name);
            else if (element.Parent is Grid)
                parts.Push($"{element.GetType().Name}[r{Grid.GetRow(element)}c{Grid.GetColumn(element)}]");
            else
                parts.Push(element.GetType().Name);
            current = element.Parent;
        }
        return $"{ResizeDirection}:{string.Join('/', parts)}";
    }

    private static Dictionary<string, SplitterState> LoadSettings()
    {
        try
        {
            var path = SettingsPath();
            if (!File.Exists(path)) return new Dictionary<string, SplitterState>(StringComparer.Ordinal);
            return JsonSerializer.Deserialize<Dictionary<string, SplitterState>>(File.ReadAllText(path))
                   ?? new Dictionary<string, SplitterState>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, SplitterState>(StringComparer.Ordinal);
        }
    }

    private static void SaveSettings(Dictionary<string, SplitterState> values)
    {
        try
        {
            var path = SettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(values));
        }
        catch
        {
            // Layout persistence must never make the application unusable.
        }
    }

    private static string SettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CSharpGit",
        "layout.json");

    private sealed record SplitterState(double FirstSize, double SecondSize, double FirstFraction)
    {
        public static SplitterState Create(double first, double second, bool firstStar, bool secondStar)
        {
            var total = first + second;
            var fraction = total > 0 ? first / total : 0.5;
            return new SplitterState(first, second, firstStar && secondStar ? fraction : fraction);
        }
    }
}
