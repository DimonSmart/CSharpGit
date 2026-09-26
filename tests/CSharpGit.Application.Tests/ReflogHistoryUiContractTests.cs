namespace CSharpGit.Application.Tests;

public sealed class ReflogHistoryUiContractTests
{
    [Fact]
    public void ReflogUsesUnifiedHistorySelectorSemanticBadgeAndExistingReferenceNavigation()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var historyReferences = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "HistoryReferences.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var reflogState = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.Reflog.cs"));
        var selector = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.HistoryDisplayMode.cs"));
        var navigation = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.ReferenceNavigation.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));
        var git = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitReferenceHistoryService.cs"));

        Assert.Contains("<SelectorBar x:Name=\"ScopeCombo\"", xaml);
        Assert.Contains("x:Name=\"CurrentScopeItem\"", xaml);
        Assert.Contains("Text=\"Current\"", xaml);
        Assert.Contains("x:Name=\"AllScopeItem\"", xaml);
        Assert.Contains("Text=\"All\"", xaml);
        Assert.Contains("x:Name=\"ShowReflogToggle\"", xaml);
        Assert.Contains("Text=\"+ Reflog\"", xaml);
        Assert.Contains("SelectionChanged=\"HistoryDisplayModeSelector_SelectionChanged\"", xaml);
        Assert.DoesNotContain("<ToggleSwitch x:Name=\"ShowReflogToggle\"", xaml);
        Assert.Contains("SetHistoryDisplayMode", reflogState);
        Assert.Contains("HistoryDisplayMode.AllReferencesWithReflog", reflogState);
        Assert.Contains("_viewModel.SetHistoryDisplayMode(mode)", selector);
        Assert.Contains("IncludeReflog: _showReflog", viewModel);
        Assert.Contains("HeadExists: _headExists", viewModel);
        Assert.Contains("DisableReflogForScopedHistoryAsync", page);
        Assert.Contains("IncludeReflog: ShowReflog", navigation);

        Assert.Contains("Text=\"reflog\"", historyReferences);
        Assert.Contains("x:Bind IsReflogOnly", historyReferences);
        Assert.Contains("This commit is not reachable from normal repository refs", historyReferences);
        Assert.Contains("AccentFillColorDefaultBrush", historyReferences);

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
