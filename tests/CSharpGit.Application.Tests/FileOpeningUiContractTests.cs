namespace CSharpGit.Application.Tests;

public sealed class FileOpeningUiContractTests
{
    [Fact]
    public void DiffFileActionsShareOneResolverAndDesktopShellBoundary()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.FileOpening.cs"));
        var abstractions = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IRepositoryFileVersionService.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "App.xaml.cs"));
        var gitComposition = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitServiceCollectionExtensions.cs"));

        Assert.Contains("IRepositoryFileVersionService", abstractions);
        Assert.Contains("IDesktopShellService", abstractions);
        Assert.Contains("IRepositoryPathService", abstractions);
        Assert.Contains("ResolveCommitAsync", page);
        Assert.Contains("ResolveWorkingTreeAsync", page);
        Assert.Contains("ChangedFilesTree.RightTapped", page);
        Assert.Contains("ChangedFilesTree.DoubleTapped", page);
        Assert.Contains("UnstagedChangesList.RightTapped", page);
        Assert.Contains("StagedChangesList.DoubleTapped", page);
        Assert.Contains("Open original", page);
        Assert.Contains("Open changed", page);
        Assert.Contains("Open in Diff Tool", page);
        Assert.Contains("RevealDescription", page);
        Assert.DoesNotContain("Process.Start", page);
        Assert.DoesNotContain("ProcessStartInfo", page);
        Assert.DoesNotContain("RunGit", page);

        Assert.Contains("services.AddCSharpGitGit();", app);
        Assert.Contains("AddSingleton<IRepositoryFileVersionService>", gitComposition);
        Assert.Contains("new GitRepositoryFileVersionService", gitComposition);
        Assert.Contains("IDesktopShellService, DesktopShellService", app);
        Assert.Contains("IRepositoryWorkflowService, DesktopRepositoryWorkflowService", app);
        Assert.Contains("IHistoryService>(provider => provider.GetRequiredService<GitFileAwareHistoryService>()", gitComposition);
    }

    [Fact]
    public void HistoricalSnapshotsAreBinarySafeAtomicAndNeverUsedForReveal()
    {
        var root = FindRepositoryRoot();
        var versions = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitRepositoryFileVersionService.cs"));
        var executor = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitCommandExecutor.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.FileOpening.cs"));

        Assert.Contains("ExecuteToFileAsync", versions);
        Assert.Contains("StandardOutput.BaseStream.CopyToAsync", executor);
        Assert.Contains("FileMode.CreateNew", executor);
        Assert.Contains("File.Move(temporaryPath, finalPath)", versions);
        Assert.Contains("FileAttributes.ReadOnly", versions);
        Assert.Contains("repository.GitDirectory", versions);
        Assert.Contains("version.RevisionIdentity", versions);
        Assert.Contains("version.BlobId", versions);
        Assert.Contains("version.GitPath", versions);
        Assert.Contains("side.ToString()", versions);
        Assert.Contains("ResolveExistingWorkingTreeFile(repository, pair.RevealPath", page);
        Assert.DoesNotContain("RevealFileAsync(snapshot", page);
    }

    [Fact]
    public void ExistingConflictOpenUsesConfiguredGitEditor()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "DesktopRepositoryWorkflowService.cs"));

        Assert.Contains("IGitToolsService", workflow);
        Assert.Contains("IRepositoryPathService", workflow);
        Assert.Contains("pathService.ResolveExistingWorkingTreeFile", workflow);
        Assert.Contains("gitTools.OpenEditorAsync", workflow);
        Assert.DoesNotContain("shellService.OpenFileAsync", workflow);
        Assert.DoesNotContain("Process.Start", workflow);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
