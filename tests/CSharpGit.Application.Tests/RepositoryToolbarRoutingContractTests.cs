namespace CSharpGit.Application.Tests;

public sealed class RepositoryToolbarRoutingContractTests
{
    [Fact]
    public void ToolbarDispatchesActionsWithoutSynthesizingControlEvents()
    {
        var page = ReadMainPage();
        Assert.Contains("HandleRepositoryToolbarAction(RepositoryToolbarAction action)", page);
        Assert.Contains("RunPageUiOperation(_repositoryPush.ForcePushWithLeaseAsync", page);
        Assert.Contains("RunPageUiOperation(_repositoryPush.PushToAsync", page);
        Assert.Contains("RunPageUiOperation(_maintenanceController.OptimizeAsync", page);
        Assert.DoesNotContain("new RoutedEventArgs()", page);
        Assert.DoesNotContain("_Click(this, args)", page);
    }

    [Fact]
    public void F5AndToolbarUseTheSameManualRefreshOwner()
    {
        var page = ReadMainPage();
        Assert.Contains("RunPageUiOperation(_manualRefresh.RefreshAsync, \"Refresh shortcut\")", page);
        Assert.Contains("RunPageUiOperation(_manualRefresh.RefreshAsync, \"Repository refresh indicator\")", page);
        Assert.DoesNotContain("RefreshRepositoryFromUiAsync", page);
    }

    private static string ReadMainPage()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        var root = directory?.FullName ??
            throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
        return File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
    }
}
