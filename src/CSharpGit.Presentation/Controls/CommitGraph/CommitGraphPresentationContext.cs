using CSharpGit.Presentation.Controls;

namespace CSharpGit.Presentation.Controls.CommitGraph;

internal static class CommitGraphPresentationContext
{
    private static CommitGraphLayoutState _current = new();

    internal static CommitGraphLayoutState Current => _current;

    internal static event EventHandler? Changed;

    internal static void Publish(CommitGraphLayoutState layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var startedAt = HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
        _current = layout;
        Changed?.Invoke(null, EventArgs.Empty);
        HistoryRenderDiagnostics.PresentationPublished(startedAt);
    }
}
