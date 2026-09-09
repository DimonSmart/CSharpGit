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

    public static readonly DependencyProperty UseGlobalModeProperty = DependencyProperty.Register(
        nameof(UseGlobalMode),
        typeof(bool),
        typeof(CommitTimeText),
        new PropertyMetadata(true, OnDisplayPropertyChanged));

    private readonly TextBlock _textBlock = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private IAppSettingsService? _settings;
    private bool _isLoaded;

    public CommitTimeText()
    {
        Content = _textBlock;
        _timer.Tick += (_, _) => UpdateDisplay();
        Loaded += CommitTimeText_Loaded;
        Unloaded += CommitTimeText_Unloaded;
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

    public bool UseGlobalMode
    {
        get => (bool)GetValue(UseGlobalModeProperty);
        set => SetValue(UseGlobalModeProperty, value);
    }

    public string Text => _textBlock.Text;

    private static void OnDisplayPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (CommitTimeText)dependencyObject;
        control.SyncSettingsSubscription();
        control.UpdateDisplay();
    }

    private void CommitTimeText_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        SyncSettingsSubscription();
        _timer.Start();
        UpdateDisplay();
    }

    private void CommitTimeText_Unloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _timer.Stop();
        UnsubscribeFromSettings();
    }

    private void SyncSettingsSubscription()
    {
        if (!_isLoaded) return;
        if (!UseGlobalMode)
        {
            UnsubscribeFromSettings();
            return;
        }

        var settings = AppSettingsContext.Current;
        if (ReferenceEquals(_settings, settings)) return;
        UnsubscribeFromSettings();
        _settings = settings;
        _settings.Changed += Settings_Changed;
    }

    private void UnsubscribeFromSettings()
    {
        if (_settings is null) return;
        _settings.Changed -= Settings_Changed;
        _settings = null;
    }

    private void Settings_Changed(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess) UpdateDisplay();
        else DispatcherQueue.TryEnqueue(UpdateDisplay);
    }

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

        var mode = UseGlobalMode
            ? (_settings ?? AppSettingsContext.Current).CommitTimeDisplayMode
            : Mode;
        _textBlock.Text = CommitTimeFormatter.Format(Value, mode, DateTimeOffset.Now);
        ToolTipService.SetToolTip(this, CommitTimeFormatter.FormatExactLocal(Value));
    }
}
