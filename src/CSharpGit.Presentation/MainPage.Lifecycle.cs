namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private int _shutdownStarted;

    public bool RequiresCloseConfirmation => _viewModel.HasUnappliedCommitMessage;

    public void BeginShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;

        Loaded -= RunDesktopCheckWhenRequested;
        DetachRepositoryTreeStateTracking();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        CloseSettingsWindow();

        _referenceHistoryCts?.Cancel();
        _referenceHistoryCts?.Dispose();
        _referenceHistoryCts = null;
    }
}
