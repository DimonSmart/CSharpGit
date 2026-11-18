namespace CSharpGit.Presentation.Windowing;

// Scope-local admission state for the application update workflow.
// A close decision must not inspect private MainPage fields.
public sealed class ApplicationUpdateActivityState
{
    private int _running;

    internal bool IsRunning => Volatile.Read(ref _running) != 0;

    internal bool TryBegin() => Interlocked.CompareExchange(ref _running, 1, 0) == 0;

    internal void End() => Interlocked.Exchange(ref _running, 0);
}
