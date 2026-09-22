using Microsoft.UI.Dispatching;

namespace CSharpGit.Presentation.Threading;

public sealed class UiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _dispatcherQueue;

    public UiDispatcher(DispatcherQueue dispatcherQueue) =>
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));

    public bool HasThreadAccess => _dispatcherQueue.HasThreadAccess;

    public bool TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _dispatcherQueue.TryEnqueue(() => action());
    }
}
