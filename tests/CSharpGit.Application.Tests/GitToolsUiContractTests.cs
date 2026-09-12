namespace CSharpGit.Application.Tests;

public sealed class GitToolsUiContractTests
{
    [Fact]
    public void GitToolsIsAFullSettingsPageBackedByGitConfiguration()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml"));
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "GitToolsSettingsViewModel.cs"));
        var contract = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IGitToolsService.cs"));
        var appSettings = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IAppSettingsService.cs"));

        Assert.Contains("Git Tools", xaml);
        Assert.Contains("EDITOR", xaml);
        Assert.Contains("DIFF TOOL", xaml);
        Assert.Contains("MERGE TOOL", xaml);
        Assert.Contains("Effective", xaml);
        Assert.Contains("Source", xaml);
        Assert.Contains("Origin", xaml);
        Assert.Contains("Global", xaml);
        Assert.Contains("Repository", xaml);
        Assert.Contains("Worktree", xaml);
        Assert.Contains("Open Test File", xaml);
        Assert.Contains("Test Diff Tool", xaml);
        Assert.Contains("Test Merge Tool", xaml);
        Assert.Contains("Remove override", xaml);
        Assert.DoesNotContain("Merge tool settings", mainXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConfigureMergeToolCommand", mainXaml);
        Assert.Contains("IGitToolsService", page);
        Assert.Contains("ReadAsync", contract);
        Assert.Contains("RemoveOverrideAsync", contract);
        Assert.Contains("RunExternalDiffAsync", contract);
        Assert.Contains("RunMergeToolForFileAsync", contract);
        Assert.Contains("RunMergeToolWorkflowAsync", contract);
        Assert.Contains("TestAsync", contract);
        Assert.Contains("GitToolWriteScope.Global", viewModel);
        Assert.Contains("GitToolWriteScope.Repository", viewModel);
        Assert.DoesNotContain("SelectedDiffPreset", appSettings);
        Assert.DoesNotContain("SelectedMergePreset", appSettings);
        Assert.DoesNotContain("core.editor", appSettings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExternalDiffAndMergeWorkflowsUseCentralGitToolsBoundary()
    {
        var root = FindRepositoryRoot();
        var fileOpening = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.FileOpening.cs"));
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "DesktopRepositoryWorkflowService.cs"));
        var gitTools = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitToolsService.cs"));
        var legacyRepositoryService = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitCliRepositoryService.cs"));

        Assert.Contains("Open in Diff Tool", fileOpening);
        Assert.Contains("RunExternalDiffAsync(repository, pair)", fileOpening);
        Assert.Contains("SettingsSection.GitTools", fileOpening);
        Assert.Contains("gitTools.RunMergeToolForFileAsync", workflow);
        Assert.Contains("gitTools.RunMergeToolWorkflowAsync", workflow);
        Assert.Contains("gitTools.OpenEditorAsync", workflow);
        Assert.Contains("\"difftool\", \"--gui\", \"--no-prompt\", \"--no-index\"", gitTools);
        Assert.Contains("\"mergetool\", \"--gui\", \"--no-prompt\"", gitTools);
        Assert.DoesNotContain("difftool.prompt", gitTools);
        Assert.DoesNotContain("mergetool.prompt", gitTools);
        Assert.DoesNotContain("mergetool.prompt", legacyRepositoryService);
        Assert.Contains("\"mergetool\", \"--gui\", \"--no-prompt\"", legacyRepositoryService);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
