using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class StashUiContractTests
{
    [Fact]
    public void EmptyRepositoryTreeKeepsStableStashesRoot()
    {
        var roots = RepositoryTreeDescriptorBuilder.Build([], [], [], [], [], []);

        var stashes = Assert.Single(
            roots,
            node => node.Key == RepositoryTreeDescriptorBuilder.StashesRootKey);

        Assert.Equal(RepositoryTreeNodeKind.Group, stashes.Kind);
        Assert.Equal("Stashes", stashes.Name);
        Assert.Empty(stashes.Children);
    }

    [Fact]
    public void RepositoryLevelStashUsesOneDialogAndSemanticRequest()
    {
        var root = FindRepositoryRoot();
        var xaml = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml");
        var page = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs");
        var stashPage = Read(root, "src", "CSharpGit.Presentation", "MainPage.Stashes.cs");
        var stashViewModel = Read(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.Stashes.cs");

        Assert.Contains("Content=\"Stash…\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"CreateStash_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("\"Stash…\"", page, StringComparison.Ordinal);
        Assert.Contains("ShowCreateStashDialogAsync", page, StringComparison.Ordinal);

        Assert.Contains("Title = \"Stash changes\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("Content = \"All tracked changes\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("Content = \"Staged changes only\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("Content = \"Include untracked files\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("includeUntracked.IsChecked = false", stashPage, StringComparison.Ordinal);
        Assert.Contains("includeUntracked.IsEnabled = !staged", stashPage, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Stash\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("new CreateStashRequest", stashPage, StringComparison.Ordinal);
        Assert.DoesNotContain("CommitMessage", stashPage, StringComparison.Ordinal);

        Assert.Contains("CanCreateStashRequest", stashViewModel, StringComparison.Ordinal);
        Assert.Contains("StashScope.StagedChangesOnly", stashViewModel, StringComparison.Ordinal);
        Assert.Contains("StashScope.AllTrackedChanges", stashViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedPathStashIsExplicitAndIncludesSelectedUntrackedFiles()
    {
        var root = FindRepositoryRoot();
        var menu = Read(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeContextMenu.cs");
        var stashPage = Read(root, "src", "CSharpGit.Presentation", "MainPage.Stashes.cs");
        var stashViewModel = Read(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.Stashes.cs");

        Assert.Contains("\"Stash selected…\"", menu, StringComparison.Ordinal);
        Assert.Contains("$\"Stash {count} files…\"", menu, StringComparison.Ordinal);
        Assert.Contains("ShowCreateSelectedStashDialogAsync(changes)", menu, StringComparison.Ordinal);

        Assert.Contains("Title = \"Stash selected changes\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("Includes 1 untracked file.", stashPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Include untracked files", SliceSelectedDialog(stashPage), StringComparison.Ordinal);

        Assert.Contains("StashScope.SelectedPaths", stashViewModel, StringComparison.Ordinal);
        Assert.Contains("change.OriginalPath", stashViewModel, StringComparison.Ordinal);
        Assert.Contains("change.Kind == FileChangeKind.Untracked", stashViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void StashSelectionUsesStashPresentationInsteadOfReferenceNavigation()
    {
        var root = FindRepositoryRoot();
        var page = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs");
        var xaml = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml");
        var repositoryFiles = Read(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryFiles.cs");

        var stashSelection = SliceCase(page, "case RepositoryTreeNodeKind.Stash when node.Value is GitStash stash:");
        Assert.Contains("await _viewModel.SelectStashAsync(stash)", stashSelection, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigateToReferenceAsync(stash.Commit)", stashSelection, StringComparison.Ordinal);

        Assert.Contains("Visibility=\"{Binding HasSelectedDetailsObject", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding SelectedDetailsTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedStashDisplay", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedStashBaseDisplay", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedStashStatsDisplay", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ApplyStashCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding PopStashCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"DropSelectedStash_Click\"", xaml, StringComparison.Ordinal);

        Assert.Contains("_viewModel.SelectedObjectCommit", repositoryFiles, StringComparison.Ordinal);
        Assert.Contains("\"Tracked files\"", repositoryFiles, StringComparison.Ordinal);
        Assert.Contains("Untracked stash files are shown in Changes.", repositoryFiles, StringComparison.Ordinal);
    }

    [Fact]
    public void StashActionsIncludeSafeDropAndStableMutationCommands()
    {
        var root = FindRepositoryRoot();
        var page = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs");
        var stashPage = Read(root, "src", "CSharpGit.Presentation", "MainPage.Stashes.cs");
        var stashViewModel = Read(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.Stashes.cs");

        var stashMenu = SliceCase(page, "case RepositoryTreeNodeKind.Stash when node.Value is GitStash stash:");
        Assert.Contains("\"Apply\"", stashMenu, StringComparison.Ordinal);
        Assert.Contains("\"Pop\"", stashMenu, StringComparison.Ordinal);
        Assert.Contains("\"Drop…\"", stashMenu, StringComparison.Ordinal);
        Assert.Contains("CanMutateStash(stash)", stashMenu, StringComparison.Ordinal);

        Assert.Contains("Title = \"Drop stash?\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("This permanently removes the selected stash.", stashPage, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Drop stash\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Close", stashPage, StringComparison.Ordinal);

        Assert.Contains("_workflowService.ApplyStashAsync", stashViewModel, StringComparison.Ordinal);
        Assert.Contains("_workflowService.PopStashAsync", stashViewModel, StringComparison.Ordinal);
        Assert.Contains("_workflowService.DropStashAsync", stashViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangesExposeHistoricalStashStateAndNetZeroMessage()
    {
        var root = FindRepositoryRoot();
        var page = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs");
        var changes = Read(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs");
        var presentation = Read(root, "src", "CSharpGit.Presentation", "MainPage.DiffPresentation.cs");
        var stashViewModel = Read(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.Stashes.cs");

        Assert.Contains("GetChangedFileDisplayStatus(file)", page, StringComparison.Ordinal);
        Assert.Contains("StateLabel", stashViewModel, StringComparison.Ordinal);
        Assert.Contains("IsNoNetStashDiff", changes, StringComparison.Ordinal);
        Assert.Contains("NoNetStashDiff", presentation, StringComparison.Ordinal);
        Assert.Contains("No net working-tree diff", presentation, StringComparison.Ordinal);
        Assert.Contains("staged and unstaged changes for this file cancel each other", presentation, StringComparison.Ordinal);
    }

    private static string SliceSelectedDialog(string source)
    {
        var start = source.IndexOf("private async Task ShowCreateSelectedStashDialogAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async void DropSelectedStash_Click", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string SliceCase(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("break;", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return source[start..end];
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));

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
