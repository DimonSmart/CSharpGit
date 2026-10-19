using CSharpGit.Domain;
using CSharpGit.Presentation.Core.History;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation.Controls;

public sealed class HistoryReferencesPresenter : Panel
{
    private const int MaxSurplusVisuals = 12;
    private readonly List<ReferenceVisual> _visuals = [];
    private readonly List<HistoryReferenceBadgeLayout.Metrics> _metrics = [];
    private readonly RectangleGeometry _clip = new();
    private readonly Border _overflowBorder;
    private readonly TextBlock _overflowText;
    private readonly Border _overflowMeasureBorder;
    private readonly TextBlock _overflowMeasureText;
    private readonly TextBlock _minimumNameMeasureText;
    private readonly ReferenceVisual _measureVisual;
    private HistoryReferenceBadgeLayout.Plan _plan = HistoryReferenceBadgeLayout.Plan.Empty;
    private int _activeCount;
    private int _overflowVisibleCount = -1;
    private double _measuredBudget;

    public static readonly DependencyProperty ReferencesProperty = DependencyProperty.Register(
        nameof(References),
        typeof(IReadOnlyList<HistoryReferenceDecoration>),
        typeof(HistoryReferencesPresenter),
        new PropertyMetadata(null, OnReferencesChanged));

