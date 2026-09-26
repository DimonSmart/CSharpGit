using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Controls;

public sealed class HistoryReferencesPresenter : Panel
{
    private const int MaxSurplusVisuals = 12;
    private readonly List<ReferenceVisual> _visuals = [];
    private int _activeCount;
    private bool _presentationContextSubscribed;

    public static readonly DependencyProperty ReferencesProperty = DependencyProperty.Register(
        nameof(References),
        typeof(IReadOnlyList<string>),
        typeof(HistoryReferencesPresenter),
        new PropertyMetadata(null, OnReferencesChanged));

    public HistoryReferencesPresenter()
    {
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

        if (double.IsFinite(availableSize.Width))
            width = Math.Min(width, availableSize.Width);
        if (double.IsFinite(availableSize.Height))
            height = Math.Min(height, availableSize.Height);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;
        for (var index = 0; index < _activeCount; index++)
        {
            var child = _visuals[index].Border;
            var desired = child.DesiredSize;
            child.Arrange(new Rect(x, 0, desired.Width, Math.Min(finalSize.Height, desired.Height)));
            x += desired.Width;
        }

        return finalSize;
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
                Children.Add(visual.Border);
                HistoryRenderDiagnostics.ReferenceVisualCreated();
            }

            var referenceName = references![index] ?? string.Empty;
            if (!string.Equals(visual.Text.Text, referenceName, StringComparison.Ordinal))
                visual.Text.Text = referenceName;

            UpdateMarker(visual, referenceName);
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

    private sealed record ReferenceVisual(
        Border Border,
        FontIcon DefaultBranchIcon,
        TextBlock Text);
}
