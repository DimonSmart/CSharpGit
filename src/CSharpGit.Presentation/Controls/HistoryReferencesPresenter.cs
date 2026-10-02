using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation.Controls;

public sealed class HistoryReferencesPresenter : Panel
{
    private const int MaxSurplusVisuals = 12;
    private readonly List<ReferenceVisual> _visuals = [];
    private readonly RectangleGeometry _clip = new();
    private readonly Border _overflowBorder;
    private readonly TextBlock _overflowText;
    private readonly Border _overflowMeasureBorder;
    private readonly TextBlock _overflowMeasureText;
    private int _activeCount;
    private int _overflowVisibleCount = -1;
    private bool _presentationContextSubscribed;

    public static readonly DependencyProperty ReferencesProperty = DependencyProperty.Register(
        nameof(References),
        typeof(IReadOnlyList<string>),
        typeof(HistoryReferencesPresenter),
        new PropertyMetadata(null, OnReferencesChanged));

    public HistoryReferencesPresenter()
    {
        (_overflowBorder, _overflowText) = CreateOverflowVisual();
        (_overflowMeasureBorder, _overflowMeasureText) = CreateOverflowVisual();
        Children.Add(_overflowBorder);
        Clip = _clip;

        Loaded += HistoryReferencesPresenter_Loaded;
        Unloaded += HistoryReferencesPresenter_Unloaded;
    }

