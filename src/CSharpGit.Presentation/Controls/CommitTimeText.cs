using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Controls;

internal interface ICommitTimeModeProvider
{
    HistoryViewModel? CommitTimeModeSource { get; }
}

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

    public static readonly DependencyProperty IsTextSelectionEnabledProperty = DependencyProperty.Register(
        nameof(IsTextSelectionEnabled),
        typeof(bool),
        typeof(CommitTimeText),
        new PropertyMetadata(false, OnTextSelectionEnabledChanged));

    private readonly TextBlock _textBlock = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };

    private HistoryViewModel? _historyModeSource;

    public CommitTimeText()
    {
        Content = _textBlock;
        _timer.Tick += (_, _) => UpdateDisplay();
        Loaded += (_, _) =>
        {
            BindHistoryMode();
            _timer.Start();
            UpdateDisplay();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            DetachHistoryMode();
        };
        DataContextChanged += (_, _) =>
        {
            if (IsLoaded) BindHistoryMode();
        };
        SizeChanged += (_, _) =>
        {
            if (IsLoaded && _historyModeSource is null) BindHistoryMode();
        };
    }

    private void BindHistoryMode()
    {
        HistoryViewModel? source = null;
        DependencyObject? ancestor = this;
        for (var depth = 0; ancestor is not null && depth < 64; depth++)
        {
            if (ancestor is ICommitTimeModeProvider provider)
            {
                source = provider.CommitTimeModeSource;
                break;
            }

            ancestor = (ancestor as FrameworkElement)?.Parent
                ?? VisualTreeHelper.GetParent(ancestor);
        }

        if (!ReferenceEquals(source, _historyModeSource))
        {
            DetachHistoryMode();
            _historyModeSource = source;
            if (source is not null)
                source.PropertyChanged += HistoryModeChanged;
        }

        if (source is not null)
            Mode = source.CommitTimeDisplayMode;
    }

    private void DetachHistoryMode()
    {
        if (_historyModeSource is not null)
            _historyModeSource.PropertyChanged -= HistoryModeChanged;
        _historyModeSource = null;
    }

    private void HistoryModeChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (ReferenceEquals(sender, _historyModeSource)
            && args.PropertyName == nameof(HistoryViewModel.CommitTimeDisplayMode)
            && _historyModeSource is { } source)
            Mode = source.CommitTimeDisplayMode;
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

    public bool IsTextSelectionEnabled
    {
        get => (bool)GetValue(IsTextSelectionEnabledProperty);
        set => SetValue(IsTextSelectionEnabledProperty, value);
    }

    public string Text => _textBlock.Text;

    private static void OnDisplayPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((CommitTimeText)dependencyObject).UpdateDisplay();

    private static void OnTextSelectionEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((CommitTimeText)dependencyObject)._textBlock.IsTextSelectionEnabled = (bool)args.NewValue;

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
