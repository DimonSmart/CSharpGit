using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _repositoryPresentationRefreshQueued;
    private bool _repositoryTreePresentationDirty;
    private bool _workingTreePresentationDirty;

    public bool IsRefreshRequired => _repositoryRefresh.IsRefreshRequired;

    // Diagnostic fence remains publicly observable through the window's visual root.
    internal Action CapturePendingMonitorDeliveryForDesktopCheck() =>
        _repositoryRefresh.CapturePendingMonitorDeliveryForDesktopCheck();

    private void QueueRepositoryPresentationRefresh(bool workingTreeChanged = false)
    {
        if (IsShuttingDown) return;

        if (workingTreeChanged) _workingTreePresentationDirty = true;
        else _repositoryTreePresentationDirty = true;
        if (_repositoryPresentationRefreshQueued) return;

        _repositoryPresentationRefreshQueued = true;
        if (DispatcherQueue.TryEnqueue(FlushRepositoryPresentationRefresh)) return;

        if (!IsShuttingDown && DispatcherQueue.HasThreadAccess)
        {
            FlushRepositoryPresentationRefresh();
            return;
        }

        ClearRepositoryPresentationRefreshQueue();
    }

    private void FlushRepositoryPresentationRefresh()
    {
        if (IsShuttingDown)
        {
            ClearRepositoryPresentationRefreshQueue();
            return;
        }

        _repositoryPresentationRefreshQueued = false;
        var refreshWorkingTree = _workingTreePresentationDirty;
        var refreshRepositoryTree = _repositoryTreePresentationDirty;
        _workingTreePresentationDirty = false;
        _repositoryTreePresentationDirty = false;
        InitializeTagSupportIfNeeded();

        if (refreshWorkingTree) RefreshPresentationCollections();
        if (refreshRepositoryTree) SynchronizeRepositoryTree();
        UpdateStatusBar();
    }

    private void ClearRepositoryPresentationRefreshQueue()
    {
        _repositoryPresentationRefreshQueued = false;
        _workingTreePresentationDirty = false;
        _repositoryTreePresentationDirty = false;
    }
}
