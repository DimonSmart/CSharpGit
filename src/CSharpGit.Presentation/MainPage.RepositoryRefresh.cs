namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _repositoryPresentationRefreshQueued;
    private bool _workingTreePresentationDirty;

    private void QueueRepositoryPresentationRefresh(bool workingTreeChanged = false)
    {
        _workingTreePresentationDirty |= workingTreeChanged;
        if (_repositoryPresentationRefreshQueued) return;

        _repositoryPresentationRefreshQueued = true;
        if (DispatcherQueue.TryEnqueue(FlushRepositoryPresentationRefresh)) return;

        FlushRepositoryPresentationRefresh();
    }

    private void FlushRepositoryPresentationRefresh()
    {
        _repositoryPresentationRefreshQueued = false;
        var refreshWorkingTree = _workingTreePresentationDirty;
        _workingTreePresentationDirty = false;

        if (refreshWorkingTree)
        {
            RefreshPresentationCollections();
            return;
        }

        RebuildRepositoryTree();
        UpdateStatusBar();
    }
}
