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
            if (!IsShuttingDown)
                RefreshPresentationCollections();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!IsShuttingDown && !_viewModel.HasError)
                await ShowErrorAsync("Refresh failed", exception.Message);
        }
        finally
        {
            SetRefreshInProgress(false);
        }
    }
}
