namespace CSharpGit.Presentation;

internal enum RepositoryFilesTreeSelectionAction
{
    Ignore,
    Select,
    ClearIfStillUnselected
}

internal readonly record struct RepositoryFilesTreeSelectionDecision<TNode>(
    RepositoryFilesTreeSelectionAction Action,
    TNode? Node = null) where TNode : class;

// UI-independent routing for TreeViewSelectionChangedEventArgs. The collections
// describe the transition; TreeView.SelectedItem may still describe the old item.
internal sealed class RepositoryFilesTreeSelectionRouter<TNode> where TNode : class
{
    private readonly HashSet<TNode> _currentNodes = new(ReferenceEqualityComparer.Instance);
    private TNode? _expectedProgrammaticSelection;
    private bool _expectedProgrammaticClear;

    internal void ReplaceTree(
        IEnumerable<TNode> roots,
        Func<TNode, IEnumerable<TNode>> children)
    {
        _currentNodes.Clear();
        _expectedProgrammaticSelection = null;
        _expectedProgrammaticClear = false;
        foreach (var root in roots)
            AddDescendants(root, children);
    }

    internal void ExpectProgrammaticSelection(TNode? target)
    {
        _expectedProgrammaticSelection = target;
        _expectedProgrammaticClear = target is null;
    }

    internal RepositoryFilesTreeSelectionDecision<TNode> Route(
        IReadOnlyList<object?> added,
        IReadOnlyList<object?> removed,
        Func<object?, TNode?> resolve,
        TNode? logicalSelection,
        bool rebuilding)
    {
        if (rebuilding)
            return default;

        if (added.Count != 0)
        {
            // Single-selection mode must not arbitrarily pick among several additions.
            if (added.Count != 1 || resolve(added[0]) is not { } next
                || !_currentNodes.Contains(next))
                return default;

            // This is also true for a delayed notification following a programmatic
            // selection. Do not call SelectTreeNode again: that clears search line context.
            if (ReferenceEquals(next, logicalSelection))
            {
                if (ReferenceEquals(next, _expectedProgrammaticSelection))
                    _expectedProgrammaticSelection = null;
                return default;
            }

            // A genuinely different addition always wins over a pending programmatic
            // expectation. Never discard a user click merely due to an old marker.
            _expectedProgrammaticSelection = null;
            _expectedProgrammaticClear = false;
            return new(RepositoryFilesTreeSelectionAction.Select, next);
        }

        if (removed.Count == 0 || logicalSelection is null)
        {
            if (logicalSelection is null)
                _expectedProgrammaticClear = false;
            return default;
        }

        foreach (var item in removed)
        {
            if (ReferenceEquals(resolve(item), logicalSelection))
            {
                // A remove-only event can precede a separate add event in Uno.
                // Let the host verify that the tree remains unselected on the
                // next dispatcher pass before actually clearing the ViewModel.
                return new(RepositoryFilesTreeSelectionAction.ClearIfStillUnselected);
            }
        }

        return default;
    }

    private void AddDescendants(TNode node, Func<TNode, IEnumerable<TNode>> children)
    {
        if (!_currentNodes.Add(node))
            return;
        foreach (var child in children(node))
            AddDescendants(child, children);
    }
}