    public HistoryReferencesPresenter()
    {
        (_overflowBorder, _overflowText) = CreateOverflowVisual();
        (_overflowMeasureBorder, _overflowMeasureText) = CreateOverflowVisual();
        _measureVisual = CreateVisual();
        _minimumNameMeasureText = new TextBlock { Text = "mmmm…" };
        if (Microsoft.UI.Xaml.Application.Current.Resources["ReferenceBadgeTextStyle"] is Style style)
            _minimumNameMeasureText.Style = style;
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
        var unrestricted = new Size(double.PositiveInfinity, availableSize.Height);
        _minimumNameMeasureText.Measure(unrestricted);
        var minimumUsefulName = _minimumNameMeasureText.DesiredSize.Width;
        var naturalWidth = 0d;
        var height = 0d;
        _metrics.Clear();

        for (var i = 0; i < _activeCount; i++)
        {
            // Measure an unattached reusable probe. Changing Width/Visibility on
            // live visual children during Measure would invalidate their layout
            // repeatedly and can cause oscillation during fast resize.
            var visual = _measureVisual;
            var reference = References![i];
            visual.Text.Text = reference.DisplayName;
            UpdatePresentation(visual, reference, updateAccessibility: false);
            ResetBadgeSizing(visual);
            visual.Border.Measure(unrestricted);
            var full = visual.Border.DesiredSize.Width;
            var fullName = visual.Text.DesiredSize.Width;
            height = Math.Max(height, visual.Border.DesiredSize.Height);

            visual.UpstreamText.Visibility = Visibility.Collapsed;
            visual.Border.Measure(unrestricted);
            var compact = visual.Border.DesiredSize.Width;

            var minimumName = Math.Min(fullName, minimumUsefulName);
            visual.Text.Width = minimumName;
            visual.Border.Measure(unrestricted);
            var withCounters = visual.Border.DesiredSize.Width;

            visual.CountersText.Visibility = Visibility.Collapsed;
            visual.Border.Measure(unrestricted);
            var minimum = visual.Border.DesiredSize.Width;

            _metrics.Add(new HistoryReferenceBadgeLayout.Metrics(
                minimum, withCounters, compact, full, minimumName, fullName));
            naturalWidth += full;
        }

        if (_activeCount > 0)
        {
            _overflowMeasureText.Text = $"+{_activeCount}";
            _overflowMeasureBorder.Measure(unrestricted);
            height = Math.Max(height, _overflowMeasureBorder.DesiredSize.Height);
        }

        _measuredBudget = double.IsFinite(availableSize.Width)
            ? Math.Max(0, availableSize.Width)
            : naturalWidth;
        _plan = HistoryReferenceBadgeLayout.Calculate(_metrics, _measuredBudget, MeasureOverflow);
        ApplyPlan(availableSize.Height);
        var desiredWidth = Math.Min(naturalWidth, _measuredBudget);
        return new Size(desiredWidth, Math.Min(height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _clip.Rect = new Rect(0, 0, Math.Max(0, finalSize.Width), Math.Max(0, finalSize.Height));

        // In normal layout the final width equals the measured constraint. A
        // different arrange width still reuses measurements and the same plan.
        if (Math.Abs(_measuredBudget - finalSize.Width) > 0.01 && double.IsFinite(finalSize.Width))
        {
            _plan = HistoryReferenceBadgeLayout.Calculate(_metrics, Math.Max(0, finalSize.Width), MeasureOverflow);
            ApplyPlan(finalSize.Height);
        }

        var x = 0d;
        for (var index = 0; index < _activeCount; index++)
        {
            var child = _visuals[index].Border;
            if (index >= _plan.Visible.Count)
            {
                SetReferenceVisibility(child, false);
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            SetReferenceVisibility(child, true);
            var width = _plan.Visible[index].Width;
            child.Arrange(new Rect(x, 0, width, Math.Min(finalSize.Height, child.DesiredSize.Height)));
            x += width;
        }

        if (_plan.HiddenCount > 0 && _plan.OverflowWidth > 0)
        {
            UpdateOverflowPresentation(_plan.Visible.Count);
            _overflowBorder.Opacity = 1;
            _overflowBorder.IsHitTestVisible = true;
            _overflowBorder.Measure(new Size(_plan.OverflowWidth, finalSize.Height));
            _overflowBorder.Arrange(new Rect(
                x, 0, _plan.OverflowWidth, Math.Min(finalSize.Height, _overflowBorder.DesiredSize.Height)));
        }
        else
        {
            HideOverflow();
            _overflowBorder.Arrange(new Rect(0, 0, 0, 0));
        }

        return finalSize;
    }

    private double MeasureOverflow(int hidden)
    {
        _overflowMeasureText.Text = $"+{hidden}";
        _overflowMeasureBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _overflowMeasureBorder.DesiredSize.Width;
    }

    private void ApplyPlan(double height)
    {
        var constraint = new Size(double.PositiveInfinity, height);
        for (var i = 0; i < _activeCount; i++)
        {
            var visual = _visuals[i];
            if (i >= _plan.Visible.Count)
            {
                SetReferenceVisibility(visual.Border, false);
                continue;
            }

            var allocation = _plan.Visible[i];
            visual.Text.Width = allocation.NameWidth;
            visual.CountersText.Visibility = allocation.ShowCounters && !string.IsNullOrEmpty(visual.CountersText.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            visual.UpstreamText.Visibility = allocation.ShowUpstream && !string.IsNullOrEmpty(visual.UpstreamText.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            visual.Border.Measure(constraint);
            SetReferenceVisibility(visual.Border, true);
        }
    }

    private static void ResetBadgeSizing(ReferenceVisual visual)
    {
        visual.Text.Width = double.NaN;
        visual.CountersText.Visibility = string.IsNullOrEmpty(visual.CountersText.Text)
            ? Visibility.Collapsed : Visibility.Visible;
        visual.UpstreamText.Visibility = string.IsNullOrEmpty(visual.UpstreamText.Text)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateOverflowPresentation(int visibleCount)
    {
        if (_overflowVisibleCount == visibleCount)
            return;

        var hiddenCount = _activeCount - visibleCount;
        _overflowText.Text = $"+{hiddenCount}";
        ToolTipService.SetToolTip(_overflowBorder, BuildOverflowToolTip(visibleCount));
        AutomationProperties.SetName(_overflowBorder, $"{hiddenCount} hidden Git references: {BuildOverflowToolTip(visibleCount)}");
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
            tooltip.Append(" (");
            tooltip.Append(references[index].Kind);
            tooltip.Append(')');
        }

        return tooltip.ToString();
    }

    private void HideOverflow()
    {
        _overflowBorder.Opacity = 0;
        _overflowBorder.IsHitTestVisible = false;
        _overflowVisibleCount = -1;
        ToolTipService.SetToolTip(_overflowBorder, null);
        AutomationProperties.SetName(_overflowBorder, string.Empty);
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
            visual.Text.Width = double.NaN;
            if (!string.Equals(visual.Text.Text, reference.DisplayName, StringComparison.Ordinal))
                visual.Text.Text = reference.DisplayName;

            UpdatePresentation(visual, reference);
            SetReferenceVisibility(visual.Border, true);
            visual.Border.Visibility = Visibility.Visible;
        }

        for (var index = count; index < _visuals.Count; index++)
        {
            _visuals[index].Border.Visibility = Visibility.Collapsed;
            _visuals[index].Text.Width = double.NaN;
        }

        while (_visuals.Count - count > MaxSurplusVisuals)
        {
            var last = _visuals.Count - 1;
            Children.RemoveAt(last);
            _visuals.RemoveAt(last);
        }

        _activeCount = count;
        _plan = HistoryReferenceBadgeLayout.Plan.Empty;
        _metrics.Clear();
        HideOverflow();
        InvalidateMeasure();
    }

    private static void UpdatePresentation(
        ReferenceVisual visual, HistoryReferenceDecoration reference, bool updateAccessibility = true)
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
            ? Visibility.Visible : Visibility.Collapsed;

        var annotations = HistoryReferenceBadgeContent.Create(reference);
        var counters = annotations.Counters;
        visual.CountersText.Text = counters;
        visual.CountersText.Visibility = string.IsNullOrEmpty(counters)
            ? Visibility.Collapsed : Visibility.Visible;

        var upstream = annotations.Upstream;
        visual.UpstreamText.Text = upstream;
        visual.UpstreamText.Visibility = string.IsNullOrEmpty(upstream)
            ? Visibility.Collapsed : Visibility.Visible;

        visual.Border.BorderThickness = reference.Kind is HistoryReferenceKind.CurrentLocalBranch
            or HistoryReferenceKind.DetachedHead
            ? new Thickness(2) : new Thickness(1);

        if (updateAccessibility)
        {
            var tooltip = BuildToolTip(reference);
            ToolTipService.SetToolTip(visual.Border, tooltip);
            AutomationProperties.SetName(visual.Border, tooltip.Replace('\n', ' '));
        }
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

        var text = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        if (Microsoft.UI.Xaml.Application.Current.Resources["ReferenceBadgeTextStyle"] is Style textStyle)
            text.Style = textStyle;

        var countersText = CreateTrackingText();
        var upstreamText = CreateTrackingText();

        var content = new Grid { ColumnSpacing = 2, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 5; i++)
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        content.Children.Add(kindMarker);
        Grid.SetColumn(defaultBranchIcon, 1);
        content.Children.Add(defaultBranchIcon);
        Grid.SetColumn(text, 2);
        content.Children.Add(text);
        Grid.SetColumn(countersText, 3);
        content.Children.Add(countersText);
        Grid.SetColumn(upstreamText, 4);
        content.Children.Add(upstreamText);

        var border = new Border { Child = content };
        if (Microsoft.UI.Xaml.Application.Current.Resources["ReferenceBadgeStyle"] is Style borderStyle)
            border.Style = borderStyle;

        return new ReferenceVisual(border, kindMarker, defaultBranchIcon, text, countersText, upstreamText);
    }

    private static TextBlock CreateTrackingText()
    {
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        if (Microsoft.UI.Xaml.Application.Current.Resources["HistoryReferenceTrackingTextStyle"] is Style style)
            text.Style = style;
        return text;
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
        TextBlock CountersText,
        TextBlock UpstreamText);
}
