namespace CSharpGit.Application.Tests;

public sealed class SubmoduleWorkflowOwnershipContractTests
{
    [Fact]
    public void SubmodulesUseAnIndependentWorkflowWithRepositoryGuards()
    {
        var root = FindRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var workflow = File.ReadAllText(Path.Combine(presentation, "Workflows", "SubmoduleWorkflowController.cs"));
        var repositoryFiles = File.ReadAllText(Path.Combine(presentation, "MainPage.RepositoryFiles.cs"));
        var composition = File.ReadAllText(Path.Combine(presentation, "MainPage.RepositoryMaintenance.cs"));
        Assert.False(File.Exists(Path.Combine(presentation, "MainPage.Submodules.cs")));
        Assert.Contains("_submodules.ShowSubmoduleMenuAsync(", repositoryFiles, StringComparison.Ordinal);
        Assert.Contains("_submodules.OpenSelectedSubmoduleAsync(", repositoryFiles, StringComparison.Ordinal);
        Assert.Contains("new Workflows.SubmoduleWorkflowController(", composition, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(repository, _viewModel.Repository)", workflow, StringComparison.Ordinal);
        Assert.Contains("TryBeginUserGitOperation(repository)", workflow, StringComparison.Ordinal);
        Assert.Contains("_submoduleService.UpdateAllAsync(repository, token)", workflow, StringComparison.Ordinal);
        Assert.Contains("_repositoryFilesViewModel.RefreshAsync()", workflow, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CSharpGit.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root not found");
    }
}
