using CSharpGit.Presentation.Controls.CommitGraph;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    // Transitional read-only diagnostic adapter until phase-3 runner migration.
    private CommitGraphLayoutState ActiveCommitGraphLayout => HistoryView.Layout;
}
