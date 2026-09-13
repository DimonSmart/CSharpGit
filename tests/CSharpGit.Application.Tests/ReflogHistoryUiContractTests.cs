namespace CSharpGit.Application.Tests;

public sealed class ReflogHistoryUiContractTests
{
    [Fact]
    public void ReflogUsesHistoryQueryStateSemanticBadgeAndExistingReferenceNavigation()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var reflogState = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.Reflog.cs"));
        var navigation = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.ReferenceNavigation.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var git = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitReferenceHistoryService.cs"));

        Assert.Contains("x:Name=\"ShowReflogToggle\"", xaml);
        Assert.Contains("IsOn=\"{Binding ShowReflog, Mode=TwoWay}\"", xaml);
        Assert.Contains("new HistoryQuery(scope, filter, skip, IncludeReflog: _showReflog)", viewModel);
        Assert.Contains("_selectedScope != Scopes[0]", reflogState);
        Assert.Contains("DisableReflogForScopedHistoryAsync", page);
        Assert.Contains("IncludeReflog: ShowReflog", navigation);

        Assert.Contains("Text=\"reflog\"", workspace);
        Assert.Contains("Binding IsReflogOnly", workspace);
        Assert.Contains("This commit is not reachable from normal repository refs", workspace);
        Assert.Contains("AccentFillColorDefaultBrush", workspace);

        Assert.Contains("[\"--all\", \"--reflog\"]", git);
        Assert.Contains("\"rev-list\", \"--reflog\", \"--not\", \"--all\"", git);
        Assert.DoesNotContain(".git/logs", git);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
