namespace CSharpGit.Application.Tests;

public sealed class RepositoryCloningUiContractTests
{
    [Fact]
    public void CloneIsExposedFromAllRepositoryEntryPoints()
    {
        var root = FindRepositoryRoot();
        var mainXaml = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var recentXaml = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "RecentRepositoriesView.xaml"));
        var recentViewModel = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "ViewModels", "RecentRepositoriesViewModel.cs"));
        var switching = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.RepositorySwitching.cs"));
        var workflow = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.RepositoryCloning.cs"));

        Assert.Contains("Clone repository…", mainXaml);
        Assert.Contains("CloneRepository_Click", mainXaml);
        Assert.Contains("Clone repository", recentXaml);
        Assert.Contains("Clone from a remote URL…", recentXaml);
        Assert.Contains("CloneRepositoryCommand", recentXaml);
        Assert.Contains("CloneRepositoryCommand", recentViewModel);
        Assert.Contains(""Clone repository…"", switching);
        Assert.Equal(1, Count(workflow, "private async Task ShowCloneRepositoryAsync()"));
    }

    [Fact]
    public void CloneWorkflowProtectsDraftAndUsesCommonRepositorySwitch()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.RepositoryCloning.cs"));

        Assert.Contains("ConfirmDiscardCommitMessageAsync(closing: false)", workflow);
        Assert.Contains("_cloneRepositoryViewModel.CloneAsync()", workflow);
        Assert.Contains("SwitchRepositoryCoreAsync(path, discardDraft)", workflow);
        Assert.Contains("Repository cloned, but could not be opened.", workflow);
        Assert.Contains("_cloneRepositoryViewModel.Cancel()", workflow);
        Assert.DoesNotContain("Directory.Delete", workflow);

        Assert.True(
            workflow.IndexOf(
                "ConfirmDiscardCommitMessageAsync(closing: false)",
                StringComparison.Ordinal)
            < workflow.IndexOf(
                "_cloneRepositoryViewModel.CloneAsync()",
                StringComparison.Ordinal));
    }

    [Fact]
    public void CloneDialogHasCompactBusyAndCancelSemantics()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.RepositoryCloning.cs"));

        Assert.Contains("Header = "Repository URL"", workflow);
        Assert.Contains("Header = "Local directory"", workflow);
        Assert.Contains("Content = "Browse…"", workflow);
        Assert.Contains("Content = "Clone"", workflow);
        Assert.Contains("Content = "Cancel"", workflow);
        Assert.Contains("ProgressRing", workflow);
        Assert.Contains("Cloning {_cloneRepositoryViewModel.RepositoryDisplayName}…", workflow);
        Assert.Contains("repositoryUrlBox.IsEnabled = !busy", workflow);
        Assert.Contains("localDirectoryBox.IsEnabled = !busy", workflow);
    }

    [Fact]
    public void SettingsExposeDefaultRepositoriesFolder()
    {
        var root = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "SettingsPage.xaml"));

        Assert.Contains("Default repositories folder", settings);
        Assert.Contains("Used as the default location for cloned repositories.", settings);
        Assert.Contains("DefaultRepositoriesDirectory", settings);
        Assert.Contains("Browse…", settings);
    }

    private static int Count(string value, string fragment) =>
        (value.Length - value.Replace(fragment, string.Empty, StringComparison.Ordinal).Length)
        / fragment.Length;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "CSharpGit repository root was not found.");
    }
}
