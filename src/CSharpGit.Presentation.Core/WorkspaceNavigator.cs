using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation;

/// <summary>
/// Session-owned navigation coordinator. Resolves history targets and rejects stale
/// repository/interaction versions; visual selection and scrolling belong to HistoryView.
/// </summary>
public sealed class WorkspaceNavigator
{
    private readonly IHistoryRepositoryContext _context;
    private readonly HistoryViewModel _history;
    private readonly Action _clearStashSelection;

    public WorkspaceNavigator(
        IHistoryRepositoryContext context,
        HistoryViewModel history,
        Action clearStashSelection)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _clearStashSelection = clearStashSelection ?? throw new ArgumentNullException(nameof(clearStashSelection));
    }

    internal RepositoryTreeNavigationCoordinator Interactions { get; } = new();
    internal WorkspaceSurface Surface { get; private set; } = WorkspaceSurface.History;
    internal event Action<WorkspaceSurface>? SurfaceChanged;

    public void ResetForRepositoryChange()
    {
        Interactions.ResetForRepositoryChange();
        ShowSurface(WorkspaceSurface.History);
    }

    public void ShowWorkingTree()
    {
        Interactions.BeginInteraction();
        ShowSurface(WorkspaceSurface.WorkingTree);
    }

    public void ShowHistory() => ShowSurface(WorkspaceSurface.History);

    internal async Task<WorkspaceCommitTarget?> NavigateToCommitAsync(
        string commitHash, bool preserveSelectedStash = false,
        long? interactionVersion = null)
    {
        var repository = _context.Repository;
        if (repository is null
            || (interactionVersion is { } version && version != Interactions.Version))
            return null;

        if (!preserveSelectedStash)
            _clearStashSelection();
        ShowHistory();

        var previouslyLoaded = _history.Rows.FirstOrDefault(
            row => string.Equals(row.Commit.Hash, commitHash, StringComparison.Ordinal));
        var target = await _history.NavigateToCommitAsync(commitHash);
        if (!ReferenceEquals(_context.Repository, repository)
            || (interactionVersion is { } currentVersion
                && currentVersion != Interactions.Version))
            return null;

        if (target is null)
        {
            if (interactionVersion is { } deferredVersion
                && !_context.CanStartUserGitOperation)
                Interactions.DeferReference(
                    repository, deferredVersion, _history.SelectionIntentVersion,
                    commitHash, preserveSelectedStash);
            return null;
        }

        return new WorkspaceCommitTarget(
            repository, target, ReferenceEquals(previouslyLoaded, target),
            _history.SelectionIntentVersion, interactionVersion);
    }

    public async Task ShowReferenceAsync(string reference, string label)
    {
        var repository = _context.Repository;
        if (repository is null) return;
        ShowHistory();
        await _history.ShowReferenceAsync(reference, label);
    }

    public async Task<HistoryRow?> ShowAllHistoryAsync(bool clearStash = true)
    {
        var repository = _context.Repository;
        if (repository is null) return null;
        if (clearStash) _clearStashSelection();
        ShowHistory();
        await _history.ShowAllAsync();
        return ReferenceEquals(repository, _context.Repository)
            ? _history.SelectedRow : null;
    }

    internal bool IsTargetCurrent(WorkspaceCommitTarget target) =>
        ReferenceEquals(target.Repository, _context.Repository)
        && Surface == WorkspaceSurface.History
        && _history.SelectionIntentVersion == target.SelectionIntentVersion
        && _history.Rows.Contains(target.Row)
        && ReferenceEquals(_history.SelectedRow, target.Row)
        && (target.InteractionVersion is null
            || Interactions.Version == target.InteractionVersion.Value);

    private void ShowSurface(WorkspaceSurface surface)
    {
        if (Surface == surface) return;
        Surface = surface;
        SurfaceChanged?.Invoke(surface);
    }
}

internal enum WorkspaceSurface
{
    History,
    WorkingTree
}

internal sealed record WorkspaceCommitTarget(
    Repository Repository,
    HistoryRow Row,
    bool WasPreviouslyLoaded,
    long SelectionIntentVersion,
    long? InteractionVersion);
