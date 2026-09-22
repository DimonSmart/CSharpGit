namespace CSharpGit.Application.Tests;

public sealed class RepositoryCreationUiContractTests
{
    [Fact]
    public void RepositoryCreationUsesOneWorkflowFromAllRequiredEntryPoints()
    {
        var root = FindRepositoryRoot();
        var mainXaml = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var recentXaml = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "RecentRepositoriesView.xaml"));
        var recentViewModel = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "ViewModels", "RecentRepositoriesViewModel.cs"));
        var recentIntegration = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.RecentRepositories.cs"));
        var workflow = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.RepositoryCreation.cs"));

        Assert.Contains("Select folder…", mainXaml);
        Assert.Contains("Create repository…", mainXaml);
        Assert.Contains("CreateRepository_Click", mainXaml);
        Assert.Contains("Create repository", recentXaml);
        Assert.Contains("Create a new local repository…", recentXaml);
        Assert.Contains("CreateRepositoryCommand", recentXaml);
        Assert.Contains("CreateRepositoryCommand", recentViewModel);
        Assert.Contains("ShowCreateRepositoryAsync", recentIntegration);
        Assert.Contains("ShowCreateRepositoryAsync", workflow);
        Assert.Equal(1, Count(workflow, "private async Task ShowCreateRepositoryAsync()"));
    }

    [Fact]
    public void CreateDialogExposesPersonalAndCentralRepositorySemantics()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.RepositoryCreation.cs"));
        var viewModel = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "ViewModels", "CreateRepositoryViewModel.cs"));

        Assert.Contains("Create new repository", workflow);
        Assert.Contains("Header = \"Directory\"", workflow);
        Assert.Contains("Browse…", workflow);
        Assert.Contains("Personal repository", workflow);
        Assert.Contains("Central repository, no working directory", workflow);
        Assert.Contains("--bare --shared=all", workflow);
        Assert.Contains("PrimaryButtonText = \"Create\"", workflow);
        Assert.Contains("CloseButtonText = \"Cancel\"", workflow);
        Assert.Contains("DefaultButton = ContentDialogButton.Primary", workflow);
        Assert.Contains("IsPrimaryButtonEnabled", workflow);

        Assert.Contains(
            "RepositoryCreationKind.WorkingTree",
            viewModel);
        Assert.Contains("IRepositoryCreationService", viewModel);
        Assert.Contains("IFolderPicker", viewModel);
        Assert.DoesNotContain("CSharpGit.Git", viewModel);
    }

    [Fact]
    public void PersonalCreationProtectsDraftAndUsesPathBasedWorkspaceSwitch()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.RepositoryCreation.cs"));
        var openViewModel = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var mainPage = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));

        Assert.Contains("Discard commit message?", workflow);
        Assert.Contains(
            "Creating and opening another repository will discard the current commit message.",
            workflow);
        Assert.Contains("Keep editing", workflow);
        Assert.Contains("Discard and continue", workflow);
        Assert.True(
            workflow.IndexOf(
                "ConfirmDiscardCommitMessageForRepositorySwitchAsync",
                StringComparison.Ordinal)
            < workflow.IndexOf(
                "_createRepositoryViewModel.CreateAsync()",
                StringComparison.Ordinal));

        Assert.Contains("OpenRepositoryPathAsync", workflow);
        Assert.Contains("OpenRepositoryPathAsync", openViewModel);
        Assert.DoesNotContain("QueuePath", workflow);
        Assert.Contains(
            "Repository was created successfully, but CSharpGit could not open it.",
            workflow);

        Assert.Contains("CurrentBranchName", openViewModel);
        Assert.Contains("CurrentBranchName", mainPage);
        Assert.Contains("?? _viewModel.CurrentBranchName", mainPage);
    }

    [Fact]
    public void CentralCreationDoesNotOpenOrRecordTheRepository()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root, "src", "CSharpGit.Presentation", "MainPage.RepositoryCreation.cs"));

        Assert.Contains(
            "_createRepositoryViewModel.RepositoryType",
            workflow);
        Assert.Contains(
            "RepositoryCreationKind.WorkingTree",
            workflow);
        Assert.Contains(
            "if (!opensWorkspace)",
            workflow);
        Assert.Contains(
            "Repository created successfully.",
            workflow);
    }

    private static int Count(string value, string fragment) =>
        (value.Length - value.Replace(
             fragment,
             string.Empty,
             StringComparison.Ordinal).Length)
        / fragment.Length;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(
                   directory.FullName,
                   "CSharpGit.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException(
                   "CSharpGit repository root was not found.");
    }
}
