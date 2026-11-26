using CSharpGit.Application.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
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

    private bool _historyModeBound;

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
            if (_historyModeBound)
            {
                ClearValue(ModeProperty);
                _historyModeBound = false;
            }
        };
    }

    private void BindHistoryMode()
    {
        if (_historyModeBound) return;

        for (var ancestor = VisualTreeHelper.GetParent(this);
             ancestor is not null;
             ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is not ICommitTimeModeProvider provider) continue;
            if (provider.CommitTimeModeSource is { } source)
            {
                SetBinding(ModeProperty, new Binding
                {
                    Source = source,
                    Path = new PropertyPath(nameof(HistoryViewModel.CommitTimeDisplayMode)),
                    Mode = BindingMode.OneWay
                });
                _historyModeBound = true;
            }

            break;
        }
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
