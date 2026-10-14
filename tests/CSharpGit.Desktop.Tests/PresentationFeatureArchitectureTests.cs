namespace CSharpGit.Desktop.Tests;

public sealed class PresentationFeatureArchitectureTests
{
    [Fact]
    public void RepositoryMaintenanceIsFeatureOwnedAndUsesSharedGate()
    {
        var page = Read("src/CSharpGit.Presentation/MainPage.RepositoryMaintenance.cs");
        var workflow = Read("src/CSharpGit.Presentation/ViewModels/RepositoryMaintenanceViewModel.cs");
        var context = Read("src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.RepositoryMaintenance.cs");
        var dialog = Read("src/CSharpGit.Presentation/RepositoryMaintenanceDialog.cs");

        Assert.DoesNotContain("IRepositoryMaintenanceService", page);
        Assert.DoesNotContain("GarbageCollectAsync", page);
        Assert.Contains("_service.GarbageCollectAsync", workflow);
        Assert.Contains("_mutationGate.WaitAsync(0)", context);
        Assert.Contains("IsMaintenanceSessionCurrent(expectedRepository)", context);
        Assert.Contains("_mutationGate.Release()", context);
        Assert.DoesNotContain("new SemaphoreSlim", workflow);
        Assert.DoesNotContain("new SemaphoreSlim", dialog);
        Assert.DoesNotContain("IRepositoryMaintenanceService", dialog);
        Assert.Contains("_repositoryMaintenance.IsRunning", page);
    }

    [Fact]
    public void FileActionsAreBoundToCapturedImmutableRequests()
    {
        var page = Read("src/CSharpGit.Presentation/MainPage.FileOpening.cs");
        var feature = Read("src/CSharpGit.Presentation/ViewModels/FileActionsViewModel.cs");
        var context = Read("src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.FileActions.cs");
        Assert.DoesNotContain("ResolveCommitAsync(", page);
        Assert.DoesNotContain("ResolveWorkingTreeAsync(", page);
        Assert.DoesNotContain("RunExternalDiffAsync(", page);
        Assert.Contains("record CommitFileActionRequest", feature);
        Assert.Contains("record WorkingTreeFileActionRequest", feature);
        Assert.Contains("PreparedFileActions", feature);
        Assert.Contains("Equals(prepared.Request, request)", feature);
        Assert.Contains("IsMenuSelectionCurrent(request)", page);
        Assert.Contains("CaptureCommitRequest(node.Entry.File.Path)", page);
        Assert.Contains("CaptureWorkingTreeRequest(change, kind)", page);
        Assert.Contains("RepositorySessionLifetime", Read("src/CSharpGit.Presentation/ViewModels/FileActionsContext.cs"));
        Assert.Contains("TryAcquire", context);
    }

    [Fact]
    public void FeaturesAreXamlFreeAndExplicitlyComposed()
    {
        var maintenance = Read("src/CSharpGit.Presentation/ViewModels/RepositoryMaintenanceViewModel.cs");
        var files = Read("src/CSharpGit.Presentation/ViewModels/FileActionsViewModel.cs");
        var app = Read("src/CSharpGit.Presentation/App.xaml.cs");
        var main = Read("src/CSharpGit.Presentation/MainPage.RepositoryMaintenance.cs");
        foreach (var source in new[] { maintenance, files })
        {
            Assert.DoesNotContain("Microsoft.UI.Xaml", source);
            Assert.DoesNotContain("MainPage", source);
            Assert.DoesNotContain("ContentDialog", source);
            Assert.DoesNotContain("GetRequiredService", source);
        }
        Assert.Contains("services.AddTransient<RepositoryMaintenanceViewModel>();", app);
        Assert.Contains("services.AddTransient<FileActionsViewModel>();", app);
        Assert.Contains("_repositoryMaintenance.Attach(_viewModel", main);
        Assert.Contains("_worktreesViewModel.RefreshAsync(throwOnError: true)", main);
        Assert.DoesNotContain("GetRequiredService<WorktreesViewModel>", maintenance);
    }

    [Fact]
    public void FeatureLifecycleHasSinglePageOwner()
    {
        var lifecycle = Read("src/CSharpGit.Presentation/MainPage.Lifecycle.cs");
        Assert.Contains("_fileActions.Dispose()", lifecycle);
        Assert.Contains("_repositoryMaintenance.Dispose()", lifecycle);
        Assert.Contains("_fileActions.PropertyChanged -= FileActions_PropertyChanged", lifecycle);
        Assert.Contains("_repositoryMaintenance.PropertyChanged -= RepositoryMaintenance_PropertyChanged", lifecycle);
    }

    [Fact]
    public void ExternalDiffConfigurationIsTyped()
    {
        var git = Read("src/CSharpGit.Git/ExternalGitToolService.cs");
        var feature = Read("src/CSharpGit.Presentation/ViewModels/FileActionsViewModel.cs");
        var page = Read("src/CSharpGit.Presentation/MainPage.FileOpening.cs");
        Assert.Contains("throw new GitToolNotConfiguredException(displayName)", git);
        Assert.Contains("catch (GitToolNotConfiguredException)", feature);
        Assert.DoesNotContain("Message.Contains(\"Diff tool is not configured\"", page);
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
