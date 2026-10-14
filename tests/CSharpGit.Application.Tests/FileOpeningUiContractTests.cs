namespace CSharpGit.Application.Tests;

public sealed class FileOpeningUiContractTests
{
    [Fact]
    public void DiffFileActionsShareOneResolverAndDesktopShellBoundary()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.FileOpening.cs"));
        var feature = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "FileActionsViewModel.cs"));
        var abstractions = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IRepositoryFileVersionService.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "App.xaml.cs"));
        var gitComposition = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitServiceCollectionExtensions.cs"));

        Assert.Contains("IRepositoryFileVersionService", abstractions);
        Assert.Contains("IDesktopShellService", abstractions);
        Assert.Contains("OpenFolderDescription", abstractions);
        Assert.Contains("IRepositoryPathService", abstractions);
        Assert.DoesNotContain("ResolveCommitAsync", page);
        Assert.DoesNotContain("ResolveWorkingTreeAsync", page);
        Assert.Contains("_versions.ResolveCommitAsync", feature);
        Assert.Contains("_versions.ResolveWorkingTreeAsync", feature);
        Assert.Contains("new DiffFileVersionPathResolver", feature);
        Assert.Contains("_shell.OpenFileAsync", feature);
        Assert.Contains("_tools.RunExternalDiffAsync", feature);
        Assert.Contains("FileActionRequest", page);
        Assert.Contains("ChangedFilesTree.RightTapped", page);
        Assert.Contains("ChangedFilesTree.DoubleTapped", page);
        Assert.Contains("UnstagedChangesList.RightTapped", page);
        Assert.Contains("StagedChangesList.DoubleTapped", page);
        Assert.Contains("Open original", page);
        Assert.Contains("Open changed", page);
        Assert.Contains("Open in Diff Tool", page);
        Assert.Contains("RevealDescription", page);
        Assert.Contains("RevealDescription", feature);
        Assert.DoesNotContain("Process.Start", page);
        Assert.DoesNotContain("ProcessStartInfo", page);
        Assert.DoesNotContain("RunGit", page);

        Assert.Contains("services.AddCSharpGitGit();", app);
        Assert.Contains("AddSingleton<IRepositoryFileVersionService>", gitComposition);
        Assert.Contains("new GitRepositoryFileVersionService", gitComposition);
        Assert.Contains("IDesktopShellService, DesktopShellService", app);
        Assert.DoesNotContain("DesktopRepositoryWorkflowService", app);
        Assert.DoesNotContain("AddSingleton<IRepositoryWorkflowService>", gitComposition);
        Assert.Contains("AddSingleton<IInteractiveRebaseService>", gitComposition);
        Assert.Contains("AddSingleton<IRepositoryOperationService>", gitComposition);
        Assert.Contains("AddSingleton<IHistoryService>", gitComposition);
        Assert.Contains("GetRequiredService<GitHistoryService>()", gitComposition);
    }

    [Fact]
    public void HistoricalSnapshotsAreBinarySafeAtomicAndNeverUsedForReveal()
    {
        var root = FindRepositoryRoot();
        var versions = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitRepositoryFileVersionService.cs"));
        var feature = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "FileActionsViewModel.cs"));
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
        Assert.Contains("ResolveExistingWorkingTreeFile", feature);
        Assert.Contains("prepared.Pair.RevealPath", feature);
        Assert.DoesNotContain("RevealFileAsync(snapshot", feature);
    }

    [Fact]
    public void ExistingConflictOpenUsesConfiguredGitEditor()
    {
        var root = FindRepositoryRoot();
        var gitTools = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "ExternalGitToolService.cs"));
        var repositoryOperations = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "RepositoryOperationsViewModel.cs"));

        Assert.Contains("IRepositoryPathService", gitTools);
        Assert.Contains("ResolveExistingWorkingTreeFile", gitTools);
        Assert.Contains("OpenConflictInEditorAsync", gitTools);
        Assert.Contains("OpenEditorAsync", gitTools);
        Assert.Contains("_externalGitToolService.OpenConflictInEditorAsync", repositoryOperations);
        Assert.DoesNotContain("Process.Start", gitTools);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
