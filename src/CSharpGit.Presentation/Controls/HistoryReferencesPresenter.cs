using CSharpGit.Domain;
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

    public static readonly DependencyProperty ReferencesProperty = DependencyProperty.Register(
        nameof(References),
        typeof(IReadOnlyList<HistoryReferenceDecoration>),
        typeof(HistoryReferencesPresenter),
        new PropertyMetadata(null, OnReferencesChanged));

    public HistoryReferencesPresenter()
    {
        (_overflowBorder, _overflowText) = CreateOverflowVisual();
        (_overflowMeasureBorder, _overflowMeasureText) = CreateOverflowVisual();
        Children.Add(_overflowBorder);
        Clip = _clip;
    }

    public IReadOnlyList<HistoryReferenceDecoration>? References
    {
        get => GetValue(ReferencesProperty) as IReadOnlyList<HistoryReferenceDecoration>;
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
            tooltip.Append(references[index].DisplayName);
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

            var reference = references![index];
            if (!string.Equals(visual.Text.Text, reference.DisplayName, StringComparison.Ordinal))
                visual.Text.Text = reference.DisplayName;

            UpdatePresentation(visual, reference);
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

    private static void UpdatePresentation(
        ReferenceVisual visual,
        HistoryReferenceDecoration reference)
    {
        var marker = reference.Kind switch
        {
            HistoryReferenceKind.CurrentLocalBranch => "●",
            HistoryReferenceKind.LocalBranch => "○",
            HistoryReferenceKind.RemoteTrackingBranch => "↗",
            HistoryReferenceKind.Tag => "#",
            HistoryReferenceKind.DetachedHead => "◇",
            _ => "•"
        };
        if (!string.Equals(visual.KindMarker.Text, marker, StringComparison.Ordinal))
            visual.KindMarker.Text = marker;

        visual.DefaultBranchIcon.Visibility = reference.IsDefault
            ? Visibility.Visible
            : Visibility.Collapsed;

        var tracking = BuildTrackingAnnotation(reference);
        if (!string.Equals(visual.TrackingText.Text, tracking, StringComparison.Ordinal))
            visual.TrackingText.Text = tracking;
        visual.TrackingText.Visibility = string.IsNullOrEmpty(tracking)
            ? Visibility.Collapsed
            : Visibility.Visible;

        visual.Border.BorderThickness = reference.Kind is HistoryReferenceKind.CurrentLocalBranch
            or HistoryReferenceKind.DetachedHead
            ? new Thickness(2)
            : new Thickness(1);

        ToolTipService.SetToolTip(visual.Border, BuildToolTip(reference));
    }

    private static string BuildTrackingAnnotation(HistoryReferenceDecoration reference)
    {
        if (reference.Kind != HistoryReferenceKind.CurrentLocalBranch
            || string.IsNullOrWhiteSpace(reference.Upstream))
            return string.Empty;

        var counters = reference.Ahead > 0 && reference.Behind > 0
            ? $"↑{reference.Ahead} ↓{reference.Behind} "
            : reference.Ahead > 0
                ? $"↑{reference.Ahead} "
                : reference.Behind > 0
                    ? $"↓{reference.Behind} "
                    : string.Empty;

        return $"{counters}· upstream {reference.Upstream}";
    }

    private static string BuildToolTip(HistoryReferenceDecoration reference) =>
        reference.Kind switch
        {
            HistoryReferenceKind.CurrentLocalBranch =>
                string.IsNullOrWhiteSpace(reference.Upstream)
                    ? $"{reference.DisplayName} is the current local branch."
                    : $"{reference.DisplayName} is the current local branch.\nUpstream: {reference.Upstream}\nAhead: {reference.Ahead}, behind: {reference.Behind}.",
            HistoryReferenceKind.LocalBranch =>
                $"{reference.DisplayName} is a local branch.",
            HistoryReferenceKind.RemoteTrackingBranch =>
                $"{reference.DisplayName} is a local remote-tracking reference.\nIt represents the remote state known after the latest fetch-like operation and may differ from the current state on the server.",
            HistoryReferenceKind.Tag =>
                $"{reference.DisplayName} is a Git tag.",
            HistoryReferenceKind.DetachedHead =>
                "HEAD is detached at this commit.",
            _ => reference.DisplayName
        };

    private static ReferenceVisual CreateVisual()
    {
        var kindMarker = new TextBlock();
        if (Microsoft.UI.Xaml.Application.Current.Resources["HistoryReferenceKindMarkerStyle"] is Style markerStyle)
            kindMarker.Style = markerStyle;

        var defaultBranchIcon = new FontIcon();
        if (Microsoft.UI.Xaml.Application.Current.Resources["HistoryReferenceDefaultBranchIconStyle"] is Style iconStyle)
            defaultBranchIcon.Style = iconStyle;

        var text = new TextBlock();
        if (Microsoft.UI.Xaml.Application.Current.Resources["ReferenceBadgeTextStyle"] is Style textStyle)
            text.Style = textStyle;
        text.VerticalAlignment = VerticalAlignment.Center;

        var trackingText = new TextBlock();
        if (Microsoft.UI.Xaml.Application.Current.Resources["HistoryReferenceTrackingTextStyle"] is Style trackingStyle)
            trackingText.Style = trackingStyle;

        var content = new Grid
        {
            ColumnSpacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        content.Children.Add(kindMarker);

        Grid.SetColumn(defaultBranchIcon, 1);
        content.Children.Add(defaultBranchIcon);

        Grid.SetColumn(text, 2);
        content.Children.Add(text);

        Grid.SetColumn(trackingText, 3);
        content.Children.Add(trackingText);

        var border = new Border { Child = content };
        if (Microsoft.UI.Xaml.Application.Current.Resources["ReferenceBadgeStyle"] is Style borderStyle)
            border.Style = borderStyle;

        return new ReferenceVisual(border, kindMarker, defaultBranchIcon, text, trackingText);
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
        TextBlock KindMarker,
        FontIcon DefaultBranchIcon,
        TextBlock Text,
        TextBlock TrackingText);
}
