namespace CSharpGit.Presentation;

internal enum WindowSessionState { Created, Active, PreparingForShutdown, Disposed }

// Owns session transitions, not native window-close admission (WindowCloseCoordinator).
internal sealed class WindowSessionLifecycle
{
    private readonly object _gate = new();
    private int _state = (int)WindowSessionState.Created;
    private Task? _initializeTask;
    private Task? _prepareTask;

    internal WindowSessionState State => (WindowSessionState)Volatile.Read(ref _state);

    internal Task InitializeAsync(
        Func<CancellationToken, Task> initialize,
        Action cleanupOnFailure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        ArgumentNullException.ThrowIfNull(cleanupOnFailure);
        lock (_gate)
        {
            if (_state is (int)WindowSessionState.PreparingForShutdown or (int)WindowSessionState.Disposed)
                throw new InvalidOperationException("A closing window session cannot be initialized.");
            return _initializeTask ??= InitializeCoreAsync(initialize, cleanupOnFailure, cancellationToken);
        }
    }

    private async Task InitializeCoreAsync(
        Func<CancellationToken, Task> initialize,
        Action cleanupOnFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await initialize(cancellationToken);
            lock (_gate)
            {
                if (_state == (int)WindowSessionState.Created)
                    Volatile.Write(ref _state, (int)WindowSessionState.Active);
            }
        }
        catch
        {
            cleanupOnFailure();
            throw;
        }
    }

    internal Task PrepareAsync(
        Func<CancellationToken, Task> prepare,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        lock (_gate)
        {
            if (_state == (int)WindowSessionState.Disposed)
                return Task.CompletedTask;
            if (_prepareTask is not null)
                return _prepareTask;
            Volatile.Write(ref _state, (int)WindowSessionState.PreparingForShutdown);
            _prepareTask = PrepareCoreAsync(prepare, cancellationToken);
            return _prepareTask;
        }
    }

    private static async Task PrepareCoreAsync(
        Func<CancellationToken, Task> prepare,
        CancellationToken cancellationToken) =>
        await prepare(cancellationToken);

    internal bool TryComplete()
    {
        lock (_gate)
        {
            if (_state == (int)WindowSessionState.Disposed) return false;
            Volatile.Write(ref _state, (int)WindowSessionState.Disposed);
            return true;
        }
    }
}
