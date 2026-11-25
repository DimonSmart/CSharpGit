namespace CSharpGit.Application.Tests;

public sealed class RepositoryCloningUiContractTests
{
    [Fact]
    public void CloneIsExposedFromAllRepositoryEntryPoints()
    {
        var root = FindRepositoryRoot();
        var mainXaml = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.xaml"))
            + File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "StartScreenView.xaml"))
            + File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "RepositoryToolbarView.xaml"))
            + File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "RepositorySelectorView.xaml"));
        var recentXaml = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "RecentRepositoriesView.xaml"));
        var recentViewModel = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "ViewModels", "RecentRepositoriesViewModel.cs"));
        var switching = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "Views", "RepositorySelectorView.xaml.cs"));
        var workflow = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "Workflows", "RepositoryCloneWorkflow.cs"));

        Assert.Contains("Clone repository…", mainXaml);
        Assert.Contains("CloneRepository_Click", mainXaml);
        Assert.Contains("Clone repository", recentXaml);
        Assert.Contains("Clone from a remote URL…", recentXaml);
        Assert.Contains("CloneRepositoryCommand", recentXaml);
        Assert.Contains("CloneRepositoryCommand", recentViewModel);
        Assert.Contains("Text=\"Clone repository…\"", mainXaml);
        Assert.Contains("Icon.Glyph.Clone", mainXaml);
        Assert.Contains("RepositorySelectorCloneRepository_Click", switching);
        Assert.Equal(1, Count(workflow, "internal async Task ShowAsync()"));
    }

    [Fact]
    public void MainPageCompositionInjectsCloneRepositoryViewModel()
    {
        var root = FindRepositoryRoot();
        var composition = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "Composition/WindowWorkspaceComposition.Repository.cs"));

        var dependencies = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "Composition", "MainPageDependencies.cs"));
        Assert.Contains("CreateRepositoryViewModel CreateRepository", dependencies);
        Assert.Contains("CloneRepositoryViewModel CloneRepository", dependencies);
        Assert.Contains("var createRepositoryViewModel = repositoryDependencies.CreateRepository;", composition);
        Assert.Contains("var cloneRepositoryViewModel = repositoryDependencies.CloneRepository;", composition);
        Assert.Contains("_cloneRepositoryViewModel = cloneRepositoryViewModel", composition);
        Assert.Contains("?? throw new ArgumentNullException(nameof(cloneRepositoryViewModel));", composition);
        Assert.Contains("new Workflows.RepositoryCloneWorkflow(", composition);
        Assert.Contains("_viewModel, _cloneRepositoryViewModel,", composition);
    }

    [Fact]
    public void CloneWorkflowProtectsDraftAndUsesCommonRepositorySwitch()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "Workflows", "RepositoryCloneWorkflow.cs"));

        Assert.Contains("_confirmDiscardDraft()", workflow);
        Assert.Contains("_cloneRepositoryViewModel.CloneAsync()", workflow);
        Assert.Contains("_switchRepository(path, discardDraft, _cloneWorkflowAdmission)", workflow);
        Assert.Contains("TryBeginRepositoryLifecycle()", workflow);
        Assert.Contains("Repository cloned, but could not be opened.", workflow);
        Assert.Contains("_cloneRepositoryViewModel.Cancel()", workflow);
        Assert.DoesNotContain("Directory.Delete", workflow);

        Assert.True(
            workflow.IndexOf(
                "_confirmDiscardDraft()",
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
            root, "src", "CSharpGit.Presentation", "Workflows", "RepositoryCloneWorkflow.cs"));

        Assert.Contains("Header = \"Repository URL\"", workflow);
        Assert.Contains("Header = \"Local directory\"", workflow);
        Assert.Contains("Content = \"Browse…\"", workflow);
        Assert.Contains("Content = \"Clone\"", workflow);
        Assert.Contains("Content = \"Cancel\"", workflow);
        Assert.Contains("ProgressRing", workflow);
        Assert.Contains("Cloning {_cloneRepositoryViewModel.RepositoryDisplayName}…", workflow);
        Assert.Contains("repositoryUrlBox.IsEnabled = !busy", workflow);
        Assert.Contains("localDirectoryBox.IsEnabled = !busy", workflow);
        Assert.Contains("await _showDialog(dialog);", workflow);
        Assert.DoesNotContain("await dialog.ShowAsync()", workflow);
        Assert.Contains("return succeeded && !_isShuttingDown();", workflow);
        Assert.Contains("if (!cloned || _isShuttingDown())", workflow);
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
