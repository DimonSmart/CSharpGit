using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI.Text;

namespace CSharpGit.Presentation.Controls;

public sealed class MiddleEllipsisTextBlock : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(MiddleEllipsisTextBlock),
        new PropertyMetadata(string.Empty, OnDisplayPropertyChanged));

    public static readonly DependencyProperty UseMiddleEllipsisProperty = DependencyProperty.Register(
        nameof(UseMiddleEllipsis),
        typeof(bool),
        typeof(MiddleEllipsisTextBlock),
        new PropertyMetadata(false, OnDisplayPropertyChanged));

    public static readonly DependencyProperty TextStyleProperty = DependencyProperty.Register(
        nameof(TextStyle),
        typeof(Style),
        typeof(MiddleEllipsisTextBlock),
        new PropertyMetadata(null, OnDisplayPropertyChanged));

    public static readonly DependencyProperty TextFontWeightProperty = DependencyProperty.Register(
        nameof(TextFontWeight),
        typeof(FontWeight),
        typeof(MiddleEllipsisTextBlock),
        new PropertyMetadata(Microsoft.UI.Text.FontWeights.Normal, OnDisplayPropertyChanged));

    public static readonly DependencyProperty TextForegroundProperty = DependencyProperty.Register(
        nameof(TextForeground),
        typeof(Brush),
        typeof(MiddleEllipsisTextBlock),
        new PropertyMetadata(null, OnDisplayPropertyChanged));

    private readonly TextBlock _textBlock = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap
    };

    private readonly TextBlock _measurementBlock = new()
    {
        TextWrapping = TextWrapping.NoWrap
    };

    public MiddleEllipsisTextBlock()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        Content = _textBlock;
        SizeChanged += (_, _) => UpdateDisplay();
        Loaded += (_, _) => UpdateDisplay();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool UseMiddleEllipsis
    {
        get => (bool)GetValue(UseMiddleEllipsisProperty);
        set => SetValue(UseMiddleEllipsisProperty, value);
    }

    public Style? TextStyle
    {
        get => (Style?)GetValue(TextStyleProperty);
        set => SetValue(TextStyleProperty, value);
    }

    public FontWeight TextFontWeight
    {
        get => (FontWeight)GetValue(TextFontWeightProperty);
        set => SetValue(TextFontWeightProperty, value);
    }

    public Brush? TextForeground
    {
        get => (Brush?)GetValue(TextForegroundProperty);
        set => SetValue(TextForegroundProperty, value);
    }

    private static void OnDisplayPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((MiddleEllipsisTextBlock)dependencyObject).UpdateDisplay();

    private void UpdateDisplay()
    {
        ApplyTypography(_textBlock);
        ApplyTypography(_measurementBlock);

        if (!UseMiddleEllipsis)
        {
            _textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            _textBlock.Text = Text ?? string.Empty;
            return;
        }

        _textBlock.TextTrimming = TextTrimming.None;
        var availableWidth = ActualWidth;
        _textBlock.Text = availableWidth <= 0
            ? Text ?? string.Empty
            : MiddleEllipsis.Fit(Text, availableWidth, MeasureText);
    }

    private void ApplyTypography(TextBlock textBlock)
    {
        textBlock.Style = TextStyle;
        textBlock.FontWeight = TextFontWeight;
        if (TextForeground is null)
            textBlock.ClearValue(TextBlock.ForegroundProperty);
        else
            textBlock.Foreground = TextForeground;
    }

    private double MeasureText(string text)
    {
        _measurementBlock.Text = text;
        _measurementBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _measurementBlock.DesiredSize.Width;
    }
}
