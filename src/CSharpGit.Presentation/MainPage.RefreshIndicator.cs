using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private ProgressRing BusyIndicator => RefreshProgressRing;

    private void RefreshIndicator_Click(object sender, RoutedEventArgs e) =>
        RunPageUiOperation(RefreshIndicatorAsync, "Repository refresh indicator");

    private async Task RefreshIndicatorAsync()
    {
        var repository = _viewModel.Repository;
        if (IsShuttingDown || _isRefreshInProgress || repository is null) return;

        SetRefreshInProgress(true);
        try
        {
            await _viewModel.RefreshAsyncForDesktopCheck();
            if (!IsShuttingDown && ReferenceEquals(repository, _viewModel.Repository))
                RefreshPresentationCollections();
        }
        catch (OperationCanceledException) when (
            IsShuttingDown || !ReferenceEquals(repository, _viewModel.Repository))
        {
        }
        catch (Exception exception)
        {
            ReportUiFailure(exception, "Repository refresh");
            if (!IsShuttingDown && ReferenceEquals(repository, _viewModel.Repository)
                && !_viewModel.HasError)
                await ShowErrorAsync("Refresh failed", exception.Message);
        }
        finally
        {
            SetRefreshInProgress(false);
        }
    }
}
