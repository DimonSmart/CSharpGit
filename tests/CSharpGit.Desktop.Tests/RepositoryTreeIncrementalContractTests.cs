namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryTreeIncrementalContractTests
{
    [Fact]
    public void RepositoryRefreshIsCoalescedAndNonStructuralPropertiesDoNotRebuildTree()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var refresh = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryRefresh.cs"));
        var worktrees = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Worktrees.cs"));
        var tags = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Tags.cs"));

        Assert.DoesNotContain("_repositoryTreeRoots.Clear();", ExtractMethod(page, "private void RebuildRepositoryTree()"), StringComparison.Ordinal);
        Assert.DoesNotContain("RebuildRepositoryTree();\n            UpdateStatusBar();", page, StringComparison.Ordinal);
        Assert.Contains("_repositoryTreePresentationDirty", refresh, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(ExtractMethod(refresh, "private void FlushRepositoryPresentationRefresh()"), "SynchronizeRepositoryTree();"));
        Assert.DoesNotContain("ApplyTagOrderingToRepositoryTree();", ExtractMethod(refresh, "private void FlushRepositoryPresentationRefresh()"), StringComparison.Ordinal);
        Assert.DoesNotContain("root.Children.Clear();", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveWorktreeRoot();", worktrees, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallWorktreeRoot();", worktrees, StringComparison.Ordinal);
        Assert.Contains("SynchronizeWorktreePresentation();", worktrees, StringComparison.Ordinal);
    }

    [Fact]
    public void StableKeysAndSeparateWorktreeReconciliationAreExplicit()
    {
        var root = FindRepositoryRoot();
        var descriptors = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeDescriptor.cs"));
        var synchronizer = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeSynchronizer.cs"));
        var node = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryTreeNode.cs"));

        Assert.Contains("$\"stash:{stash.Commit}\"", descriptors, StringComparison.Ordinal);
        Assert.Contains("$\"worktree:{worktree.Path}\"", descriptors, StringComparison.Ordinal);
        Assert.Contains("$\"branch-folder:remote:{remoteName}:{prefix}\"", descriptors, StringComparison.Ordinal);
        Assert.Contains("Reconcile(root.Children, desired.Children);", synchronizer, StringComparison.Ordinal);
        Assert.Contains("ApplyBranchWorktreeIndicators(worktrees);", synchronizer, StringComparison.Ordinal);
        Assert.Contains("if (HierarchyGuideSegments.SequenceEqual(segments)) return;", node, StringComparison.Ordinal);
        Assert.DoesNotContain("adjustedSegments", node, StringComparison.Ordinal);
        Assert.Contains("if (string.Equals(_associatedWorktreePath, path, StringComparison.Ordinal)) return;", node, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method '{signature}' was not found.");
        var brace = source.IndexOf('{', start);
        Assert.True(brace >= 0);
        var depth = 0;
        for (var index = brace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[start..(index + 1)];
        }
        throw new Xunit.Sdk.XunitException($"Method '{signature}' was not balanced.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
