using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation.Controls;

public sealed partial class InteractiveRebaseTodoEditor : UserControl
{
    private ScrollViewer? _editorScrollViewer;
    private ScrollViewer? _overlayScrollViewer;
    private ScrollViewer? _gutterScrollViewer;
    private bool _updatingText;

    public InteractiveRebaseTodoEditor()
    {
        InitializeComponent();
        Loaded += InteractiveRebaseTodoEditor_Loaded;
        Unloaded += InteractiveRebaseTodoEditor_Unloaded;
        UpdatePresentation(string.Empty);
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(InteractiveRebaseTodoEditor),
        new PropertyMetadata(string.Empty, TextPropertyChanged));

    public string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    internal void FocusEditor()
    {
        Editor.Focus(FocusState.Programmatic);
        Editor.Select(Editor.Text.Length, 0);
    }

    private static void TextPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var control = (InteractiveRebaseTodoEditor)dependencyObject;
        if (control._updatingText)
            return;

        var text = args.NewValue as string ?? string.Empty;
        control._updatingText = true;
        try
        {
            if (!string.Equals(control.Editor.Text, text, StringComparison.Ordinal))
                control.Editor.Text = text;
        }
        finally
        {
            control._updatingText = false;
        }

        control.UpdatePresentation(text);
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingText)
            return;

        var text = Editor.Text ?? string.Empty;
        _updatingText = true;
        try
        {
            SetValue(TextProperty, text);
        }
        finally
        {
            _updatingText = false;
        }

        UpdatePresentation(text);
    }

    private void InteractiveRebaseTodoEditor_Loaded(object sender, RoutedEventArgs e)
    {
        AttachScrollViewers();
        DispatcherQueue.TryEnqueue(AttachScrollViewers);
    }

    private void InteractiveRebaseTodoEditor_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_editorScrollViewer is not null)
            _editorScrollViewer.ViewChanged -= EditorScrollViewer_ViewChanged;

        _editorScrollViewer = null;
        _overlayScrollViewer = null;
        _gutterScrollViewer = null;
    }

    private void AttachScrollViewers()
    {
        Editor.ApplyTemplate();
        ExecutableTextOverlay.ApplyTemplate();
        LineNumberGutter.ApplyTemplate();

        var editorScrollViewer = FindDescendant<ScrollViewer>(Editor);
        if (!ReferenceEquals(_editorScrollViewer, editorScrollViewer))
        {
            if (_editorScrollViewer is not null)
                _editorScrollViewer.ViewChanged -= EditorScrollViewer_ViewChanged;

            _editorScrollViewer = editorScrollViewer;
            if (_editorScrollViewer is not null)
                _editorScrollViewer.ViewChanged += EditorScrollViewer_ViewChanged;
        }

        _overlayScrollViewer = FindDescendant<ScrollViewer>(ExecutableTextOverlay);
        _gutterScrollViewer = FindDescendant<ScrollViewer>(LineNumberGutter);
        SynchronizeScroll();
    }

    private void EditorScrollViewer_ViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs e) =>
        SynchronizeScroll();

    private void SynchronizeScroll()
    {
        if (_editorScrollViewer is null)
            return;

        _overlayScrollViewer?.ChangeView(
            _editorScrollViewer.HorizontalOffset,
            _editorScrollViewer.VerticalOffset,
            null,
            true);
        _gutterScrollViewer?.ChangeView(
            0,
            _editorScrollViewer.VerticalOffset,
            null,
            true);
    }

    private void UpdatePresentation(string text)
    {
        ExecutableTextOverlay.Text = BuildExecutableOverlay(text);

        var lineCount = CountLines(text);
        LineNumberGutter.Text = BuildLineNumbers(lineCount);

        var digitCount = lineCount.ToString(CultureInfo.InvariantCulture).Length;
        GutterColumn.Width = new GridLength(Math.Max(44, 28 + digitCount * 8));

        DispatcherQueue.TryEnqueue(SynchronizeScroll);
    }

    private static string BuildExecutableOverlay(string text)
    {
        if (text.Length == 0)
            return string.Empty;

        var result = new StringBuilder(text.Length);
        var lineStart = 0;

        for (var index = 0; index <= text.Length; index++)
        {
            if (index < text.Length
                && text[index] != '\r'
                && text[index] != '\n')
            {
                continue;
            }

            var line = text.AsSpan(lineStart, index - lineStart);
            if (IsCommentLine(line))
            {
                foreach (var character in line)
                    result.Append(character == '\t' ? '\t' : ' ');
            }
            else
            {
                result.Append(line);
            }

            if (index < text.Length)
            {
                if (text[index] == '\r'
                    && index + 1 < text.Length
                    && text[index + 1] == '\n')
                {
                    result.Append("\r\n");
                    index++;
                }
                else
                {
                    result.Append(text[index]);
                }
            }

            lineStart = index + 1;
        }

        return result.ToString();
    }

    private static bool IsCommentLine(ReadOnlySpan<char> line)
    {
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;

        return index < line.Length && line[index] == '#';
    }

    private static int CountLines(string text)
    {
        var lineCount = 1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                lineCount++;
                if (index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
            }
            else if (text[index] == '\n')
            {
                lineCount++;
            }
        }

        return lineCount;
    }

    private static string BuildLineNumbers(int lineCount)
    {
        var result = new StringBuilder(lineCount * 4);
        for (var line = 1; line <= lineCount; line++)
        {
            if (line > 1)
                result.Append(Environment.NewLine);

            result.Append(line.ToString(CultureInfo.InvariantCulture));
        }

        return result.ToString();
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                return match;

            if (FindDescendant<T>(child) is { } descendant)
                return descendant;
        }

        return null;
    }
}
