namespace CSharpGit.Application.Tests;

public sealed class RepositoryToolbarRoutingContractTests
{
    [Fact]
    public void ToolbarDispatchesActionsWithoutSynthesizingControlEvents()
    {
        var page = ReadMainPage();
        var toolbar = ReadToolbarController();
        Assert.Contains("Handle(RepositoryToolbarAction action)", toolbar);
        Assert.Contains("_runUiOperation(_push.ForcePushWithLeaseAsync", toolbar);
        Assert.Contains("_runUiOperation(_push.PushToAsync", toolbar);
        Assert.Contains("_runUiOperation(_maintenance.OptimizeAsync", toolbar);
        Assert.DoesNotContain("new RoutedEventArgs()", toolbar);
        Assert.DoesNotContain("_Click(this, args)", toolbar);
    }

    [Fact]
    public void F5AndToolbarUseTheSameManualRefreshOwner()
    {
        var page = ReadMainPage();
        var toolbar = ReadToolbarController();
        Assert.Contains("runOperation(refresh, \"Refresh shortcut\")", ReadKeyboardController());
        Assert.DoesNotContain("Page_KeyDown", page);
        Assert.Contains("_runUiOperation(_refresh.RefreshAsync, \"Repository refresh indicator\")", toolbar);
        Assert.DoesNotContain("RefreshRepositoryFromUiAsync", page);
    }

    private static string ReadToolbarController()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        var root = directory?.FullName ??
            throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
        return File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Workflows", "RepositoryToolbarActionController.cs"));
    }

    private static string ReadKeyboardController()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        var root = directory?.FullName ??
            throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
        return File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Workflows", "WorkspaceKeyboardController.cs"));
    }

    private static string ReadMainPage()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        var root = directory?.FullName ??
            throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
        return File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Composition/WindowWorkspaceComposition.cs"));
    }
}
