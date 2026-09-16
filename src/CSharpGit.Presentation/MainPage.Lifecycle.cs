using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private int _shutdownStarted;

    public bool RequiresCloseConfirmation => _viewModel.HasUnappliedCommitMessage;

    private void MainPage_Loaded(object sender, RoutedEventArgs args)
    {
        InitializeCommitDetailsSurface();
        InitializeConfirmationDialogs();
    }

    public void BeginShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;

        Loaded -= RunDesktopCheckWhenRequested;
        DetachRepositoryTreeStateTracking();
        ShutdownRepositoryChangeMonitoring();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        CloseSettingsWindow();

        _referenceHistoryCts?.Cancel();
        _referenceHistoryCts?.Dispose();
        _referenceHistoryCts = null;
    }
}