    public IReadOnlyList<string>? References
    {
        get => GetValue(ReferencesProperty) as IReadOnlyList<string>;
        set => SetValue(ReferencesProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0d;
        var height = 0d;
        var childAvailableSize = new Size(double.PositiveInfinity, availableSize.Height);

        for (var index = 0; index < _activeCount; index++)
        {
            var child = _visuals[index].Border;
            child.Measure(childAvailableSize);
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        if (_activeCount > 0)
        {
            var maximumOverflowText = $"+{_activeCount}";
            if (!string.Equals(_overflowMeasureText.Text, maximumOverflowText, StringComparison.Ordinal))
                _overflowMeasureText.Text = maximumOverflowText;

            _overflowMeasureBorder.Measure(childAvailableSize);
            _overflowBorder.Measure(childAvailableSize);
            height = Math.Max(height, _overflowMeasureBorder.DesiredSize.Height);
        }

        if (double.IsFinite(availableSize.Width))
            width = Math.Min(width, availableSize.Width);
        if (double.IsFinite(availableSize.Height))
            height = Math.Min(height, availableSize.Height);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _clip.Rect = new Rect(
            0,
            0,
            Math.Max(0, finalSize.Width),
            Math.Max(0, finalSize.Height));

        var totalWidth = 0d;
        for (var index = 0; index < _activeCount; index++)
            totalWidth += _visuals[index].Border.DesiredSize.Width;

        var overflowRequired = totalWidth > finalSize.Width;
        var overflowWidth = overflowRequired ? _overflowMeasureBorder.DesiredSize.Width : 0d;
        var showOverflow = overflowRequired && overflowWidth <= finalSize.Width;
        var availableForReferences = showOverflow
            ? Math.Max(0, finalSize.Width - overflowWidth)
            : Math.Max(0, finalSize.Width);
        var visibleCount = GetVisibleReferenceCount(availableForReferences);

        var x = 0d;
        for (var index = 0; index < _activeCount; index++)
        {
            var child = _visuals[index].Border;
            if (index < visibleCount)
            {
                SetReferenceVisibility(child, true);
                var desired = child.DesiredSize;
                child.Arrange(new Rect(x, 0, desired.Width, Math.Min(finalSize.Height, desired.Height)));
                x += desired.Width;
            }
            else
            {
                SetReferenceVisibility(child, false);
                child.Arrange(new Rect(0, 0, 0, 0));
            }
        }

        if (showOverflow)
        {
            UpdateOverflowPresentation(visibleCount);
            _overflowBorder.Opacity = 1;
            _overflowBorder.IsHitTestVisible = true;
            _overflowBorder.Measure(new Size(overflowWidth, finalSize.Height));
            _overflowBorder.Arrange(new Rect(
                x,
                0,
                overflowWidth,
                Math.Min(finalSize.Height, _overflowBorder.DesiredSize.Height)));
        }
        else
        {
            HideOverflow();
            _overflowBorder.Arrange(new Rect(0, 0, 0, 0));
        }

        return finalSize;
    }

    private int GetVisibleReferenceCount(double availableWidth)
    {
        var width = 0d;
        var visibleCount = 0;

        for (var index = 0; index < _activeCount; index++)
        {
            var childWidth = _visuals[index].Border.DesiredSize.Width;
            if (width + childWidth > availableWidth)
                break;

            width += childWidth;
            visibleCount++;
        }

        return visibleCount;
    }

    private void UpdateOverflowPresentation(int visibleCount)
    {
        if (_overflowVisibleCount == visibleCount)
            return;

        var hiddenCount = _activeCount - visibleCount;
        _overflowText.Text = $"+{hiddenCount}";
        ToolTipService.SetToolTip(_overflowBorder, BuildOverflowToolTip(visibleCount));
        _overflowVisibleCount = visibleCount;
    }

    private string BuildOverflowToolTip(int visibleCount)
    {
        var references = References;
        if (references is null || visibleCount >= references.Count)
            return string.Empty;

        var tooltip = new System.Text.StringBuilder();
        for (var index = visibleCount; index < references.Count; index++)
        {
            if (tooltip.Length > 0)
                tooltip.AppendLine();
            tooltip.Append(references[index]);
        }

        return tooltip.ToString();
    }

    private void HideOverflow()
    {
        _overflowBorder.Opacity = 0;
        _overflowBorder.IsHitTestVisible = false;
        _overflowVisibleCount = -1;
    }

    private static void SetReferenceVisibility(Border border, bool visible)
    {
        border.Opacity = visible ? 1 : 0;
        border.IsHitTestVisible = visible;
    }

    private static void OnReferencesChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((HistoryReferencesPresenter)dependencyObject).UpdateReferences();

    private void HistoryReferencesPresenter_Loaded(object sender, RoutedEventArgs args)
    {
        if (!_presentationContextSubscribed)
        {
            HistoryReferencePresentationContext.Changed += PresentationContext_Changed;
            _presentationContextSubscribed = true;
        }

        RefreshMarkers();
    }

    private void HistoryReferencesPresenter_Unloaded(object sender, RoutedEventArgs args)
    {
        if (!_presentationContextSubscribed)
            return;

        HistoryReferencePresentationContext.Changed -= PresentationContext_Changed;
        _presentationContextSubscribed = false;
    }

    private void PresentationContext_Changed(object? sender, EventArgs args)
    {
        HistoryRenderDiagnostics.ReferencePresentationContextUpdated();
        RefreshMarkers();
    }

    private void UpdateReferences()
    {
        HistoryRenderDiagnostics.ReferencesPresenterUpdated();
        var references = References;
        var count = references?.Count ?? 0;

        for (var index = 0; index < count; index++)
        {
            ReferenceVisual visual;
            if (index < _visuals.Count)
            {
                visual = _visuals[index];
                HistoryRenderDiagnostics.ReferenceVisualReused();
            }
            else
            {
                visual = CreateVisual();
                _visuals.Add(visual);
                Children.Insert(Children.Count - 1, visual.Border);
                HistoryRenderDiagnostics.ReferenceVisualCreated();
            }

            var referenceName = references![index] ?? string.Empty;
            if (!string.Equals(visual.Text.Text, referenceName, StringComparison.Ordinal))
                visual.Text.Text = referenceName;

            UpdateMarker(visual, referenceName);
            SetReferenceVisibility(visual.Border, true);
            if (visual.Border.Visibility != Visibility.Visible)
                visual.Border.Visibility = Visibility.Visible;
        }

        for (var index = count; index < _visuals.Count; index++)
            _visuals[index].Border.Visibility = Visibility.Collapsed;

        while (_visuals.Count - count > MaxSurplusVisuals)
        {
            var last = _visuals.Count - 1;
            Children.RemoveAt(last);
            _visuals.RemoveAt(last);
        }

        _activeCount = count;
        HideOverflow();
        InvalidateMeasure();
    }

    private void RefreshMarkers()
    {
        for (var index = 0; index < _activeCount; index++)
        {
            var visual = _visuals[index];
            UpdateMarker(visual, visual.Text.Text);
        }
    }

    private static void UpdateMarker(ReferenceVisual visual, string referenceName)
    {
        var visibility = HistoryReferencePresentationContext.IsDefaultRemoteBranch(referenceName)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (visual.DefaultBranchIcon.Visibility != visibility)
            visual.DefaultBranchIcon.Visibility = visibility;
    }

    private static ReferenceVisual CreateVisual()
    {
        var icon = new FontIcon();
        if (Microsoft.UI.Xaml.Application.Current.Resources["HistoryReferenceDefaultBranchIconStyle"] is Style iconStyle)
            icon.Style = iconStyle;

        var text = new TextBlock();
        if (Microsoft.UI.Xaml.Application.Current.Resources["ReferenceBadgeTextStyle"] is Style textStyle)
            text.Style = textStyle;
        text.VerticalAlignment = VerticalAlignment.Center;

        var content = new Grid
        {
            ColumnSpacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(icon);
        Grid.SetColumn(text, 1);
        content.Children.Add(text);

        var border = new Border { Child = content };
        if (Microsoft.UI.Xaml.Application.Current.Resources["ReferenceBadgeStyle"] is Style borderStyle)
            border.Style = borderStyle;

        return new ReferenceVisual(border, icon, text);
    }

    private static (Border Border, TextBlock Text) CreateOverflowVisual()
    {
        var text = new TextBlock();
        if (Microsoft.UI.Xaml.Application.Current.Resources["ReferenceBadgeTextStyle"] is Style textStyle)
            text.Style = textStyle;
        text.VerticalAlignment = VerticalAlignment.Center;

        var border = new Border
        {
            Child = text,
            Opacity = 0,
            IsHitTestVisible = false
        };
        if (Microsoft.UI.Xaml.Application.Current.Resources["ReferenceBadgeStyle"] is Style borderStyle)
            border.Style = borderStyle;

        return (border, text);
    }

    private sealed record ReferenceVisual(
        Border Border,
        FontIcon DefaultBranchIcon,
        TextBlock Text);
}
