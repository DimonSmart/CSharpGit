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

        _commitDetailsView = new CommitDetailsView
        {
            DataContext = _viewModel,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        DetailsScroller.Content = _commitDetailsView;
        DetailsScroller.SizeChanged += DetailsScroller_SizeChanged;
        ConstrainCommitDetailsToViewport(DetailsScroller.ActualWidth);
    }

    private void DetailsScroller_SizeChanged(object sender, SizeChangedEventArgs args) =>
        ConstrainCommitDetailsToViewport(args.NewSize.Width);

    private void ConstrainCommitDetailsToViewport(double availableWidth)
    {
        if (_commitDetailsView is null || availableWidth <= 0) return;
        _commitDetailsView.Width = availableWidth;
    }
}
