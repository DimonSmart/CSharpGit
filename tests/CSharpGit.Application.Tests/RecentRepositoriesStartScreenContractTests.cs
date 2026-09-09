namespace CSharpGit.Application.Tests;

public sealed class RecentRepositoriesStartScreenContractTests
{
    [Fact]
    public void RecentRepositoryLauncherPreservesEmptyStateAndTracksRepositoryMetadata()
    {
        var root = FindRepositoryRoot();
        var settingsContract = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IAppSettingsService.cs"));
        var settingsPersistence = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Infrastructure", "JsonAppSettingsService.cs"));
        var mainPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var recentView = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "RecentRepositoriesView.xaml"));
        var recentViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RecentRepositoriesViewModel.cs"));
        var integration = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RecentRepositories.cs"));

        Assert.Contains("RecentRepositorySettings", settingsContract);
        Assert.Contains("LastOpenedUtc", settingsContract);
        Assert.Contains("LastBranchName", settingsContract);
        Assert.Contains("MaxRecentRepositories = 8", settingsPersistence);
        Assert.Contains("Take(MaxRecentRepositories)", settingsPersistence);
        Assert.Contains("PathComparer.Equals", settingsPersistence);

        Assert.Contains("Open a Git repository", mainPage);
        Assert.Contains("Select the folder of an existing repository or worktree.", mainPage);
        Assert.Contains("Recent repositories", recentView);
        Assert.Contains("Open repository", recentView);
        Assert.Contains("RemoveCommand", recentView);
        Assert.Contains("TileOpacity", recentView);
        Assert.Contains("VariableSizedWrapGrid", recentView);
        Assert.DoesNotContain("ItemsWrapGrid", recentView);
        Assert.Contains("Folder not found", recentViewModel);

        Assert.Contains("Directory.Exists(item.Path)", integration);
        Assert.Contains("RecordRecentRepositoryAsync", integration);
        Assert.Contains("branch.IsCurrent", integration);
        Assert.DoesNotContain("RemoveRecentRepositoryAsync(item.Path)", integration);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
