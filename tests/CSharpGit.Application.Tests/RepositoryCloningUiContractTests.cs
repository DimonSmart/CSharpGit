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
        Assert.Contains("Text=\"Clone repository…\"", mainXaml);
        Assert.Contains("Icon.Glyph.Clone", mainXaml);
        Assert.Contains("RepositorySelectorCloneRepository_Click", switching);
        Assert.Equal(1, Count(workflow, "private async Task ShowCloneRepositoryAsync()"));
    }

    [Fact]
    public void MainPageCompositionInjectsCloneRepositoryViewModel()
    {
        var root = FindRepositoryRoot();
        var composition = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.RepositoryMaintenance.cs"));

        const string createParameter =
            "CreateRepositoryViewModel createRepositoryViewModel";
        const string cloneParameter =
            "CloneRepositoryViewModel cloneRepositoryViewModel";
        const string cloneAssignment =
            "_cloneRepositoryViewModel = cloneRepositoryViewModel";
        const string cloneNullGuard =
            "?? throw new ArgumentNullException(nameof(cloneRepositoryViewModel));";

        Assert.Contains(cloneParameter, composition);
        Assert.Contains(cloneAssignment, composition);
        Assert.Contains(cloneNullGuard, composition);

        var createParameterIndex = composition.IndexOf(
            createParameter,
            StringComparison.Ordinal);
        var cloneParameterIndex = composition.IndexOf(
            cloneParameter,
            StringComparison.Ordinal);
        var cloneAssignmentIndex = composition.IndexOf(
            cloneAssignment,
            StringComparison.Ordinal);
        var cloneNullGuardIndex = composition.IndexOf(
            cloneNullGuard,
            cloneAssignmentIndex,
            StringComparison.Ordinal);

        Assert.True(createParameterIndex >= 0);
        Assert.True(cloneParameterIndex > createParameterIndex);
        Assert.True(cloneAssignmentIndex > cloneParameterIndex);
        Assert.True(cloneNullGuardIndex > cloneAssignmentIndex);
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

        Assert.Contains("Header = \"Repository URL\"", workflow);
        Assert.Contains("Header = \"Local directory\"", workflow);
        Assert.Contains("Content = \"Browse…\"", workflow);
        Assert.Contains("Content = \"Clone\"", workflow);
        Assert.Contains("Content = \"Cancel\"", workflow);
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
