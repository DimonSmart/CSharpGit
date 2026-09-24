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
    public void CreateStashUsesSharedWorkflowFromRepositoryTreeAndWorkingTree()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var stashPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Stashes.cs"));
        var commitActions = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.CommitActions.cs"));
        var workingTreeContextMenu = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeContextMenu.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));

        Assert.Contains("RepositoryTreeDescriptorBuilder.StashesRootKey", page, StringComparison.Ordinal);
        Assert.Contains("string.Equals(node.Key", page, StringComparison.Ordinal);
        Assert.DoesNotContain("node.Name == \"Stashes\"", page, StringComparison.Ordinal);
        Assert.Contains("\"Create stash…\"", page, StringComparison.Ordinal);
        Assert.Contains("_viewModel.CanCreateStash", page, StringComparison.Ordinal);
        Assert.Contains("ShowCreateStashDialogAsync", page, StringComparison.Ordinal);

        var stashEntryCase = SliceStashContextMenuCase(page);
        Assert.Contains("\"Apply\"", stashEntryCase, StringComparison.Ordinal);
        Assert.Contains("\"Pop\"", stashEntryCase, StringComparison.Ordinal);
        Assert.DoesNotContain("Create stash", stashEntryCase, StringComparison.Ordinal);

        var commitActionArea = SliceCommitActionArea(xaml);
        Assert.Contains("Content=\"Commit\"", commitActionArea, StringComparison.Ordinal);
        Assert.Contains("Content=\"Amend\"", commitActionArea, StringComparison.Ordinal);
        Assert.Contains("Content=\"Create stash…\"", commitActionArea, StringComparison.Ordinal);
        Assert.Contains("Click=\"CreateStash_Click\"", commitActionArea, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanCreateStash}\"", commitActionArea, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource CompactButtonStyle}\"", commitActionArea, StringComparison.Ordinal);

        var clickHandler = SliceMethod(
            stashPage,
            "private async void CreateStash_Click",
            "private async Task ShowCreateStashDialogAsync");
        Assert.Contains("await ShowCreateStashDialogAsync()", clickHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentDialog", clickHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("new TextBox", clickHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.CreateStashAsync", clickHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("IRepositoryWorkflowService", clickHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("Refresh", clickHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("CommitMessage", clickHandler, StringComparison.Ordinal);

        Assert.Contains("var message = new TextBox", stashPage, StringComparison.Ordinal);
        Assert.Contains("Title = \"Create stash\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("Header = \"Message\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("PlaceholderText = \"Optional stash message\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Create stash\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("CloseButtonText = \"Cancel\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Primary", stashPage, StringComparison.Ordinal);
        Assert.Contains("await _viewModel.CreateStashAsync(message.Text)", stashPage, StringComparison.Ordinal);
        Assert.DoesNotContain("CommitMessage", stashPage, StringComparison.Ordinal);

        Assert.DoesNotContain("CreateStashCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("StashMessage", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateStashCommand", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("StashMessage", viewModel, StringComparison.Ordinal);

        Assert.Contains("public bool CanCreateStash => CanMutate()", viewModel, StringComparison.Ordinal);
        Assert.Contains("CurrentOperation == RepositoryOperation.None", viewModel, StringComparison.Ordinal);
        Assert.Contains("!Conflicts.Any(conflict => !conflict.IsResolved)", viewModel, StringComparison.Ordinal);
        Assert.Contains("public async Task CreateStashAsync(string? message)", viewModel, StringComparison.Ordinal);
        Assert.Contains("if (!CanCreateStash) return;", viewModel, StringComparison.Ordinal);
        Assert.Contains("_workflowService.CreateStashAsync(Repository!, message)", viewModel, StringComparison.Ordinal);

        Assert.DoesNotContain("Create stash", commitActions, StringComparison.Ordinal);
        Assert.DoesNotContain("Create stash", workingTreeContextMenu, StringComparison.Ordinal);
    }

    private static string SliceCommitActionArea(string xaml)
    {
        var editor = xaml.IndexOf("x:Name=\"CommitMessageEditor\"", StringComparison.Ordinal);
        Assert.True(editor >= 0);

        const string panelMarker = "<StackPanel Orientation=\"Horizontal\" Spacing=\"{StaticResource Spacing.S}\">";
        var start = xaml.IndexOf(panelMarker, editor, StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = xaml.IndexOf("</StackPanel>", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return xaml[start..(end + "</StackPanel>".Length)];
    }

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string SliceStashContextMenuCase(string source)
    {
        var applyAction = source.IndexOf("AddMenuItem(flyout, \"Apply\"", StringComparison.Ordinal);
        Assert.True(applyAction >= 0);
        var start = source.LastIndexOf("case RepositoryTreeNodeKind.Stash", applyAction, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("break;", applyAction, StringComparison.Ordinal);
        Assert.True(end > start);
        return source[start..end];
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
