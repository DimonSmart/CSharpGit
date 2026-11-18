namespace CSharpGit.Presentation;

// Scope-local admission state for application updates.
public sealed class ApplicationUpdateActivityState
{
    private int _running;
    internal bool IsRunning => Volatile.Read(ref _running) != 0;
    internal bool TryBegin() => Interlocked.CompareExchange(ref _running, 1, 0) == 0;
    internal void End() => Interlocked.Exchange(ref _running, 0);
}
