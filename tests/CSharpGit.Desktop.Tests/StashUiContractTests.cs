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
    public void CreateStashIsContextualRootWorkflowAndLegacyDialogStateIsRemoved()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var stashPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Stashes.cs"));
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

        Assert.Contains("Title = \"Create stash\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("Header = \"Message\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("PlaceholderText = \"Optional stash message\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Create stash\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("CloseButtonText = \"Cancel\"", stashPage, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Primary", stashPage, StringComparison.Ordinal);
        Assert.Contains("await _viewModel.CreateStashAsync(message.Text)", stashPage, StringComparison.Ordinal);

        Assert.DoesNotContain("Text=\"Stash\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("StashMessage", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateStashCommand", xaml, StringComparison.Ordinal);

        Assert.Contains("public bool CanCreateStash => CanMutate()", viewModel, StringComparison.Ordinal);
        Assert.Contains("CurrentOperation == RepositoryOperation.None", viewModel, StringComparison.Ordinal);
        Assert.Contains("!Conflicts.Any(conflict => !conflict.IsResolved)", viewModel, StringComparison.Ordinal);
        Assert.Contains("public async Task CreateStashAsync(string? message)", viewModel, StringComparison.Ordinal);
        Assert.Contains("if (!CanCreateStash) return;", viewModel, StringComparison.Ordinal);
        Assert.Contains("_workflowService.CreateStashAsync(Repository!, message)", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateStashCommand", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("StashMessage", viewModel, StringComparison.Ordinal);
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
