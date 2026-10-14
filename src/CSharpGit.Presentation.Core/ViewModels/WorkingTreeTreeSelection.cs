namespace CSharpGit.Presentation.ViewModels;

public sealed class WorkingTreeTreeSelection
{
    private readonly HashSet<string> _selectedPaths = new(StringComparer.Ordinal);
    private string? _anchorPath;

    public IReadOnlyCollection<string> SelectedPaths => _selectedPaths;

    public IReadOnlyList<WorkingTreeTreeNode> Apply(
        WorkingTreeTreeNode target,
        IReadOnlyList<WorkingTreeTreeNode> roots,
        bool controlPressed,
        bool shiftPressed)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(roots);
        if (target.Change is null) return GetSelectedLeaves(roots);

        if (shiftPressed && TryApplyRange(target.Path, roots, controlPressed))
        {
            ApplyVisualState(roots);
            return GetSelectedLeaves(roots);
        }

        if (controlPressed)
        {
            if (!_selectedPaths.Add(target.Path)) _selectedPaths.Remove(target.Path);
        }
        else
        {
            _selectedPaths.Clear();
            _selectedPaths.Add(target.Path);
        }

        _anchorPath = target.Path;
        ApplyVisualState(roots);
        return GetSelectedLeaves(roots);
    }

    public void SetSelectedPaths(
        IEnumerable<string> paths,
        IReadOnlyList<WorkingTreeTreeNode> roots)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(roots);

        var availablePaths = EnumerateLeaves(roots)
            .Select(node => node.Path)
            .ToHashSet(StringComparer.Ordinal);
        _selectedPaths.Clear();
        foreach (var path in paths)
        {
            if (availablePaths.Contains(path)) _selectedPaths.Add(path);
        }

        if (_anchorPath is not null && !availablePaths.Contains(_anchorPath)) _anchorPath = null;
        ApplyVisualState(roots);
    }

    public void SelectSingle(WorkingTreeTreeNode target, IReadOnlyList<WorkingTreeTreeNode> roots)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Change is null) return;
        _selectedPaths.Clear();
        _selectedPaths.Add(target.Path);
        _anchorPath = target.Path;
        ApplyVisualState(roots);
    }

    public void Clear(IReadOnlyList<WorkingTreeTreeNode> roots)
    {
        _selectedPaths.Clear();
        _anchorPath = null;
        ApplyVisualState(roots);
    }

    public bool IsSelected(string path) => _selectedPaths.Contains(path);

    public IReadOnlyList<WorkingTreeTreeNode> GetSelectedLeaves(IReadOnlyList<WorkingTreeTreeNode> roots) =>
        EnumerateLeaves(roots).Where(node => _selectedPaths.Contains(node.Path)).ToList();

    public static IReadOnlyList<WorkingTreeTreeNode> GetLeaves(IReadOnlyList<WorkingTreeTreeNode> roots) =>
        EnumerateLeaves(roots).ToList();

    private bool TryApplyRange(
        string targetPath,
        IReadOnlyList<WorkingTreeTreeNode> roots,
        bool preserveExistingSelection)
    {
        if (_anchorPath is null) return false;

        var visibleLeaves = EnumerateVisibleLeaves(roots).ToList();
        var anchorIndex = visibleLeaves.FindIndex(node => string.Equals(node.Path, _anchorPath, StringComparison.Ordinal));
        var targetIndex = visibleLeaves.FindIndex(node => string.Equals(node.Path, targetPath, StringComparison.Ordinal));
        if (anchorIndex < 0 || targetIndex < 0) return false;

        if (!preserveExistingSelection) _selectedPaths.Clear();
        var start = Math.Min(anchorIndex, targetIndex);
        var end = Math.Max(anchorIndex, targetIndex);
        for (var index = start; index <= end; index++) _selectedPaths.Add(visibleLeaves[index].Path);
        return true;
    }

    private void ApplyVisualState(IEnumerable<WorkingTreeTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsBatchSelected = node.Change is not null && _selectedPaths.Contains(node.Path);
            ApplyVisualState(node.Children);
        }
    }

    private static IEnumerable<WorkingTreeTreeNode> EnumerateLeaves(IEnumerable<WorkingTreeTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Change is not null)
            {
                yield return node;
                continue;
            }

            foreach (var child in EnumerateLeaves(node.Children)) yield return child;
        }
    }

    private static IEnumerable<WorkingTreeTreeNode> EnumerateVisibleLeaves(IEnumerable<WorkingTreeTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Change is not null)
            {
                yield return node;
                continue;
            }

            if (!node.IsExpanded) continue;
            foreach (var child in EnumerateVisibleLeaves(node.Children)) yield return child;
        }
    }
}
