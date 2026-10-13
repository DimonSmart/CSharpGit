using CSharpGit.Presentation;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryFilesTreeSelectionRouterTests
{
    private sealed class Node(string name)
    {
        public string Name { get; } = name;
        public List<Node> Children { get; } = [];
    }

    private sealed record Wrapper(Node Content);

    private static Node? Resolve(object? candidate) => candidate switch
    {
        Node node => node,
        Wrapper { Content: var node } => node,
        _ => null
    };

    private static RepositoryFilesTreeSelectionRouter<Node> Router(params Node[] roots)
    {
        var router = new RepositoryFilesTreeSelectionRouter<Node>();
        router.ReplaceTree(roots, n => n.Children);
        return router;
    }

    private static RepositoryFilesTreeSelectionDecision<Node> Event(
        RepositoryFilesTreeSelectionRouter<Node> router,
        object?[] added,
        object?[] removed,
        Node? selection = null,
        bool rebuilding = false) =>
        router.Route(added, removed, Resolve, selection, rebuilding);

    [Fact]
    public void FirstClickAndSuccessiveClicksUseAddedItemNotLaggingSelectedItem()
    {
        var a = new Node("A");
        var b = new Node("B");
        var c = new Node("C");
        var router = Router(a, b, c);
        Node? selected = null;
        // The UI's SelectedItem can still be null, A, B; it is intentionally
        // not an input to the router.
        foreach (var next in new[] { a, b, c })
        {
            var decision = Event(router, [next], selected is null ? [] : [selected], selected);
            Assert.Equal(RepositoryFilesTreeSelectionAction.Select, decision.Action);
            Assert.Same(next, decision.Node);
            selected = decision.Node;
        }
    }

    [Fact]
    public void MixedDeltaPrefersAdditionAndEmptyOrUnknownDeltaIsIgnored()
    {
        var a = new Node("A");
        var b = new Node("B");
        var router = Router(a, b);

        var decision = Event(router, [new Wrapper(b)], [a], a);
        Assert.Equal(RepositoryFilesTreeSelectionAction.Select, decision.Action);
        Assert.Same(b, decision.Node);
        Assert.Equal(RepositoryFilesTreeSelectionAction.Ignore, Event(router, [], [], b).Action);
        Assert.Equal(RepositoryFilesTreeSelectionAction.Ignore, Event(router, [new object()], [b], b).Action);
        Assert.Equal(RepositoryFilesTreeSelectionAction.Ignore, Event(router, [a, b], [], b).Action);
    }

    [Fact]
    public void EchoesOfProgrammaticSelectionDoNotRepeatLogicalChanges()
    {
        var a = new Node("A");
        var router = Router(a);
        router.ExpectProgrammaticSelection(a);
        Assert.Equal(RepositoryFilesTreeSelectionAction.Ignore, Event(router, [a], [], a).Action);
        // Delayed echo (after the setter has returned) has identical semantics.
        Assert.Equal(RepositoryFilesTreeSelectionAction.Ignore, Event(router, [new Wrapper(a)], [], a).Action);
        Assert.Equal(RepositoryFilesTreeSelectionAction.Ignore, Event(router, [], [a], null).Action);
    }

    [Fact]
    public void ContentSearchWithoutVisualNodeIsNotClearedByProgrammaticNull()
    {
        var a = new Node("A");
        var router = Router(a);
        router.ExpectProgrammaticSelection(null);
        Assert.Equal(RepositoryFilesTreeSelectionAction.Ignore, Event(router, [], [a], null).Action);
        // The actual user is never blocked by a pending programmatic null.
        Assert.Equal(RepositoryFilesTreeSelectionAction.Select, Event(router, [a], [], null).Action);
    }

    [Fact]
    public void RealRemovalRequestsDeferredClearAndLaterAdditionWins()
    {
        var a = new Node("A");
        var b = new Node("B");
        var router = Router(a, b);
        var removal = Event(router, [], [a], a);
        Assert.Equal(RepositoryFilesTreeSelectionAction.ClearIfStillUnselected, removal.Action);
        Assert.Equal(RepositoryFilesTreeSelectionAction.Select, Event(router, [b], [], a).Action);
        Assert.Equal(RepositoryFilesTreeSelectionAction.Ignore, Event(router, [], [a], b).Action);
    }

    [Fact]
    public void RebuildDiscardsOldNodesEvenForSamePathAndKeepsCurrentDescendants()
    {
        var old = new Node("same/path");
        var router = Router(old);
        var fresh = new Node("same/path");
        var folder = new Node("folder");
        folder.Children.Add(fresh);
        router.ReplaceTree([folder], n => n.Children);
        Assert.Equal(RepositoryFilesTreeSelectionAction.Ignore, Event(router, [old], [], null).Action);
        Assert.Equal(RepositoryFilesTreeSelectionAction.Select, Event(router, [fresh], [], null).Action);
        Assert.Equal(RepositoryFilesTreeSelectionAction.Ignore, Event(router, [fresh], [], null, rebuilding: true).Action);
    }

    [Fact]
    public void AProgrammaticExpectationCannotBlockDifferentUserSelection()
    {
        var a = new Node("A");
        var b = new Node("B");
        var router = Router(a, b);
        router.ExpectProgrammaticSelection(a);
        var decision = Event(router, [b], [a], a);
        Assert.Equal(RepositoryFilesTreeSelectionAction.Select, decision.Action);
        Assert.Same(b, decision.Node);
    }

    [Fact]
    public void FoldersAndUnsupportedEntriesStillRouteAsNormalTreeNodes()
    {
        var directory = new Node("directory");
        var symlink = new Node("symlink");
        var submodule = new Node("submodule");
        var router = Router(directory, symlink, submodule);
        foreach (var item in new[] { directory, symlink, submodule })
            Assert.Equal(RepositoryFilesTreeSelectionAction.Select, Event(router, [item], [], null).Action);
    }
}
