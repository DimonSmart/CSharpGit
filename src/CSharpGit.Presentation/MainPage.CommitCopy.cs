using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _commitDetailsSurfaceInitialized;

    private void InitializeCommitDetailsSurface()
    {
        if (_commitDetailsSurfaceInitialized) return;

        _commitDetailsSurfaceInitialized = true;
        DetailsScroller.HorizontalScrollMode = ScrollMode.Disabled;
        DetailsScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        DetailsScroller.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        DetailsScroller.Content = new CommitDetailsView
        {
            DataContext = _viewModel,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }
}
