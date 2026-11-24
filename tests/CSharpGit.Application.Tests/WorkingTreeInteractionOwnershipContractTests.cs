namespace CSharpGit.Application.Tests;

public sealed class WorkingTreeInteractionOwnershipContractTests
{
    [Fact]
    public void FeatureViewOwnsWorkingTreeSelectionAndTearsDownItsOwnSubscriptions()
    {
        var root = FindRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var view = File.ReadAllText(Path.Combine(presentation, "Views", "WorkingTreeView.Selection.cs"));
        var code = File.ReadAllText(Path.Combine(presentation, "Composition/WindowWorkspaceComposition.cs"));
        var lifetime = File.ReadAllText(Path.Combine(presentation, "Composition/WindowWorkspaceComposition.Lifetime.cs"));
        var context = File.ReadAllText(Path.Combine(presentation, "Views", "WorkingTreeView.ContextMenu.cs"));

        Assert.False(File.Exists(Path.Combine(presentation, "MainPage.WorkingTreeDiff.cs")));
        Assert.Contains("internal void SelectWorkingTreeChange(", view);
        Assert.Contains("internal void SetSelectionSync(bool value)", view);
        Assert.Contains("_unstagedChanges.CollectionChanged += WorkingTreePresentationSourceChanged", view);
        Assert.Contains("_unstagedChanges.CollectionChanged -= WorkingTreePresentationSourceChanged", view);
        Assert.Contains("_viewModel.WorkingTree.PropertyChanged -= WorkingTreeViewModel_PropertyChanged", view);
        Assert.Contains("UnregisterPropertyChangedCallback(VisibilityProperty, _visibilityCallbackToken)", view);
        Assert.Contains("WorkingTreePane.Configure(", code);
        Assert.True(code.IndexOf("        WorkingTreePane.Configure(", StringComparison.Ordinal)
            < code.IndexOf("        RefreshPresentationCollections();", StringComparison.Ordinal),
            "WorkingTreeView must receive dependencies before the initial collection refresh.");
        Assert.Contains("WorkingTreePane.ShutdownWorkingTreeDiffSurface();", lifetime);
        Assert.Contains("SelectWorkingTreeContextTarget(", context);
        Assert.False(File.Exists(Path.Combine(presentation, "MainPage.WorkingTreeContextMenu.cs")));
        Assert.DoesNotContain("_unstagedTreeRoots", context);
        Assert.DoesNotContain("_unstagedTreeRoots", code);
        Assert.DoesNotContain("_unstagedChanges", code);
        Assert.DoesNotContain("_stagedChanges", code);
        Assert.Contains("internal void RefreshChangesProjection()", view);
        Assert.Contains("WorkingTreePane.RefreshChangesProjection();", code);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit root not found");
    }
}
