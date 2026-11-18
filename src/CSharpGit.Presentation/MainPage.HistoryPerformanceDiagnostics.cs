namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private void InitializeHistoryPerformanceDiagnostics() =>
        HistoryView.ConfigurePerformance(
            _viewModel,
            _recentRepositorySettings,
            _gitCommandActivitySource,
            _authorAvatarService,
            _desktopShellService,
            ShowErrorAsync,
            async dialog => { await ShowPageDialogAsync(dialog); },
            ReportUiFailure);

    private Task StopHistoryPerformanceCaptureOnShutdown() =>
        HistoryView.StopHistoryPerformanceCaptureOnShutdown();
}
