using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private ProgressRing BusyIndicator => RefreshProgressRing;

    private async void RefreshIndicator_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshInProgress || _viewModel.Repository is null) return;

        SetRefreshInProgress(true);
        try
        {
            await _viewModel.RefreshAsyncForDesktopCheck();
            RefreshPresentationCollections();
        }
        finally
        {
            SetRefreshInProgress(false);
        }
    }
}
