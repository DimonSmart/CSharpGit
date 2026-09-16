using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _commitDetailsSurfaceInitialized;
    private CommitDetailsView? _commitDetailsView;

    private void InitializeCommitDetailsSurface()
    {
        if (_commitDetailsSurfaceInitialized) return;

        _commitDetailsSurfaceInitialized = true;
        DetailsScroller.HorizontalScrollMode = ScrollMode.Disabled;
        DetailsScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        DetailsScroller.HorizontalContentAlignment = HorizontalAlignment.Stretch;

        _commitDetailsView = CommitDetailsContent;
        DetailsScroller.SizeChanged += DetailsScroller_SizeChanged;
        HistoryPane.SizeChanged += DetailsScroller_SizeChanged;
        if (XamlRoot is not null)
            XamlRoot.Changed += (_, _) => QueueCommitDetailsLayout();
        DetailsScroller.DispatcherQueue.TryEnqueue(ConstrainCommitDetailsToViewport);
    }

    internal void QueueCommitDetailsLayout() =>
        DispatcherQueue.TryEnqueue(ConstrainCommitDetailsToViewport);

    private void DetailsScroller_SizeChanged(object sender, SizeChangedEventArgs args) =>
        ConstrainCommitDetailsToViewport();

    private void ConstrainCommitDetailsToViewport()
    {
        if (_commitDetailsView is null || XamlRoot is null || Application.Current is not App app) return;

        var origin = HistoryPane.TransformToVisual(null).TransformPoint(default);
        var rasterizationScale = XamlRoot.RasterizationScale;
        if (!double.IsFinite(rasterizationScale) || rasterizationScale <= 0) return;

        var windowWidth = app.MainWindowClientWidth / rasterizationScale;
        var visibleWidth = windowWidth - origin.X;
        if (!double.IsFinite(visibleWidth) || visibleWidth <= 0) return;

        visibleWidth = Math.Floor(visibleWidth);
        if (!double.IsFinite(DetailsScroller.Width) || Math.Abs(DetailsScroller.Width - visibleWidth) > 0.5)
            DetailsScroller.Width = visibleWidth;
        if (!double.IsFinite(_commitDetailsView.Width) || Math.Abs(_commitDetailsView.Width - visibleWidth) > 0.5)
            _commitDetailsView.Width = visibleWidth;
    }
}
