namespace CSharpGit.Application.Tests;

public sealed class FileActionAvailabilityOwnershipContractTests
{
    [Fact]
    public void FileActionButtonsHaveASeparatePresentationOwner()
    {
        var root = FindRoot();
        var p = Path.Combine(root, "src", "CSharpGit.Presentation");
        var page = File.ReadAllText(Path.Combine(p, "MainPage.FileOpening.cs"));
        var owner = File.ReadAllText(Path.Combine(p, "Workflows", "FileActionAvailabilityController.cs"));
        Assert.Contains("new Workflows.FileActionAvailabilityController(", page);
        Assert.Contains("_fileActionButtons.UpdateCommitButtons();", page);
        Assert.Contains("_fileActionButtons.UpdateWorkingTreeButtons();", page);
        Assert.DoesNotContain("private void UpdateCommitButtons()", page);
        Assert.DoesNotContain("private void SetRevealButton(", page);
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
