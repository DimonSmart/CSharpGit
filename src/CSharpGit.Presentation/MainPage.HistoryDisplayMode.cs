using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    // Phase-3 Commit Details and desktop diagnostics still use these narrow visual adapters.
    // They will be removed when those consumers migrate.
    internal ListView HistoryList => HistoryView.List;
    internal Border HistoryColumnHeader => HistoryView.ColumnHeader;
    private TextBox HistoryFilter => HistoryView.FilterBox;
}
