using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    // Transitional consumers belong to phase-3 Commit Details and diagnostic dialogs.
    // All history state, filters and scroll subscriptions are now owned by HistoryView.
    internal ListView HistoryList => HistoryView.List;
    internal Border HistoryColumnHeader => HistoryView.ColumnHeader;
    private TextBox HistoryFilter => HistoryView.FilterBox;
    private ColumnDefinition HistoryGraphHeaderColumn => HistoryView.GraphHeaderColumn;
    private SelectorBar ScopeCombo => HistoryView.ScopeSelector;
    private StackPanel ReferenceScopePanel => HistoryView.ReferencePanel;
    private TextBlock ActiveReferenceText => HistoryView.ReferenceLabel;
    private Button LoadMoreHistoryButton => HistoryView.LoadMoreButton;
    private StackPanel HistoryPerformanceControls => HistoryView.PerformanceControls;
    private TextBlock HistoryPerformanceCaptureStatus => HistoryView.PerformanceCaptureStatus;
    private MenuFlyoutItem StartHistoryPerformanceCaptureMenuItem => HistoryView.StartCaptureAction;
    private MenuFlyoutItem StopHistoryPerformanceCaptureMenuItem => HistoryView.StopCaptureAction;
    private MenuFlyoutItem OpenHistoryPerformanceDiagnosticsFolderMenuItem => HistoryView.OpenDiagnosticsAction;
}
