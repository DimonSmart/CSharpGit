namespace CSharpGit.Application.Tests;

public sealed class WorkingTreeDiffUiContractTests
{
    [Fact]
    public void WorkingTreeDiffUsesExplicitReadOnlyContractAndTypedSelection()
    {
        var root = FindRepositoryRoot();
        var contract = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IWorkingTreeDiffService.cs"));
        var mutationContract = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IRepositoryStateService.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));

        Assert.Contains("enum WorkingTreeDiffKind", contract);
        Assert.Contains("Unstaged", contract);
        Assert.Contains("Staged", contract);
        Assert.Contains("interface IWorkingTreeDiffService", contract);
        Assert.Contains("WorkingTreeChange change", contract);
        Assert.DoesNotContain("ReadDiffAsync", ExtractInterface(mutationContract, "IWorkingTreeService"));
        Assert.Contains("SelectedWorkingTreeDiffKind", viewModel);
        Assert.Contains("SelectedWorkingTreeDiff", viewModel);
    }

    [Fact]
    public void WorkingTreeHasTwoListsResizableCompactDiffAndExclusiveSelection()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var workingTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeDiff.cs"));
        var compactLayout = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.CompactLayout.cs"));

        Assert.Contains("x:Name=\"UnstagedChangesList\"", xaml);
        Assert.Contains("x:Name=\"StagedChangesList\"", xaml);
        Assert.Contains("x:Name=\"WorkingTreeCompactDiffList\"", xaml);
        Assert.Contains("controls:GridSplitter", xaml);
        Assert.Contains("Text=\"OLD\"", xaml);
        Assert.Contains("Text=\"NEW\"", xaml);
        Assert.Contains("WorkingTreeDiffKindText", xaml);
        Assert.Contains("StagedChangesList.SelectedItem = null", workingTree);
        Assert.Contains("UnstagedChangesList.SelectedItem = null", workingTree);

        Assert.Contains("CompactResource<Style>(\"CompactDiffItemContainerStyle\")", workingTree);
        Assert.Contains("CompactResource<DataTemplate>(\"CompactDiffItemTemplate\")", workingTree);
        Assert.Contains("CompactDiffList.ItemContainerStyle = CompactResource<Style>(\"CompactDiffItemContainerStyle\")", compactLayout);
        Assert.Contains("CompactDiffList.ItemTemplate = CompactResource<DataTemplate>(\"CompactDiffItemTemplate\")", compactLayout);
        Assert.Contains("CompactDiffLine.Build", workingTree);
    }

    [Fact]
    public void WorkingTreeDiffIsLazyCancellationSafeAndDiscardPreservesIndex()
    {
        var root = FindRepositoryRoot();
        var workingTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeDiff.cs"));
        var git = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitCliRepositoryService.cs"));
        var gitDiff = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitCliRepositoryService.WorkingTreeDiff.cs"));

        Assert.Contains("CancellationTokenSource", workingTree);
        Assert.Contains("_workingTreeDiffGeneration", workingTree);
        Assert.Contains("IsCurrentWorkingTreeDiffRequest", workingTree);
        Assert.Contains("OperationCanceledException", workingTree);
        Assert.Contains("ReadDiffAsync(repository, change, kind", workingTree);
        Assert.DoesNotContain("foreach (var change in _viewModel.Changes)", workingTree);

        var discardStart = git.IndexOf("public async Task DiscardFileAsync", StringComparison.Ordinal);
        var discardEnd = git.IndexOf("public async Task CommitAsync", discardStart, StringComparison.Ordinal);
        var discard = git[discardStart..discardEnd];
        Assert.Contains("\"restore\", \"--worktree\", \"--\", change.Path", discard);
        Assert.DoesNotContain("--source=HEAD", discard);
        Assert.DoesNotContain("\"--staged\"", discard);

        Assert.Contains("\"--cached\"", gitDiff);
        Assert.Contains("\"--no-ext-diff\"", gitDiff);
        Assert.Contains("\"--find-renames\"", gitDiff);
        Assert.Contains("change.OriginalPath", gitDiff);
        Assert.Contains("\"--no-index\"", gitDiff);
        Assert.Contains("process.ExitCode is not 0 and not 1", gitDiff);
        Assert.Contains("ParseDiffLines(output)", gitDiff);
    }

    private static string ExtractInterface(string source, string name)
    {
        var start = source.IndexOf($"public interface {name}", StringComparison.Ordinal);
        var next = source.IndexOf("public interface ", start + 1, StringComparison.Ordinal);
        return source[start..(next < 0 ? source.Length : next)];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
