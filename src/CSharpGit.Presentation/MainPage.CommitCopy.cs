using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _commitDetailsSurfaceInitialized;

    private void InitializeCommitDetailsSurface()
    {
        if (_commitDetailsSurfaceInitialized) return;
        if (DetailsTabs.Items.Count == 0 || DetailsTabs.Items[0] is not PivotItem commitTab) return;

        _commitDetailsSurfaceInitialized = true;
        commitTab.Content = new CommitDetailsView { DataContext = _viewModel };
    }
}
