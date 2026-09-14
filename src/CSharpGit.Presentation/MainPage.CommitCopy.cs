using CSharpGit.Presentation.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _commitDetailsSurfaceInitialized;

    private void InitializeCommitDetailsSurface()
    {
        if (_commitDetailsSurfaceInitialized) return;

        _commitDetailsSurfaceInitialized = true;
        DetailsScroller.Content = new CommitDetailsView { DataContext = _viewModel };
    }
}
