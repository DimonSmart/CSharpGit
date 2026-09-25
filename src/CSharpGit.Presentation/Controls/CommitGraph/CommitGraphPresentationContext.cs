namespace CSharpGit.Presentation.Controls.CommitGraph;

internal static class CommitGraphPresentationContext
{
    private static CommitGraphLayoutState _current = new();

    internal static CommitGraphLayoutState Current => _current;

    internal static event EventHandler? Changed;

    internal static void Publish(CommitGraphLayoutState layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _current = layout;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
