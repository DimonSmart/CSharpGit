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
        var xaml = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml")
            + Read(root, "src", "CSharpGit.Presentation", "Views", "HistoryWorkspaceView.xaml")
            + Read(root, "src", "CSharpGit.Presentation", "Views", "WorkingTreeView.xaml");
        var editor = Read(root, "src", "CSharpGit.Presentation", "Views", "WorkingTreeCommitEditorView.xaml");
        var editorCode = Read(root, "src", "CSharpGit.Presentation", "Views", "WorkingTreeCommitEditorView.xaml.cs");
        var composer = Read(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryMaintenance.cs");
        var page = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs");
        var stashPage = Read(root, "src", "CSharpGit.Presentation", "Workflows", "StashDialogController.cs");
        var stashViewModel = Read(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "StashesViewModel.cs");

        Assert.Contains("<views:WorkingTreeCommitEditorView", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Stash…\"", editor, StringComparison.Ordinal);
        Assert.Contains("Icon.Glyph.Stash", editor, StringComparison.Ordinal);
        Assert.Contains("Click=\"Stash_Click\"", editor, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding Stashes.CanCreateStash}\"", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"{Binding CanCreateStash}\"", editor, StringComparison.Ordinal);
        Assert.Contains("StashRequested?.Invoke", editorCode, StringComparison.Ordinal);
        var interactions = Read(root, "src", "CSharpGit.Presentation", "Workflows", "StashInteractionController.cs");
        Assert.Contains("_commitEditor.StashRequested += CreateStash_Click;", interactions, StringComparison.Ordinal);
        Assert.Contains("_runUiOperation(_dialogs.ShowCreateAsync", interactions, StringComparison.Ordinal);
        Assert.Contains("new Workflows.StashDialogController(", composer, StringComparison.Ordinal);
        var menu = Read(root, "src", "CSharpGit.Presentation", "Workflows", "RepositoryReferenceMenuController.cs");
        Assert.Contains("\"Stash…\"", menu, StringComparison.Ordinal);
        Assert.Contains("_stashes.ShowCreateAsync", menu, StringComparison.Ordinal);

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
        var menu = Read(root, "src", "CSharpGit.Presentation", "Views", "WorkingTreeView.ContextMenu.cs");
        var stashPage = Read(root, "src", "CSharpGit.Presentation", "Workflows", "StashDialogController.cs");
        var stashViewModel = Read(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "StashesViewModel.cs");

        Assert.Contains("\"Stash selected…\"", menu, StringComparison.Ordinal);
        Assert.Contains("$\"Stash {count} files…\"", menu, StringComparison.Ordinal);
        Assert.Contains("_showSelectedStashDialogAsync(changes)", menu, StringComparison.Ordinal);

        Assert.Contains("Title = \"Stash selected changes\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("Includes 1 untracked file.", stashPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Include untracked files", SliceSelectedDialog(stashPage), StringComparison.Ordinal);

        Assert.Contains("StashScope.SelectedPaths", stashViewModel, StringComparison.Ordinal);
        Assert.Contains("change.OriginalPath", stashViewModel, StringComparison.Ordinal);
        Assert.Contains("change.Kind == FileChangeKind.Untracked", stashViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void StashSelectionPreservesStashPresentationAndNavigatesMatchingHistoryCommit()
    {
        var root = FindRepositoryRoot();
        var page = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs");
        var xaml = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml")
            + Read(root, "src", "CSharpGit.Presentation", "Views", "HistoryWorkspaceView.xaml");
        var repositoryFiles = Read(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "RepositoryFilesViewModel.cs");
        var historyAdapter = Read(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.History.cs");
        var detailsComposition = Read(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.DetailsComposition.cs");
        var detailsViewModel = Read(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "CommitDetailsViewModel.cs");

        var navigation = Read(root, "src", "CSharpGit.Presentation", "Workflows", "WorkspaceNavigationController.cs");
        var stashSelection = SliceFirstCase(navigation, "case RepositoryTreeNodeKind.Stash when node.Value is GitStash stash:");
        Assert.Contains("await _viewModel.Stashes.SelectStashAsync(stash)", stashSelection, StringComparison.Ordinal);
        Assert.Contains("stash.Commit,", stashSelection, StringComparison.Ordinal);
        Assert.Contains("preserveSelectedStash: true", stashSelection, StringComparison.Ordinal);
        Assert.Contains("treeInteractionVersion: interactionVersion", stashSelection, StringComparison.Ordinal);
        Assert.Contains("string.Equals(selectedRow.Commit.Hash, selectedStash.Commit, StringComparison.Ordinal)", historyAdapter, StringComparison.Ordinal);
        Assert.Contains("string.Equals(historyRow.Commit.Hash, stash.Commit, StringComparison.Ordinal)", detailsComposition, StringComparison.Ordinal);

        Assert.Contains("Visibility=\"{Binding CommitDetails.HasSelectedDetailsObject", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding CommitDetails.SelectedDetailsTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommitDetails.SelectedStashDisplay", xaml, StringComparison.Ordinal);
        Assert.Contains("CommitDetails.SelectedStashBaseDisplay", xaml, StringComparison.Ordinal);
        Assert.Contains("CommitDetails.SelectedStashStatsDisplay", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding Stashes.ApplyCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding Stashes.PopCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"DropSelectedStash_Click\"", xaml, StringComparison.Ordinal);

        Assert.Contains("context?.SelectedObjectCommit", repositoryFiles, StringComparison.Ordinal);
        var filesSurface = Read(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryFiles.cs");
        Assert.Contains("\"Tracked files\"", filesSurface, StringComparison.Ordinal);
        Assert.Contains("Untracked stash files are shown in Changes.", detailsViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void StashActionsIncludeSafeDropAndStableMutationCommands()
    {
        var root = FindRepositoryRoot();
        var page = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs");
        var stashPage = Read(root, "src", "CSharpGit.Presentation", "Workflows", "StashDialogController.cs");
        var stashViewModel = Read(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "StashesViewModel.cs");

        var navigation = Read(root, "src", "CSharpGit.Presentation", "Workflows", "RepositoryReferenceMenuController.cs");
        Assert.Contains("PopulateStash(flyout, stash);", navigation, StringComparison.Ordinal);
        var start = navigation.IndexOf("private void PopulateStash(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = navigation.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
        Assert.True(end > start);
        var stashMenu = navigation[start..end];
        Assert.Contains("\"Apply\"", stashMenu, StringComparison.Ordinal);
        Assert.Contains("\"Pop\"", stashMenu, StringComparison.Ordinal);
        Assert.Contains("\"Drop…\"", stashMenu, StringComparison.Ordinal);
        Assert.Contains("CanMutateStash(stash)", stashMenu, StringComparison.Ordinal);

        Assert.Contains("Title = \"Drop stash?\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("This permanently removes the selected stash.", stashPage, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Drop stash\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Close", stashPage, StringComparison.Ordinal);

        Assert.Contains("_stashMutationService.ApplyStashAsync", stashViewModel, StringComparison.Ordinal);
        Assert.Contains("_stashMutationService.PopStashAsync", stashViewModel, StringComparison.Ordinal);
        Assert.Contains("_stashMutationService.DropStashAsync", stashViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangesExposeHistoricalStashStateAndNetZeroMessage()
    {
        var root = FindRepositoryRoot();
        var page = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs");
        var changes = Read(root, "src", "CSharpGit.Presentation", "Views", "HistoryWorkspaceView.Changes.cs");
        var presentation = Read(root, "src", "CSharpGit.Presentation", "Workflows", "DiffPresentationController.cs");
        var detailsViewModel = Read(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "CommitDetailsViewModel.cs");

        Assert.Contains("GetChangedFileDisplayStatus(file)", changes, StringComparison.Ordinal);
        Assert.Contains("StateLabel", detailsViewModel, StringComparison.Ordinal);
        Assert.Contains("IsNoNetStashDiff", changes, StringComparison.Ordinal);
        Assert.Contains("NoNetStashDiff", presentation, StringComparison.Ordinal);
        Assert.Contains("No net working-tree diff", presentation, StringComparison.Ordinal);
        Assert.Contains("staged and unstaged changes for this file cancel each other", presentation, StringComparison.Ordinal);
    }

    private static string SliceSelectedDialog(string source)
    {
        var start = source.IndexOf("internal async Task ShowCreateSelectedAsync", StringComparison.Ordinal);
        var end = source.IndexOf("internal async Task ConfirmDropAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string SliceFirstCase(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("break;", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return source[start..end];
    }

    private static string SliceCase(string source, string marker)
    {
        var start = source.LastIndexOf(marker, StringComparison.Ordinal);
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
