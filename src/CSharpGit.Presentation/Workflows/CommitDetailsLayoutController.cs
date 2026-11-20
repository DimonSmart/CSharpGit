using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

// Owns Commit Details viewport width, size subscriptions, and XamlRoot resize handling.
internal sealed class CommitDetailsLayoutController(
    ScrollViewer detailsScroller,
    FrameworkElement historyPane,
    CommitDetailsView commitDetailsView,
    Func<XamlRoot?> xamlRoot,
    Func<double> mainWindowClientWidth)
{
    private readonly ScrollViewer _detailsScroller = detailsScroller;
    private readonly FrameworkElement _historyPane = historyPane;
    private readonly CommitDetailsView _commitDetailsView = commitDetailsView;
    private readonly Func<double> _mainWindowClientWidth = mainWindowClientWidth;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private XamlRoot? _root;
    private bool _initialized;

    internal void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _detailsScroller.HorizontalScrollMode = ScrollMode.Disabled;
        _detailsScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _detailsScroller.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _detailsScroller.SizeChanged += DetailsScroller_SizeChanged;
        _historyPane.SizeChanged += DetailsScroller_SizeChanged;
        EnsureRootSubscription();
        QueueLayout();
    }

    internal void QueueLayout() =>
        _detailsScroller.DispatcherQueue.TryEnqueue(ConstrainCommitDetailsToViewport);

    private void EnsureRootSubscription()
    {
        var currentRoot = _xamlRoot() ?? _detailsScroller.XamlRoot;
        if (ReferenceEquals(_root, currentRoot)) return;
        if (_root is not null)
            _root.Changed -= XamlRoot_Changed;
        _root = currentRoot;
        if (_initialized && _root is not null)
            _root.Changed += XamlRoot_Changed;
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => QueueLayout();

    private void DetailsScroller_SizeChanged(object sender, SizeChangedEventArgs args) =>
        ConstrainCommitDetailsToViewport();

    private void ConstrainCommitDetailsToViewport()
    {
        if (!_initialized) return;
        EnsureRootSubscription();
        if (_root is null) return;

        var origin = _historyPane.TransformToVisual(null).TransformPoint(default);
        var rasterizationScale = _root.RasterizationScale;
        if (!double.IsFinite(rasterizationScale) || rasterizationScale <= 0) return;

        var windowWidth = _mainWindowClientWidth() / rasterizationScale;
        var visibleWidth = windowWidth - origin.X;
        if (!double.IsFinite(visibleWidth) || visibleWidth <= 0) return;

        visibleWidth = Math.Floor(visibleWidth);
        if (!double.IsFinite(_detailsScroller.Width) || Math.Abs(_detailsScroller.Width - visibleWidth) > 0.5)
            _detailsScroller.Width = visibleWidth;
        if (!double.IsFinite(_commitDetailsView.Width) || Math.Abs(_commitDetailsView.Width - visibleWidth) > 0.5)
            _commitDetailsView.Width = visibleWidth;
    }

    internal void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;
        _detailsScroller.SizeChanged -= DetailsScroller_SizeChanged;
        _historyPane.SizeChanged -= DetailsScroller_SizeChanged;
        if (_root is not null)
            _root.Changed -= XamlRoot_Changed;
        _root = null;
    }
}
