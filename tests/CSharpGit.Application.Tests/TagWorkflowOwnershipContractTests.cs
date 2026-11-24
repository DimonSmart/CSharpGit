namespace CSharpGit.Application.Tests;

public sealed class TagWorkflowOwnershipContractTests
{
    [Fact]
    public void MainPageOnlyBuildsTagMenusAndWorkflowOwnsTagDialogs()
    {
        var root = FindRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var page = File.ReadAllText(Path.Combine(presentation, "MainPage.Tags.cs"));
        var workflow = File.ReadAllText(Path.Combine(presentation, "Workflows", "TagWorkflowController.cs"));
        var composition = File.ReadAllText(Path.Combine(presentation, "MainPage.RepositoryMaintenance.cs"));

        Assert.Contains("_tagWorkflows.ShowCreateTagDialogAsync(", page, StringComparison.Ordinal);
        Assert.Contains("_tagWorkflows.DeleteTagFromUiAsync(", page, StringComparison.Ordinal);
        Assert.Contains("new Workflows.TagWorkflowController(", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.Tags.CreateTagAsync(", page, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.Tags.PushTagAsync(", page, StringComparison.Ordinal);
        Assert.Contains("_viewModel.Tags.CreateTagAsync(", workflow, StringComparison.Ordinal);
        Assert.Contains("_viewModel.Tags.PushTagAsync(", workflow, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(repository, _viewModel.Repository)", workflow, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CSharpGit.slnx")))
            d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("CSharpGit root not found");
    }
}
