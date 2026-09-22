using CSharpGit.Application.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Controls;

public sealed class CommitTimeText : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(DateTimeOffset),
        typeof(CommitTimeText),
        new PropertyMetadata(default(DateTimeOffset), OnDisplayPropertyChanged));

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode),
        typeof(CommitTimeDisplayMode),
        typeof(CommitTimeText),
        new PropertyMetadata(CommitTimeDisplayMode.Smart, OnDisplayPropertyChanged));

    private readonly TextBlock _textBlock = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _isLoaded;

    public CommitTimeText()
    {
        Content = _textBlock;
        _timer.Tick += (_, _) => UpdateDisplay();
        Loaded += (_, _) =>
        {
            _timer.Start();
            UpdateDisplay();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    public DateTimeOffset Value
    {
        get => (DateTimeOffset)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public CommitTimeDisplayMode Mode
    {
        get => (CommitTimeDisplayMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public string Text => _textBlock.Text;

    private static void OnDisplayPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((CommitTimeText)dependencyObject).UpdateDisplay();

    private void UpdateDisplay()
    {
        _textBlock.FontSize = FontSize;
        _textBlock.FontFamily = FontFamily;
        _textBlock.FontWeight = FontWeight;
        _textBlock.Foreground = Foreground;

        if (Value == default)
        {
            _textBlock.Text = string.Empty;
            ToolTipService.SetToolTip(this, null);
            return;
        }

        _textBlock.Text = CommitTimeFormatter.Format(Value, Mode, DateTimeOffset.Now);
        ToolTipService.SetToolTip(this, CommitTimeFormatter.FormatExactLocal(Value));
    }
}
