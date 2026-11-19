namespace CSharpGit.Application.Tests;

public sealed class RecentRepositoryPickerOwnershipContractTests
{
    [Fact]
    public void NativePickerLivesOutsideMainPageAndRechecksSwitchAdmissionAfterAwait()
    {
        var root = FindRepositoryRoot();
        var p = Path.Combine(root, "src", "CSharpGit.Presentation");
        var workflow = File.ReadAllText(Path.Combine(p, "Workflows", "RecentRepositoryPickerWorkflow.cs"));
        var composition = File.ReadAllText(Path.Combine(p, "MainPage.RepositoryMaintenance.cs"));

        Assert.False(File.Exists(Path.Combine(p, "MainPage.RecentRepositories.cs")));
        Assert.Contains("await folderPicker.PickFolderAsync()", workflow);
        Assert.Contains("catch (OperationCanceledException)", workflow);
        Assert.Contains("if (!string.IsNullOrEmpty(path) && canChangeRepository())", workflow);
        Assert.Contains("await switchRepository(path)", workflow);
        Assert.Contains("await showDialog(new ContentDialog", workflow);
        Assert.Contains("new Workflows.RecentRepositoryPickerWorkflow(", composition);
        Assert.Contains("_viewModel.CanChangeRepository && !IsShuttingDown", composition);
        Assert.Contains("ShowPageDialogAsync);", composition);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ??
            throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
