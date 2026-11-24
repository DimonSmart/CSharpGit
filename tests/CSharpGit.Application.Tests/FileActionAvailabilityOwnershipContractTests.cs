namespace CSharpGit.Application.Tests;

public sealed class FileActionAvailabilityOwnershipContractTests
{
    [Fact]
    public void FileActionButtonsHaveASeparatePresentationOwner()
    {
        var root = FindRoot();
        var p = Path.Combine(root, "src", "CSharpGit.Presentation");
        var composition = File.ReadAllText(Path.Combine(p, "MainPage.RepositoryMaintenance.cs"));
        var interaction = File.ReadAllText(Path.Combine(p, "Workflows", "FileActionInteractionController.cs"));
        var owner = File.ReadAllText(Path.Combine(p, "Workflows", "FileActionAvailabilityController.cs"));
        Assert.False(File.Exists(Path.Combine(p, "MainPage.FileOpening.cs")));
        Assert.Contains("new Workflows.FileActionAvailabilityController(", composition);
        Assert.Contains("_availability.UpdateCommitButtons();", interaction);
        Assert.Contains("_availability.UpdateWorkingTreeButtons();", interaction);
        Assert.DoesNotContain("private void UpdateCommitButtons()", interaction);
        Assert.DoesNotContain("private void SetRevealButton(", interaction);
        Assert.Contains("internal void UpdateCommitButtons()", owner);
        Assert.Contains("internal void UpdateWorkingTreeButtons()", owner);
        Assert.Contains("FileActionsViewModel.CanExternalDiff(pair)", owner);
        Assert.Contains("_fileActions.RevealDescription", owner);
    }

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CSharpGit.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("CSharpGit root not found");
    }
}
