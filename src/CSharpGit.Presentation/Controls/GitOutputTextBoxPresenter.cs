using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation.Controls;

/// <summary>Shows streamed text without stealing the user's scroll position or selection.</summary>
internal sealed class GitOutputTextBoxPresenter
{
    private readonly TextBox _textBox;
    private ScrollViewer? _viewport;
    private GitOutputDisplayBuffer? _deferredBuffer;
    private string? _deferredPreview;
    private bool _followTail = true;
    private bool _ignoreViewChanges;
    private bool _updatingText;
    private int _scrollVersion;

    public GitOutputTextBoxPresenter(TextBox textBox)
    {
        _textBox = textBox ?? throw new ArgumentNullException(nameof(textBox));
        _textBox.Loaded += (_, _) =>
        {
            FindViewport();
            if (_followTail)
                ScheduleViewportAdjustment(followTail: true, previousOffset: null);
        };
        _textBox.SelectionChanged += (_, _) => FlushDeferred();
        _textBox.LostFocus += (_, _) => FlushDeferred();
    }

    public void Reset(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _followTail = true;
        _deferredBuffer = null;
        _deferredPreview = null;
        SetText(text, forceScroll: true);
    }

    public void Update(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _deferredBuffer = null;
        if (IsSelecting())
        {
            _deferredPreview = text;
            return;
        }

        _deferredPreview = null;
        SetText(text);
    }

    public void ApplyDelta(GitOutputDisplayBuffer display, GitOutputDisplayDelta delta)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (!delta.HasChange) return;
        if (IsSelecting())
        {
            // Display remains the authoritative, incrementally updated output.
            _deferredBuffer = display;
            return;
        }

        if (_deferredBuffer is not null)
        {
            _deferredBuffer = null;
            SetText(display.Text);
            return;
        }

        FindViewport();
        var previousOffset = _viewport?.VerticalOffset;
        _ignoreViewChanges = true;
        _updatingText = true;
        try
        {
            if (delta.ReplaceFrom is { } replaceFrom)
            {
                _textBox.Select(replaceFrom, _textBox.Text.Length - replaceFrom);
                _textBox.SelectedText = delta.Text;
            }
            else
            {
                _textBox.Select(_textBox.Text.Length, 0);
                _textBox.SelectedText = delta.Text;
            }
        }
        finally
        {
            _updatingText = false;
        }

        ScheduleViewportAdjustment(_followTail, previousOffset);
    }

    private void SetText(string text, bool forceScroll = false)
    {
        if (_textBox.Text == text && !forceScroll) return;
        FindViewport();
        var previousOffset = _viewport?.VerticalOffset;
        _ignoreViewChanges = true;
        _updatingText = true;
        try
        {
            if (_textBox.Text != text)
                _textBox.Text = text;
        }
        finally
        {
            _updatingText = false;
        }

        ScheduleViewportAdjustment(_followTail, previousOffset);
    }

    private bool IsSelecting() =>
        GitOutputFollowTailPolicy.ShouldDeferUpdate(
            _textBox.FocusState != FocusState.Unfocused, _textBox.SelectionLength);

    private void FlushDeferred()
    {
        if (_updatingText || IsSelecting()) return;
        if (_deferredBuffer is { } display)
        {
            _deferredBuffer = null;
            SetText(display.Text);
        }
        else if (_deferredPreview is { } preview)
        {
            _deferredPreview = null;
            SetText(preview);
        }
    }

    private void FindViewport()
    {
        if (_viewport is not null) return;
        _viewport = FindScrollViewer(_textBox);
        if (_viewport is not null)
            _viewport.ViewChanged += Viewport_ViewChanged;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is ScrollViewer scrollViewer)
                return scrollViewer;
            if (FindScrollViewer(child) is { } nested)
                return nested;
        }
        return null;
    }

    private void Viewport_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_ignoreViewChanges || _viewport is null) return;
        _followTail = GitOutputFollowTailPolicy.IsNearBottom(
            _viewport.VerticalOffset, _viewport.ScrollableHeight);
    }

    private void ScheduleViewportAdjustment(bool followTail, double? previousOffset)
    {
        _ignoreViewChanges = true;
        var version = ++_scrollVersion;
        if (!_textBox.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (version != _scrollVersion) return;
            try
            {
                FindViewport();
                if (_viewport is not null)
                {
                    var target = followTail ? _viewport.ScrollableHeight : previousOffset;
                    if (target is { } offset)
                        _viewport.ChangeView(null, offset, null, disableAnimation: true);
                }
            }
            finally
            {
                _ignoreViewChanges = false;
            }
        }))
            _ignoreViewChanges = false;
    }
}
